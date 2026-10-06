using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Traits;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private Character StockActor(List<Character> fixtures, string className = "Logger", Faction faction = null, GENDER gender = GENDER.MALE)
		{
			Character actor = CharacterManager.Instance.CreateNewCharacter(className, RACE.HUMANS, gender,
				faction: faction ?? FactionManager.Instance.vagrantFaction, homeRegion: GridMap.Instance.mainRegion, randomizeTraits: false);
			fixtures.Add(actor);
			LocationGridTile tile = GridMap.Instance.mainRegion.areas.Where(a => !a.IsNextToOrPartOfVillage())
				.Select(a => a.gridTileComponent.centerGridTile)
				.First(t => t != null && t.structure is Wilderness && t.IsPassable() && !t.isOccupied
					&& !fixtures.Any(c => c != actor && c.hasMarker && c.gridTileLocation == t));
			actor.CreateMarker();
			actor.InitialCharacterPlacement(tile);
			actor.marker.UpdatePosition();
			return actor;
		}

		private IEnumerator StockDiscordSuite()
		{
			var fixtures = new List<Character>();
			try
			{
				Character patient = StockActor(fixtures);
				var needs = patient.needsComponent;
				int hungry = needs.doNotGetHungry, tired = needs.doNotGetTired, bored = needs.doNotGetBored;
				patient.traitContainer.AddTrait(patient, "Recuperating");
				patient.traitContainer.AddTrait(patient, "Eating");
				patient.traitContainer.RemoveTrait(patient, "Recuperating");
				Check("C09-43 leaving hospice preserves only the active Eating hunger suppression", () =>
					(needs.doNotGetHungry == hungry + 1 && needs.doNotGetTired == tired && needs.doNotGetBored == bored,
					$"hungry={needs.doNotGetHungry}, tired={needs.doNotGetTired}, bored={needs.doNotGetBored}"));
				patient.traitContainer.RemoveTrait(patient, "Eating");
				patient.traitContainer.AddTrait(patient, "Recuperating");
				patient.traitContainer.RemoveTrait(patient, "Recuperating");
				needs.SetFullness(80f);
				needs.PerTick();
				Check("C09-43 repeated hospice stays release needs suppression and fullness decreases again", () =>
					(needs.doNotGetHungry == hungry && needs.doNotGetTired == tired && needs.doNotGetBored == bored && needs.fullness < 80f,
					$"hungry={needs.doNotGetHungry}, tired={needs.doNotGetTired}, bored={needs.doNotGetBored}, fullness={needs.fullness}"));
				var savedNeeds = new SaveDataCharacterNeedsComponent();
				savedNeeds.Save(needs);
				var loadedNeeds = savedNeeds.Load();
				Check("C09-43 a completed hospice stay saves no permanent needs suppression", () =>
					(loadedNeeds.doNotGetHungry == hungry && loadedNeeds.doNotGetTired == tired && loadedNeeds.doNotGetBored == bored,
					$"loaded hunger={loadedNeeds.doNotGetHungry}, tired={loadedNeeds.doNotGetTired}, bored={loadedNeeds.doNotGetBored}"));

				Character survivor = StockActor(fixtures), spouse = StockActor(fixtures), affair = StockActor(fixtures);
				survivor.relationshipContainer.AddRelationship(survivor, spouse, RELATIONSHIP_TYPE.LOVER);
				var breakup = new BreakUpData();
				breakup.ResetData();
				breakup.SetIsInUse(true);
				Check("C11-76 Break Up accepts a living partner", () => (breakup.CanPerformAbilityTowards(survivor), "living spouse"));
				spouse.Death();
				Check("C11-76 Break Up rejects an exclusively dead partner and explains why", () =>
					(!breakup.CanPerformAbilityTowards(survivor) && !string.IsNullOrEmpty(breakup.GetReasonsWhyCannotPerformAbilityTowards(survivor)),
					$"eligible={breakup.CanPerformAbilityTowards(survivor)}, reason={breakup.GetReasonsWhyCannotPerformAbilityTowards(survivor)}"));
				var relationship = survivor.relationshipContainer.relationships.First().Value;
				survivor.relationshipContainer.relationships[int.MaxValue] = relationship;
				Check("C11-76 an unresolved partner ID does not offer an unusable Break Up", () =>
					(!breakup.CanPerformAbilityTowards(survivor), $"eligible={breakup.CanPerformAbilityTowards(survivor)}"));
				survivor.relationshipContainer.relationships.Remove(int.MaxValue);
				survivor.relationshipContainer.AddRelationship(survivor, affair, RELATIONSHIP_TYPE.AFFAIR);
				Check("C11-76 a living affair keeps Break Up available after a spouse dies", () =>
					(breakup.CanPerformAbilityTowards(survivor), "dead spouse and living affair"));
				breakup.ActivateAbility(survivor);
				var scheme = (SchemeUIController)AccessTools.Field(typeof(UIManager), "_schemeUIController").GetValue(UIManager.Instance);
				Check("C11-76 the actual scheme panel targets the living affair, not the corpse", () =>
					(AccessTools.Field(typeof(SchemeUIController), "_otherTarget").GetValue(scheme) == affair, "scheme target must be the living affair"));
				scheme.HideUI();
				AccessTools.Method(typeof(UIManager), "OnCloseSchemeUI").Invoke(UIManager.Instance, null);

				Character poisonous = StockActor(fixtures), ordinary = StockActor(fixtures);
				poisonous.traitContainer.AddTrait(poisonous, "Poisonous");
				var selfPoison = poisonous.traitContainer.GetTraitOrStatus<Poisoned>("Poisoned");
				float ownHp = poisonous.currentHP;
				selfPoison.OnTickStarted(poisonous);
				Check("C08-96 intrinsically Poisonous characters do not damage themselves every tick", () =>
					(poisonous.currentHP == ownHp, $"HP {ownHp} -> {poisonous.currentHP}"));
				ordinary.traitContainer.AddTrait(ordinary, "Poisoned", null, bypassElementalChance: true);
				float normalHp = ordinary.currentHP;
				ordinary.traitContainer.GetTraitOrStatus<Poisoned>("Poisoned").OnTickStarted(ordinary);
				Check("C08-96 externally poisoned ordinary characters still take poison damage", () =>
					(ordinary.currentHP < normalHp, $"HP {normalHp} -> {ordinary.currentHP}"));

				Character cultist = StockActor(fixtures);
				cultist.religionComponent.ChangeReligion(RELIGION.Demon_Worship);
				cultist.traitContainer.AddTrait(cultist, "Demon Cultist");
				var purify = new Purify();
				var node = new ActualGoapNode();
				try
				{
					node.SetActionData(purify, patient, cultist, null, 10);
					purify.AfterPurifySuccess(node);
					Check("C09-69 purification removes demon worship as well as the cultist trait", () =>
						(!cultist.traitContainer.HasTrait("Demon Cultist") && cultist.religionComponent.religion != RELIGION.Demon_Worship,
						$"cultist={cultist.traitContainer.HasTrait("Demon Cultist")}, religion={cultist.religionComponent.religion}"));
				}
				finally { node.Reset(); }
			}
			finally
			{
				foreach (Character actor in fixtures)
				{
					if (actor.stateComponent.currentState != null) actor.stateComponent.ExitCurrentState();
					actor.DestroyMarker(removeFromGame: false);
					actor.faction?.LeaveFaction(actor);
					CharacterManager.Instance.RemoveCharacter(actor);
					DatabaseManager.Instance.characterDatabase.CleanUpCharacter(actor);
				}
			}
			yield break;
		}
	}
}
