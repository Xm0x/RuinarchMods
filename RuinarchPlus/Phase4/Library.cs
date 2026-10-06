using System;
using System.Collections.Generic;
using HarmonyLib;
using Inner_Maps;
using Locations.Settlements;
using Ruinarch.ModContent;
using Ruinarch.ModContent.Templates;
using UnityEngine;

// Declared in the game's structure namespace, like MassGrave and TownHall: this type lives in
// the MOD assembly, and the Ruinarch.ModContent framework instantiates it through the
// registered factory.
namespace Inner_Maps.Location_Structures
{
	/// <summary>
	/// A Town's or City's Library (see <c>RuinarchPlus.Phase4.Records</c>): the village keeps
	/// its written record of the player's buildings in Books inside, and any villager of the
	/// village writes and reads there. Built through the game's own construction pipeline,
	/// using the bundled Library layout with the Workshop's build cost and hit points.
	/// A plain village building otherwise: it hires no worker and is damaged and destroyed
	/// like any other.
	/// </summary>
	public class Library : ManMadeStructure
	{
		internal static readonly List<Library> Active = new List<Library>();

		public Library(STRUCTURE_TYPE type, Region location)
			: base(type, location)
		{
			SetMaxHPAndReset(8000);
			Active.Add(this);
		}

		public Library(Region location, SaveDataManMadeStructure data)
			: base(location, data)
		{
			SetMaxHP(8000);
			Active.Add(this);
		}

		/// <summary>The standing Library of <paramref name="settlement"/>, or null.</summary>
		internal static Library FindFor(BaseSettlement settlement)
		{
			for (int i = 0; i < Active.Count; i++)
			{
				Library library = Active[i];
				if (!library.hasBeenDestroyed && library.settlementLocation == settlement)
				{
					return library;
				}
			}
			return null;
		}

		protected override void AfterStructureDestruction(Character p_responsibleCharacter = null)
		{
			NPCSettlement settlement = settlementLocation as NPCSettlement;
			Active.Remove(this);
			base.AfterStructureDestruction(p_responsibleCharacter);
			global::RuinarchPlus.Phase4.Records.OnLibraryLost(this, settlement);
		}
	}

	// The framework normally appends template variants to the borrowed prefab list.
	// Libraries use the authored layout exclusively; stock Workshops remain untouched.
	[HarmonyPatch(typeof(StructureData), nameof(StructureData.GetStructurePrefabs),
		new Type[] { typeof(FACTION_TYPE), typeof(StructureSetting) })]
	internal static class LibraryLook
	{
		private static List<GameObject> _prefabs;

		[HarmonyPriority(Priority.First)]
		private static void Prefix(StructureSetting p_structureSetting, out bool __state)
		{
			__state = p_structureSetting.structureType == ModContent.StructureTypeFor(global::RuinarchPlus.Phase4.Records.LibraryId);
		}

		[HarmonyPriority(Priority.Last)]
		private static Exception Finalizer(Exception __exception, bool __state, ref List<GameObject> __result)
		{
			if (!__state) return __exception;
			if (_prefabs == null)
			{
				// PrefabsFor returns a detached List, so cache it without copying on each query.
				_prefabs = (List<GameObject>)ModTemplates.PrefabsFor("ruinarch.plus/library");
				if (_prefabs.Count == 0)
				{
					_prefabs = null;
					throw new TemplateException("The bundled ruinarch.plus/library template is not loaded.");
				}
			}
			__result = _prefabs;
			return null;
		}
	}
}
