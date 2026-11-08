using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BayatGames.SaveGameFree.Serializers;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UtilityScripts;
using Traits;

namespace TruePlanet
{
    internal sealed class PortalMemory
    {
        public int HomeTile;
        public string HomeSettlement, HomeStructure, DeployedAtStructure, Territory;
        public Dictionary<int, int> NativeIdsByTile = new Dictionary<int, int>();
        // Native relationship keys are map-local integers, not character identities.
        public Dictionary<string, JToken> Relationships = new Dictionary<string, JToken>();
        public Dictionary<string, JToken> SharedOpinions = new Dictionary<string, JToken>();
        public string[] OpinionCharacters = Array.Empty<string>();
    }
    internal sealed class PortalPayload
    {
        public int FormatVersion = 1, SourceTile;
        public string CampaignId, WorldId, PlayerFaction;
        public GameDate Date;
        public string[] Actors, Cargo;
        public JObject NativeGraph;
        public Dictionary<string, PortalMemory> Memories = new Dictionary<string, PortalMemory>();
    }
    internal static class PortalTransport
    {
        private static readonly System.Reflection.FieldInfo HubField = AccessTools.Field(typeof(SaveCurrentProgressManager), "<currentSaveDataProgress>k__BackingField");
        private static readonly System.Reflection.FieldInfo CharacterCounter = AccessTools.Field(typeof(Utilities), "_lastCharacterID");
        private static readonly System.Reflection.FieldInfo ItemCounter = AccessTools.Field(typeof(Utilities), "_lastTileObjectID");
        private static readonly System.Reflection.FieldInfo FactionCounter = AccessTools.Field(typeof(Utilities), "_lastFactionID");

        private static readonly Dictionary<string, LocationStructure> WalkTargets = new Dictionary<string, LocationStructure>(StringComparer.Ordinal);
        private static readonly CharacterBehaviour WalkBehaviour = new PortalWalkBehaviour();
        private static readonly Func<BehaviourComponent, CharacterBehaviour, bool> AddWalkBehaviour =
            AccessTools.MethodDelegate<Func<BehaviourComponent, CharacterBehaviour, bool>>(AccessTools.Method(typeof(BehaviourComponent), "AddBehaviourComponent", new[] { typeof(CharacterBehaviour) }));
        private static readonly Func<BehaviourComponent, CharacterBehaviour, bool> RemoveWalkBehaviour =
            AccessTools.MethodDelegate<Func<BehaviourComponent, CharacterBehaviour, bool>>(AccessTools.Method(typeof(BehaviourComponent), "RemoveBehaviourComponent", new[] { typeof(CharacterBehaviour) }));

        internal static void Install(Harmony harmony)
        {
            harmony.CreateClassProcessor(typeof(SaveNativeWalkBehaviours)).Patch();
            harmony.CreateClassProcessor(typeof(EndNativeWalks)).Patch();
        }
        internal static void BeginPortalWalk(Character actor, LocationStructure portal)
        {
            if (WalkTargets.TryGetValue(actor.persistentID, out LocationStructure current) && ReferenceEquals(current, portal)) return;
            if (ReachablePortalTile(actor, portal) == null) throw new InvalidOperationException("The expedition cannot reach its Portal.");
            WalkTargets[actor.persistentID] = portal;
            AddWalkBehaviour(actor.behaviourComponent, WalkBehaviour);
        }
        internal static void EndPortalWalk(Character actor)
        {
            WalkTargets.Remove(actor.persistentID);
            if (!actor.hasBeenCleanedUp) RemoveWalkBehaviour(actor.behaviourComponent, WalkBehaviour);
        }
        private sealed class PortalWalkBehaviour : CharacterBehaviour
        {
            public PortalWalkBehaviour() { priority = 30; }
            public override bool TryDoBehaviour(Character character, ref string log, out JobQueueItem producedJob)
            {
                producedJob = null;
                if (!WalkTargets.TryGetValue(character.persistentID, out LocationStructure portal)) return false;
                // Native interruptions may cancel a walk. Plan it again only when native
                // movement/job gates allow it; hold arrivals instead of idle wandering.
                if (character.gridTileLocation != null && portal.tiles.Contains(character.gridTileLocation)) return true;
                if (!character.limiterComponent.canPerform || !character.limiterComponent.canMove || !character.limiterComponent.canTakeJobs
                    || character.jobQueue.HasJob(JOB_TYPE.GO_TO)) return true;
                LocationGridTile exit = ReachablePortalTile(character, portal);
                if (exit != null) character.jobComponent.CreateGoToSpecificTileJob(exit, out producedJob);
                return true;
            }
        }
        [HarmonyPatch(typeof(SaveDataBehaviourComponent), nameof(SaveDataBehaviourComponent.Save))]
        private static class SaveNativeWalkBehaviours
        {
            private static void Postfix(SaveDataBehaviourComponent __instance)
            {
                // Mission records restore committed walks; uncommitted assembly remains
                // owned by its source. Never serialize a process-local behaviour instance.
                __instance.currentBehaviourComponents.Remove(typeof(PortalWalkBehaviour).FullName);
            }
        }
        [HarmonyPatch(typeof(GameManager), "OnDestroy")]
        private static class EndNativeWalks
        {
            private static void Prefix() { WalkTargets.Clear(); }
        }

        internal static LocationGridTile ReachablePortalTile(Character actor, LocationStructure portal)
        {
            foreach (LocationGridTile tile in portal.passableTiles)
                if (actor.movementComponent.HasPathToEvenIfDiffRegion(tile)) return tile;
            return null;
        }

        internal static PortalPayload Capture(IEnumerable<Character> travelling)
        {
            TileContext tile = TileGame.Active ?? throw new InvalidOperationException("No campaign map is active.");
            Character[] actors = travelling.Distinct().ToArray();
            if (actors.Length == 0 || actors.Any(actor => actor == null || actor.isDead || !actor.hasMarker))
                throw new InvalidOperationException("Only living, present actors can enter a portal.");
            var characters = new Dictionary<string, Character>(StringComparer.Ordinal);
            var items = new Dictionary<string, TileObject>(StringComparer.Ordinal);
            var cargo = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<Character>(actors);
            while (queue.Count != 0)
            {
                Character actor = queue.Dequeue();
                if (characters.ContainsKey(actor.persistentID)) continue;
                characters.Add(actor.persistentID, actor);
                foreach (TileObject item in actor.items.Concat(actor.ownedItems).Concat(actor.equipmentComponent.allEquipments)) items[item.persistentID] = item;
                if (actor.carryComponent.carriedPOI is Character carried)
                { cargo.Add(carried.persistentID); queue.Enqueue(carried); }
                else if (actor.carryComponent.carriedPOI is TileObject carriedItem)
                { cargo.Add(carriedItem.persistentID); items[carriedItem.persistentID] = carriedItem; }
                if (actor.mountComponent.mountedCharacter != null)
                { cargo.Add(actor.mountComponent.mountedCharacter.persistentID); queue.Enqueue(actor.mountComponent.mountedCharacter); }
            }
            foreach (Character actor in actors) cargo.Remove(actor.persistentID);
            var graph = new SaveDataCurrentProgress(); graph.Initialize();
            var saver = SaveManager.Instance.saveCurrentProgressManager;
            if (saver.isSaving || saver.isWritingToDisk) throw new InvalidOperationException("Finish saving before transferring actors.");
            object previous = HubField.GetValue(saver); HubField.SetValue(saver, graph);
            JObject native;
            try
            {
                foreach (Character actor in characters.Values)
                {
                    graph.AddToSaveHub(actor);
                    if (actor.faction != null) graph.AddToSaveHub(actor.faction);
                    if (actor.relationshipContainer is BaseRelationshipContainer relationships)
                        foreach (IRelationshipData relationship in relationships.relationships.Values)
                            foreach (SharedOpinionModifier opinion in relationship.opinions.sharedOpinions) graph.AddToSaveHub(opinion);
                }
                foreach (TileObject item in items.Values) graph.AddToSaveHub(item);
                using (var stream = new MemoryStream())
                {
                    new SaveGameJsonSerializer().Serialize(graph, stream, Encoding.UTF8);
                    using (var input = new MemoryStream(stream.ToArray()))
                    using (var reader = new StreamReader(input, Encoding.UTF8, true))
                    using (var json = new JsonTextReader(reader)) native = JObject.Load(json);
                }
            }
            finally { HubField.SetValue(saver, previous); }
            var payload = new PortalPayload { CampaignId = tile.CampaignId, WorldId = tile.Planet.Id, SourceTile = tile.TileId,
                PlayerFaction = PlayerManager.Instance.player.playerFaction.persistentID, Date = GameManager.Instance.Today(),
                Actors = actors.Select(actor => actor.persistentID).ToArray(), Cargo = cargo.ToArray(), NativeGraph = native };
            var identities = CharacterManager.Instance.allCharacters.Concat(CharacterManager.Instance.limboCharacters)
                .ToDictionary(actor => actor.id.ToString(System.Globalization.CultureInfo.InvariantCulture), actor => actor.persistentID);
            foreach (Character actor in characters.Values)
            {
                PortalMemory memory;
                if (tile.PortalMemories.TryGetValue(actor.persistentID, out PortalMemory remembered))
                    memory = JsonConvert.DeserializeObject<PortalMemory>(JsonConvert.SerializeObject(remembered));
                else memory = new PortalMemory { HomeTile = tile.TileId,
                    HomeSettlement = actor.homeSettlement?.persistentID, HomeStructure = actor.homeStructure?.persistentID,
                    DeployedAtStructure = actor.deployedAtStructure?.persistentID, Territory = actor.territory?.persistentID };
                memory.NativeIdsByTile[tile.TileId] = actor.id;
                var relationship = (JObject)NativeHub(native, "Character")[actor.persistentID]["saveDataBaseRelationshipContainer"];
                foreach (string identity in identities.Values)
                { memory.Relationships.Remove(identity); memory.SharedOpinions.Remove(identity); }
                CaptureRelations(relationship["relationships"], identities, memory.Relationships);
                CaptureRelations(relationship["sharedOpinionModifiers"], identities, memory.SharedOpinions);
                memory.OpinionCharacters = memory.OpinionCharacters.Where(identity => !identities.ContainsValue(identity))
                    .Union(((JArray)relationship["charactersWithOpinion"]).Values<string>()).ToArray();
                payload.Memories.Add(actor.persistentID, memory);
            }
            Validate(payload); return payload;
        }
        private static void CaptureRelations(JToken native, Dictionary<string, string> identities, Dictionary<string, JToken> remembered)
        {
            foreach (JToken relation in native)
                if (identities.TryGetValue((string)relation["Key"], out string identity)) remembered[identity] = relation["Value"].DeepClone();
        }
        internal static Character[] Arrive(PortalPayload payload, LocationStructure portal)
        {
            Validate(payload);
            TileContext tile = TileGame.Active ?? throw new InvalidOperationException("No destination map is active.");
            if (payload.CampaignId != tile.CampaignId || payload.WorldId != tile.Planet.Id || portal == null
                || portal != PlayerManager.Instance.player.playerSettlement.GetFirstStructureOfType(STRUCTURE_TYPE.THE_PORTAL))
                throw new InvalidDataException("Portal arrival does not match the campaign or its actual local anchor.");
            LocationGridTile arrival = portal.GetRandomPassableTile();
            if (arrival == null || !arrival.IsPassable()) throw new InvalidOperationException("The arrival portal is blocked.");
            JObject native = (JObject)payload.NativeGraph.DeepClone();
            string playerFaction = PlayerManager.Instance.player.playerFaction.persistentID;
            ReplaceIdentity(native, payload.PlayerFaction, playerFaction);
            JObject characters = NativeHub(native, "Character"), items = NativeHub(native, "Tile_Object"), factions = NativeHub(native, "Faction");
            // Jobs, interrupts and parties are deliberately ended at the physical transfer boundary.
            // Their original native records remain in the immutable payload, not in the receiving scheduler.
            foreach (string category in new[] { "Job", "Interrupt", "Party", "Party_Quest", "Crime", "Gathering", "Game_Alert" })
                NativeHub(native, category).RemoveAll();
            // Rumor history references actions by persistent ID. Retain the native records,
            // but detach their ended source jobs before resolving receiving-map references.
            foreach (JProperty record in NativeHub(native, "Action").Properties())
                record.Value["associatedJobID"] = null;
            factions.Remove(playerFaction);
            var singletons = (Dictionary<string, Trait>)AccessTools.Field(typeof(TraitManager), "instancedSingletonTraits").GetValue(TraitManager.Instance);
            foreach (JProperty record in NativeHub(native, "Trait").Properties().ToArray())
                if (singletons.TryGetValue((string)record.Value["name"], out Trait singleton))
                {
                    // Native status expiry unregisters a singleton but leaves it cached.
                    // Reuse that instance without losing the incoming status reference.
                    if (!DatabaseManager.Instance.traitDatabase.traitsByGUID.ContainsKey(singleton.persistentID))
                        DatabaseManager.Instance.traitDatabase.RegisterTrait(singleton);
                    ReplaceIdentity(native, record.Name, singleton.persistentID);
                    NativeHub(native, "Trait").Remove(singleton.persistentID);
                }
            int characterId = (int)CharacterCounter.GetValue(null), itemId = (int)ItemCounter.GetValue(null), factionId = (int)FactionCounter.GetValue(null);
            var localIds = CharacterManager.Instance.allCharacters.Concat(CharacterManager.Instance.limboCharacters)
                .ToDictionary(actor => actor.persistentID, actor => actor.id, StringComparer.Ordinal);
            foreach (JProperty record in characters.Properties())
            {
                if (localIds.ContainsKey(record.Name)) throw new InvalidDataException("A transferred actor is already owned by the destination.");
                PortalMemory memory = payload.Memories[record.Name];
                int id = memory.NativeIdsByTile.TryGetValue(tile.TileId, out int previousId) ? previousId : ++characterId;
                if (localIds.Values.Contains(id)) throw new InvalidDataException("A returning actor's native identity is already occupied.");
                localIds.Add(record.Name, id); ((JObject)record.Value)["id"] = id;
            }
            foreach (JProperty record in items.Properties())
            {
                if (DatabaseManager.Instance.tileObjectDatabase.GetTileObjectByPersistentIDSafe(record.Name) != null)
                    throw new InvalidDataException("A transferred item is already owned by the destination.");
                ((JObject)record.Value)["id"] = ++itemId;
            }
            foreach (JProperty record in factions.Properties().ToArray())
            {
                if (FactionManager.Instance.GetFactionByPersistentID(record.Name) != null) { record.Remove(); continue; }
                var faction = (JObject)record.Value; faction["id"] = ++factionId;
                // The source quest hub was detached with its scheduler, not transferred.
                faction["partyQuestBoard"]["availablePartyQuests"] = new JArray();
                // Off-map diplomatic records stay in the payload; never bind them to an unrelated local faction.
                var relations = (JObject)faction["relationships"];
                foreach (JProperty relation in relations.Properties().ToArray())
                    if (FactionManager.Instance.GetFactionByPersistentID(relation.Name) == null && factions[relation.Name] == null) relation.Remove();
            }
            long dateOffset = DateTicks(GameManager.Instance.Today()) - DateTicks(payload.Date);
            ShiftDates(native, dateOffset);
            foreach (JProperty record in characters.Properties())
            {
                var actor = (JObject)record.Value; PortalMemory memory = payload.Memories[record.Name];
                actor["worldPos"] = new JObject { { "x", arrival.centeredWorldLocation.x }, { "y", arrival.centeredWorldLocation.y }, { "z", arrival.centeredWorldLocation.z } };
                actor["currentStructure"] = portal.persistentID; actor["territory"] = null; actor["deployedAtStructure"] = null;
                actor["homeStructure"] = ExistingStructure(memory.HomeStructure)?.persistentID;
                actor["homeSettlement"] = memory.HomeSettlement != null && DatabaseManager.Instance.settlementDatabase.GetSettlementByPersistentIDSafe(memory.HomeSettlement) != null ? memory.HomeSettlement : null;
                if ((string)actor["faction"] == playerFaction && (string)actor["homeStructure"] == null) actor["homeStructure"] = portal.persistentID;
                actor["currentJob"] = null; actor["currentActionNode"] = null; actor["jobs"] = new JArray();
                actor["pendingTopPriorityJobs"] = new JArray(); actor["forceCancelJobsOnTickEnded"] = new JArray();
                RemovePreviousPartyBehaviour(payload.NativeGraph, actor);
                actor["partyComponent"]["currentParty"] = null; actor["gatheringComponent"]["currentGathering"] = null;
                actor["interruptComponent"]["currentInterruptID"] = null; actor["interruptComponent"]["triggeredSimultaneousInterruptID"] = null;
                foreach (string list in new[] { "hostileCharactersInRange", "hostileTileObjectsInRange", "avoidCharactersInRange", "avoidTileObjectsInRange" }) actor["combatComponent"][list] = new JArray();
                foreach (string list in new[] { "witnessedCrimes", "reportedCrimes", "activeCrimes", "previousCrimes" }) actor["crimeComponent"][list] = new JArray();
                actor["isInLimbo"] = false; actor["isLimboCharacter"] = false; actor["hasMarker"] = true; actor["state"] = POI_STATE.ACTIVE.ToString();
                var relationships = (JObject)actor["saveDataBaseRelationshipContainer"];
                relationships["relationships"] = ProjectRelations(memory.Relationships, localIds);
                relationships["sharedOpinionModifiers"] = ProjectRelations(memory.SharedOpinions, localIds);
                relationships["charactersWithOpinion"] = new JArray(memory.OpinionCharacters.Where(localIds.ContainsKey));
            }
            PruneEndedActions(native);
            SaveDataCurrentProgress imported;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(native.ToString(Formatting.None))))
                imported = new SaveGameJsonSerializer().Deserialize<SaveDataCurrentProgress>(stream, Encoding.UTF8);
            if (imported == null) throw new InvalidDataException("The native actor transfer graph could not be read.");
            var savedActors = ((SaveDataCharacterHub)imported.objectHub[OBJECT_TYPE.Character]).hub;
            var savedItems = ((SaveDataTileObjectHub)imported.objectHub[OBJECT_TYPE.Tile_Object]).hub;
            var savedFactions = ((SaveDataFactionHub)imported.objectHub[OBJECT_TYPE.Faction]).hub;
            var savedTraits = ((SaveDataTraitHub)imported.objectHub[OBJECT_TYPE.Trait]).hub;
            var loadedTraits = new List<Trait>(savedTraits.Count);
            var savedActions = ((SaveDataActionHub)imported.objectHub[OBJECT_TYPE.Action]).hub;
            var loadedActions = new List<ActualGoapNode>(savedActions.Count);
            var context = new SaveDataCurrentProgress(); context.Initialize();
            foreach (var hub in imported.objectHub) context.objectHub[hub.Key] = hub.Value;
            var saver = SaveManager.Instance.saveCurrentProgressManager;
            if (saver.isSaving || saver.isWritingToDisk) throw new InvalidOperationException("Finish saving before portal arrival.");
            object previous = HubField.GetValue(saver); HubField.SetValue(saver, context);
            try
            {
                context.SavePlayer();
                foreach (SaveDataFaction faction in savedFactions.Values) faction.Load();
                foreach (SaveDataTrait trait in savedTraits.Values) loadedTraits.Add(trait.Load());
                foreach (SaveDataSharedOpinionModifier opinion in ((SaveDataSharedOpinionModifierHub)imported.objectHub[OBJECT_TYPE.Shared_Opinion_Modifier]).hub.Values)
                    if (!DatabaseManager.Instance.sharedOpinionDatabase.allSharedOpinionModifiers.ContainsKey(opinion.persistentID)) opinion.Load();
                foreach (SaveDataTileObject item in savedItems.Values) item.Load();
                foreach (SaveDataCharacter actor in savedActors.Values) actor.Load();
                foreach (SaveDataActualGoapNode data in savedActions.Values)
                    if (!DatabaseManager.Instance.actionDatabase.allActions.ContainsKey(data.persistentID))
                    {
                        ActualGoapNode action = data.Load();
                        DatabaseManager.Instance.actionDatabase.AddAction(action); loadedActions.Add(action);
                    }
                foreach (Trait trait in loadedTraits) trait.LoadSecondWaveInstancedTrait(savedTraits[trait.persistentID]);
                foreach (SaveDataFaction faction in savedFactions.Values) FactionManager.Instance.GetFactionByPersistentID(faction.persistentID).LoadReferences(faction);
                foreach (SaveDataTileObject item in savedItems.Values) DatabaseManager.Instance.tileObjectDatabase.GetTileObjectByPersistentID(item.persistentID).LoadSecondWave(item);
                foreach (ActualGoapNode action in loadedActions) action.LoadReferences(savedActions[action.persistentID]);
                foreach (ActualGoapNode action in loadedActions) action.LoadAdditionalReferences(savedActions[action.persistentID]);
                foreach (SaveDataCharacter data in savedActors.Values) CharacterManager.Instance.GetCharacterByPersistentID(data.persistentID).LoadReferences(data);
                foreach (SaveDataCharacter data in savedActors.Values)
                {
                    Character actor = CharacterManager.Instance.GetCharacterByPersistentID(data.persistentID);
                    actor.LoadReferencesMainThread(data);
                    actor.dailyScheduleComponent.OnCharacterLeftParty(actor);
                    if (actor.faction != null && !actor.faction.characters.Contains(actor)) actor.faction.characters.Add(actor);
                    tile.PortalMemories[actor.persistentID] = payload.Memories[actor.persistentID];
                }
                foreach (SaveDataFaction faction in savedFactions.Values) FactionManager.Instance.GetFactionByPersistentID(faction.persistentID).LoadReferencesMainThread(faction);
                foreach (SaveDataTileObject item in savedItems.Values) DatabaseManager.Instance.tileObjectDatabase.GetTileObjectByPersistentID(item.persistentID).LoadAdditionalInfo(item);
                foreach (Trait trait in loadedTraits) if (trait.shouldBeLoadedInMainThread)
                {
                    trait.LoadTraitSecondWaveInMainThread(savedTraits[trait.persistentID]);
                    DatabaseManager.Instance.traitDatabase.traitsToBeLoadedOnMainThread.Remove(trait);
                }
            }
            finally { HubField.SetValue(saver, previous); }
            return payload.Actors.Select(id => CharacterManager.Instance.GetCharacterByPersistentID(id)).ToArray();
        }
        private static void PruneEndedActions(JObject native)
        {
            JObject actions = NativeHub(native, "Action");
            var retained = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Queue<string>();
            // Current jobs/actions were detached above. Keep the closure of native history
            // references, not unrelated walk nodes pointing at source-map floor objects.
            foreach (JProperty category in ((JObject)native["objectHub"]).Properties())
                if (category.Name != "Action") FindActionReferences((JContainer)category.Value, actions, retained, pending);
            while (pending.Count != 0)
                FindActionReferences((JContainer)actions[pending.Dequeue()], actions, retained, pending);
            foreach (JProperty action in actions.Properties().ToArray())
                if (!retained.Contains(action.Name)) action.Remove();
        }
        private static void FindActionReferences(JContainer root, JObject actions, HashSet<string> retained, Queue<string> pending)
        {
            foreach (JToken value in root.Descendants())
                if (value.Type == JTokenType.String)
                {
                    string id = (string)value;
                    if (id != null && actions[id] != null && retained.Add(id)) pending.Enqueue(id);
                }
        }
        internal static PortalPayload CaptureReturn(Character[] survivors, string captiveId, LocationStructure portal)
        {
            if (survivors.Length == 0) return null;
            Character captive = captiveId == null ? null : CharacterManager.Instance.GetCharacterByPersistentID(captiveId);
            Prisoner prisoner = captive?.traitContainer.GetTraitOrStatus<Prisoner>("Prisoner");
            bool returningCaptive = captive != null && !captive.isDead && prisoner != null
                && prisoner.IsFactionPrisonerOf(PlayerManager.Instance.player.playerFaction)
                && (captive.gridTileLocation != null && portal.tiles.Contains(captive.gridTileLocation)
                    || captive.isBeingCarriedBy != null && survivors.Contains(captive.isBeingCarriedBy));
            return Capture(returningCaptive ? survivors.Concat(new[] { captive }) : survivors);
        }
        private static void RemovePreviousPartyBehaviour(JObject original, JObject actor)
        {
            string partyId = (string)actor["partyComponent"]["currentParty"];
            string questId = partyId == null ? null : (string)NativeHub(original, "Party")[partyId]?["currentQuest"];
            string related = questId == null ? null : (string)NativeHub(original, "Party_Quest")[questId]?["relatedBehaviour"];
            var behaviours = (JArray)actor["behaviourComponent"]["currentBehaviourComponents"];
            for (int index = behaviours.Count - 1; index >= 0; index--)
                if ((string)behaviours[index] == typeof(PartyBehaviour).FullName || related != null && (string)behaviours[index] == related)
                    behaviours.RemoveAt(index);
        }
        internal static void Depart(PortalPayload payload)
        {
            Validate(payload);
            TileContext tile = TileGame.Active;
            if (tile == null || tile.CampaignId != payload.CampaignId || tile.TileId != payload.SourceTile)
                throw new InvalidDataException("Departure is not owned by the payload's source map.");
            var actors = NativeHub(payload.NativeGraph, "Character").Properties()
                .Select(record => CharacterManager.Instance.GetCharacterByPersistentID(record.Name)).ToArray();
            if (actors.Any(actor => actor == null || actor.isDead)) throw new InvalidOperationException("A departing actor is no longer present.");
            var itemDatabase = DatabaseManager.Instance.tileObjectDatabase;
            var items = NativeHub(payload.NativeGraph, "Tile_Object").Properties()
                .Select(record => itemDatabase.GetTileObjectByPersistentID(record.Name)).ToArray();
            if (items.Any(item => item == null)) throw new InvalidOperationException("A departing item is no longer present.");
            foreach (Character actor in actors)
            {
                EndPortalWalk(actor);
                actor.partyComponent.currentParty?.RemoveMember(actor);
                CharacterManager.Instance.PutCharacterIntoLimbo(actor);
                actor.UnsubscribeFromPermanentSignals();
                actor.faction?.characters.Remove(actor);
                actor.homeSettlement?.RemoveResident(actor);
                actor.homeRegion?.RemoveResident(actor);
                CharacterManager.Instance.RemoveLimboCharacter(actor);
                DatabaseManager.Instance.characterDatabase.RemoveFromAliveVillagersList(actor);
                SchedulingManager.Instance.ClearAllSchedulesBy(actor);
                tile.PortalMemories.Remove(actor.persistentID);
            }
            foreach (TileObject item in items)
            {
                AccessTools.Method(typeof(TileObject), "UnsubscribeListeners").Invoke(item, null);
                item.SetPOIState(POI_STATE.INACTIVE);
                itemDatabase.allTileObjects[item.tileObjectType].Remove(item);
                itemDatabase.tileObjectsByGUID.Remove(item.persistentID); itemDatabase.allTileObjectsList.Remove(item);
                if (item.mapObjectVisual != null) item.mapObjectVisual.gameObject.SetActive(false);
            }
            foreach (JProperty record in NativeHub(payload.NativeGraph, "Trait").Properties())
                if (DatabaseManager.Instance.traitDatabase.traitsByGUID.TryGetValue(record.Name, out Trait trait) && !trait.isSingleton)
                    DatabaseManager.Instance.traitDatabase.UnRegisterTrait(trait);
        }
        private static JArray ProjectRelations(Dictionary<string, JToken> remembered, Dictionary<string, int> localIds)
        {
            var result = new JArray();
            foreach (var relation in remembered) if (localIds.TryGetValue(relation.Key, out int id)) result.Add(new JObject { ["Key"] = id, ["Value"] = relation.Value.DeepClone() });
            return result;
        }
        private static LocationStructure ExistingStructure(string id) => id == null ? null : DatabaseManager.Instance.structureDatabase.GetStructureByPersistentIDSafe(id);
        private static JObject NativeHub(JObject graph, string category) => (JObject)graph["objectHub"][category]["_hub"];
        private static void ReplaceIdentity(JToken token, string source, string destination)
        {
            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties().ToArray())
                {
                    ReplaceIdentity(property.Value, source, destination);
                    if (property.Name == source) { JToken value = property.Value; property.Remove(); obj.Add(destination, value); }
                }
            }
            else if (token is JArray array) foreach (JToken child in array) ReplaceIdentity(child, source, destination);
            else if (token is JValue value && value.Type == JTokenType.String && (string)value == source) value.Value = destination;
        }
        private static long DateTicks(GameDate date) => (((long)date.year * 12 + date.month - 1) * 30 + date.day - 1) * 480 + date.tick;
        private static void ShiftDates(JToken token, long offset)
        {
            if (offset == 0) return;
            if (token is JObject obj)
            {
                if (obj["hasValue"]?.Type == JTokenType.Boolean && (bool)obj["hasValue"] && obj["month"] != null && obj["year"] != null && obj["day"] != null && obj["tick"] != null)
                {
                    long total = (((long)obj["year"] * 12 + (int)obj["month"] - 1) * 30 + (int)obj["day"] - 1) * 480 + (int)obj["tick"] + offset;
                    if (total < 0) throw new InvalidDataException("A transferred actor timer precedes the native calendar.");
                    long day = total / 480; obj["tick"] = total % 480; obj["day"] = day % 30 + 1; obj["month"] = day / 30 % 12 + 1; obj["year"] = day / 360;
                }
                else foreach (JProperty property in obj.Properties()) ShiftDates(property.Value, offset);
            }
            else if (token is JArray array) foreach (JToken child in array) ShiftDates(child, offset);
        }
        private static void Validate(PortalPayload payload)
        {
            if (payload == null || payload.FormatVersion != 1 || payload.SourceTile < 0 || payload.NativeGraph == null || payload.Actors == null || payload.Actors.Length == 0
                || payload.Cargo == null || payload.Memories == null || !Guid.TryParseExact(payload.CampaignId, "N", out _) || string.IsNullOrEmpty(payload.WorldId)
                || !Guid.TryParse(payload.PlayerFaction, out _)) throw new InvalidDataException("Invalid native portal payload.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            JObject characters = NativeHub(payload.NativeGraph, "Character"), items = NativeHub(payload.NativeGraph, "Tile_Object");
            foreach (string id in payload.Actors.Concat(payload.Cargo))
                if (!Guid.TryParse(id, out _) || !identities.Add(id) || characters[id] == null && items[id] == null)
                    throw new InvalidDataException("Portal actor or cargo identity is missing or duplicated.");
            foreach (JProperty actor in characters.Properties())
                if (!payload.Memories.ContainsKey(actor.Name) || (string)actor.Value["persistentID"] != actor.Name)
                    throw new InvalidDataException("Transferred character memory differs from its native identity.");
        }
    }
}
