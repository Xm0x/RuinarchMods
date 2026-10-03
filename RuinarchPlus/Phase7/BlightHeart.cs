using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using Inner_Maps.Location_Structures;
using Ruinarch.ModContent;
using UnityEngine;

namespace RuinarchPlus.Phase7
{
	/// <summary>
	/// The Blight Heart, a demonic building (config: <c>blightEnabled</c>). Every hour it
	/// corrupts a few tiles at the edge of the corruption it stands in ("its patch"), within its
	/// reach; deaths on its patch feed it up to level 3 (more reach, growth and HP). Destroyed,
	/// its patch stays but stops growing. Looks like a Crypt for now. Level and feeding are
	/// saved in <c>ModData/ruinarch.plus.blight.json</c>.
	/// </summary>
	public class BlightHeart : DemonicStructure
	{
		internal const string Id = "ruinarch.plus.blight_heart";
		private const string SaveId = "ruinarch.plus.blight";
		private static readonly int[] HpByLevel = { 1500, 2250, 3000 };

		/// <summary>Hearts standing in the current world.</summary>
		private static readonly List<BlightHeart> Hearts = new List<BlightHeart>();

		internal int Level { get; private set; } = 1;
		internal int Fed { get; private set; }

		/// <summary>Corrupted tiles connected to the Heart within its reach (refreshed hourly).</summary>
		internal readonly HashSet<LocationGridTile> Patch = new HashSet<LocationGridTile>();
		private readonly HashSet<LocationGridTile> _frontierSet = new HashSet<LocationGridTile>();
		private readonly List<LocationGridTile> _frontier = new List<LocationGridTile>();
		private readonly Queue<LocationGridTile> _patchQueue = new Queue<LocationGridTile>();

		internal int PatchSize => Patch.Count;
		internal int Reach => Pick(RuinarchPlusConfig.Current.blightReach, 6);
		internal int GrowthPerHour => Pick(RuinarchPlusConfig.Current.blightGrowthPerHour, 3);
		internal int FeedNeeded => Math.Max(1, RuinarchPlusConfig.Current.blightFeedPerLevel) * Level;

		public override string nameplateName => $"Blight Heart (level {Level})";

		protected override string GetCustomDescription()
		{
			return $"{nameplateName}\nFeeding: {Fed}/{FeedNeeded}\nPatch: {PatchSize} tiles\nReach: {Reach} tiles; grows up to {GrowthPerHour} tiles each hour.";
		}

		public BlightHeart(STRUCTURE_TYPE type, Region location)
			: base(type, location)
		{
			SetMaxHPAndReset(HpByLevel[0]);
		}

		public BlightHeart(Region location, SaveDataDemonicStructure data)
			: base(location, data)
		{
		}

		public override void OnBuiltNewStructure()
		{
			base.OnBuiltNewStructure();
			Track(this);
			RuinarchPlus.Log?.Info($"A Blight Heart was raised at {GetCenterTile()?.localPlace}.");
		}

		public override void OnDoneLoadStructure()
		{
			base.OnDoneLoadStructure();
			Track(this);
		}

		private int Pick(int[] byLevel, int fallback)
		{
			return byLevel != null && byLevel.Length > 0 ? byLevel[Math.Min(Level, byLevel.Length) - 1] : fallback;
		}

		private static void Track(BlightHeart h)
		{
			Hearts.RemoveAll(x => x == null || x.hasBeenDestroyed || x == h);
			Hearts.Add(h);
		}

		/// <summary>Standing Hearts of the current world.</summary>
		internal static List<BlightHeart> All()
		{
			Region region = GridMap.Instance?.mainRegion;
			Hearts.RemoveAll(h => h == null || h.hasBeenDestroyed || h.region != region);
			return Hearts.ToList();
		}

		/// <summary>Refreshes the patch and returns the untaken tiles touching it, in reach.</summary>
		internal List<LocationGridTile> RefreshPatch()
		{
			Patch.Clear();
			_frontierSet.Clear();
			_frontier.Clear();
			_patchQueue.Clear();
			LocationGridTile center = GetCenterTile();
			if (center == null)
			{
				return _frontier;
			}
			int reach = Reach;
			foreach (LocationGridTile t in tiles)
			{
				if (Patch.Add(t))
				{
					_patchQueue.Enqueue(t);
				}
			}
			while (_patchQueue.Count > 0)
			{
				LocationGridTile t = _patchQueue.Dequeue();
				foreach (LocationGridTile n in t.neighbourList)
				{
					if (n.GetDistanceTo(center) > reach)
					{
						continue;
					}
					if (n.corruptionComponent.isCorrupted)
					{
						if (Patch.Add(n))
						{
							_patchQueue.Enqueue(n);
						}
					}
					else
					{
						if (_frontierSet.Add(n)) _frontier.Add(n);
					}
				}
			}
			return _frontier;
		}

		private void Feed()
		{
			if (Level >= 3)
			{
				return;
			}
			Fed++;
			if (Fed < FeedNeeded)
			{
				return;
			}
			Fed = 0;
			Level++;
			int hp = HpByLevel[Level - 1];
			int gained = hp - maxHP;
			SetMaxHP(hp);
			AdjustHP(gained);
			RuinarchPlus.Log?.Info($"A Blight Heart fed on the dead and grew to level {Level} (reach {Reach}, {GrowthPerHour} tiles an hour).");
		}

		// --- registration, hourly growth, deaths, save ---

		public static void Register()
		{
			try
			{
				ModContent.RegisterStructure(new StructureRegistration
				{
					Id = Id,
					DisplayName = "Blight Heart",
					Factory = (type, region) => new BlightHeart(type, region),
					LoadFactory = (type, region, save) => new BlightHeart(region, (SaveDataDemonicStructure)save),
					PrefabSource = STRUCTURE_TYPE.CRYPT,
					SkillDataFrom = PLAYER_SKILL_TYPE.CRYPT,
					// The game's skill classes can only be made once its skill tables are built.
					CreateSkill = (structure, skill) => new BlightHeartData(structure, skill),
					IsPlayerStructure = true,
				});
				ModSave.Register(SaveId, Save, Load);
				RuinarchPlus.Log?.Info($"Blight Heart registered (STRUCTURE_TYPE={(int)ModContent.StructureTypeFor(Id)}).");
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Error("Blight Heart registration failed: " + e);
			}
		}

		/// <summary>Grants the build skill once the player exists, with blightHeartLimit charges.</summary>
		private static void Grant()
		{
			PlayerSkillComponent skills = PlayerManager.Instance?.player?.playerSkillComponent;
			DemonicStructurePlayerSkill data = PlayerSkillManager.Instance?.GetDemonicStructureSkillData(ModContent.SkillTypeFor(Id));
			if (skills == null || data == null || data.isInUse)
			{
				return;
			}
			skills.AddAndCategorizePlayerSkill(data);
			int limit = Math.Max(1, RuinarchPlusConfig.Current.blightHeartLimit);
			data.SetMaxCharges(limit);
			data.SetCharges(Math.Max(0, limit - All().Count));
			RuinarchPlus.Log?.Info($"Blight Heart can be built ({data.charges} of {limit}).");
		}

		internal static void Hourly()
		{
			if (!BlightRules.Enabled)
			{
				return;
			}
			Grant();
			BlightEngine.StartHour();
			List<BlightHeart> hearts = All();
			foreach (BlightHeart h in hearts.OrderBy(_ => UnityEngine.Random.value))
			{
				try
				{
					List<LocationGridTile> grown = BlightEngine.Grow(h.RefreshPatch(), h.GrowthPerHour);
					h.Patch.UnionWith(grown);
				}
				catch (Exception e)
				{
					RuinarchPlus.Log?.Warning("Blight Heart growth failed: " + e.Message);
				}
			}
			BlightEngine.Flush();
			BlightResponse.Hourly();
		}

		internal static void OnDeath(LocationGridTile where)
		{
			if (!BlightRules.Enabled || where == null || !where.corruptionComponent.isCorrupted)
			{
				return;
			}
			foreach (BlightHeart h in All())
			{
				// Fires and cleaners can change connectivity between hourly growth ticks.
				h.RefreshPatch();
				if (h.Patch.Contains(where))
				{
					h.Feed();
					break;
				}
			}
		}

		// One "heartId|level|fed" per Heart.
		private static string Save()
		{
			BlightSaveData file = new BlightSaveData();
			foreach (BlightHeart h in All())
			{
				file.hearts.Add($"{h.persistentID}|{h.Level}|{h.Fed}");
			}
			return file.hearts.Count == 0 ? null : JsonUtility.ToJson(file);
		}

		private static void Load(string json)
		{
			BlightSaveData file = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<BlightSaveData>(json);
			if (file == null)
			{
				return;
			}
			List<BlightHeart> hearts = All();
			foreach (string entry in file.hearts)
			{
				string[] p = entry.Split('|');
				BlightHeart h = p.Length < 3 ? null : hearts.FirstOrDefault(x => x.persistentID == p[0]);
				if (h == null || !int.TryParse(p[1], out int level) || !int.TryParse(p[2], out int fed))
				{
					continue;
				}
				h.Level = Mathf.Clamp(level, 1, 3);
				h.Fed = Math.Max(0, fed);
				h.SetMaxHP(HpByLevel[h.Level - 1]);
				h.RefreshPatch();
			}
			RuinarchPlus.Log?.Info($"Blight loaded: {hearts.Count} Heart(s).");
		}
	}

	[Serializable]
	public class BlightSaveData
	{
		public List<string> hearts = new List<string>();
	}

	/// <summary>The Blight Heart's build skill (the demonic build menu entry).</summary>
	public class BlightHeartData : DemonicStructurePlayerSkill
	{
		private readonly PLAYER_SKILL_TYPE _type;

		public override string name => "Blight Heart";

		public override PLAYER_SKILL_TYPE type => _type;

		public override string description => "Grows corruption around itself every hour and feeds on those who die on it. Villagers will try to clean the blight away.";

		public override Vector2Int size => new Vector2Int(6, 4);

		public BlightHeartData(STRUCTURE_TYPE structure, PLAYER_SKILL_TYPE type)
		{
			_type = type;
			base.structureType = structure;
		}
	}

	// With the blight switched off the Heart is not buildable: whatever path grants its skill
	// (a save made while it was on, a loadout), it stays out of the build menu.
	[HarmonyPatch(typeof(PlayerSkillComponent), nameof(PlayerSkillComponent.AddAndCategorizePlayerSkill), new Type[] { typeof(SkillData), typeof(bool), typeof(bool) })]
	internal static class Blight_DisabledSkill
	{
		private static bool Prefix(SkillData p_skillData)
		{
			return BlightRules.Enabled || p_skillData == null || p_skillData.type != ModContent.SkillTypeFor(BlightHeart.Id);
		}
	}

	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class Blight_HourTick
	{
		private static void Postfix(GameManager __instance)
		{
			try
			{
				if (__instance.Today().tick % 20 == 0)
				{
					BlightHeart.Hourly();
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Blight hourly failed: " + e.Message);
			}
		}
	}

	// A death on a Heart's blight feeds it. The tile is read before death removes the body.
	[HarmonyPatch(typeof(Character), nameof(Character.Death))]
	internal static class Blight_Death
	{
		private static void Prefix(Character __instance, out LocationGridTile __state)
		{
			__state = __instance.isDead ? null : __instance.gridTileLocation;
		}

		private static void Postfix(Character __instance, LocationGridTile __state)
		{
			try
			{
				if (__state != null && __instance.isDead)
				{
					BlightHeart.OnDeath(__state);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Blight death handler failed: " + e.Message);
			}
		}
	}

	// Monsters and summons die through Summon.Death, which does not call Character.Death.
	[HarmonyPatch(typeof(Summon), nameof(Summon.Death))]
	internal static class Blight_SummonDeath
	{
		private static void Prefix(Character __instance, out LocationGridTile __state)
		{
			__state = __instance.isDead ? null : __instance.gridTileLocation;
		}

		private static void Postfix(Character __instance, LocationGridTile __state)
		{
			try
			{
				if (__state != null && __instance.isDead)
				{
					BlightHeart.OnDeath(__state);
				}
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Blight death handler failed: " + e.Message);
			}
		}
	}
}
