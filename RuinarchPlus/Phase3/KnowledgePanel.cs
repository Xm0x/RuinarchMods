using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Inner_Maps.Location_Structures;
using Locations.Settlements;

namespace RuinarchPlus.Phase3
{
	/// <summary>
	/// "Who Knows of You": a section of the bookmarks panel (under Major Events) read from the
	/// knowledge ledger (config: <c>knowledgeEnabled</c>). One line for the region ("Your
	/// presence in the region is not known" or how many factions know of you), one per faction
	/// that knows ("Aurenad know of your Portal and Corrupt Kennel", or "... know of you, but
	/// not where you are"), and one per villager carrying news home. Faction lines list what
	/// the faction knows on hover and open its panel on click; a carrier's line selects them.
	///
	/// The section is a bookmark category of its own, a value the game's enum does not have
	/// (<see cref="Category"/>): the two places that read categories by value (the panel's
	/// sort order and the header text) are patched below. Bookmarks are not saved by the game,
	/// so the section is rebuilt for each game or load. It always keeps the region line: a
	/// category that empties out loses its section in the panel for good.
	/// </summary>
	internal static class KnowledgePanel
	{
		// Not a value of the game's BOOKMARK_CATEGORY; only ever used at runtime.
		internal const BOOKMARK_CATEGORY Category = (BOOKMARK_CATEGORY)100;
		internal const string Header = "Who Knows of You";
		private const int MaxCarriers = 5;

		private sealed class Line
		{
			internal string Text;
			internal GenericTextBookmarkable Bookmark;
		}

		private sealed class Wanted
		{
			internal string Text;
			internal Action Select;
			internal Func<string> Tooltip;
		}

		private static readonly Dictionary<string, Line> Lines = new Dictionary<string, Line>();
		private static BookmarkComponent _shownIn;

		/// <summary>The section's lines as shown, in panel order (for the test harness).</summary>
		internal static List<string> Texts() => Lines.Values.Select(l => l.Text).ToList();

		internal static void Refresh()
		{
			BookmarkComponent bookmarks = PlayerManager.Instance?.player?.bookmarkComponent;
			if (bookmarks == null)
			{
				return;
			}
			if (bookmarks != _shownIn)
			{
				// A new game or a load: a new, empty bookmark list.
				Lines.Clear();
				_shownIn = bookmarks;
			}
			List<KeyValuePair<string, Wanted>> wanted = Knowledge.Enabled ? Build() : new List<KeyValuePair<string, Wanted>>();
			HashSet<string> keys = new HashSet<string>(wanted.Select(w => w.Key));
			foreach (string gone in Lines.Keys.Where(k => !keys.Contains(k)).ToList())
			{
				bookmarks.RemoveBookmark(Lines[gone].Bookmark, Category);
				Lines.Remove(gone);
			}
			foreach (KeyValuePair<string, Wanted> w in wanted)
			{
				if (Lines.TryGetValue(w.Key, out Line line))
				{
					if (line.Text != w.Value.Text)
					{
						line.Text = w.Value.Text;
						line.Bookmark.bookmarkEventDispatcher.ExecuteBookmarkChangedNameOrElementsEvent(line.Bookmark);
					}
					continue;
				}
				Line added = new Line { Text = w.Value.Text };
				Func<string> tooltip = w.Value.Tooltip;
				added.Bookmark = new GenericTextBookmarkable(() => added.Text, () => BOOKMARK_TYPE.Text, w.Value.Select, null,
					tooltip == null ? (Action<UIHoverPosition>)null : pos => UIManager.Instance.ShowSmallInfo(tooltip(), pos, Header),
					tooltip == null ? (Action)null : () => UIManager.Instance.HideSmallInfo());
				Lines[w.Key] = added;
				bookmarks.AddBookmark(added.Bookmark, Category);
			}
		}

		private static List<KeyValuePair<string, Wanted>> Build()
		{
			List<KeyValuePair<string, Wanted>> lines = new List<KeyValuePair<string, Wanted>>();
			List<Faction> factions = FactionManager.Instance.allFactions
				.Where(f => f != null && f.isMajorNonPlayer && f.ownedSettlements.Any(s => s is NPCSettlement { locationType: LOCATION_TYPE.VILLAGE }))
				.ToList();
			List<Faction> knowing = factions.Where(f => f.isAwareOfPlayer || Knowledge.KnownStanding(f).Count > 0).ToList();
			lines.Add(Pair("region", knowing.Count == 0
				? "Your presence in the region is not known."
				: $"{knowing.Count} of {factions.Count} {(factions.Count == 1 ? "faction knows" : "factions know")} of you.", null, null));
			foreach (Faction f in knowing)
			{
				Faction faction = f;
				List<LocationStructure> known = Knowledge.KnownStanding(faction);
				string text = known.Count == 0
					? $"{Name(faction.name)} know of you, but not where you are."
					: $"{Name(faction.name)} know of {List(known)}.";
				lines.Add(Pair("f:" + faction.persistentID, text,
					() => UIManager.Instance.ShowFactionInfo(faction),
					() => KnownList(faction)));
			}
			foreach (KeyValuePair<Character, List<LocationStructure>> courier in Knowledge.Couriers().Take(MaxCarriers))
			{
				Character c = courier.Key;
				string of = c.faction != null ? $" of {c.faction.name}" : "";
				lines.Add(Pair("c:" + c.persistentID, $"{Name(c.name)}{of} is carrying news of {List(courier.Value)} home.",
					() => UIManager.Instance.ShowCharacterInfo(c, centerOnCharacter: true), null));
			}
			return lines;
		}

		private static KeyValuePair<string, Wanted> Pair(string key, string text, Action select, Func<string> tooltip)
		{
			return new KeyValuePair<string, Wanted>(key, new Wanted { Text = text, Select = select, Tooltip = tooltip });
		}

		private static string Name(string name) => UtilityScripts.Utilities.ColorizeAndBoldName(name);

		// "your Portal", "your Portal and Corrupt Kennel", "your Portal, Kennel and 2 more".
		private static string List(List<LocationStructure> structures)
		{
			List<string> names = Knowledge.Names(structures.Take(2)).ToList();
			int more = structures.Count - names.Count;
			if (more > 0)
			{
				return $"your {string.Join(", ", names)} and {more} more";
			}
			return "your " + (names.Count == 2 ? names[0] + " and " + names[1] : names[0]);
		}

		private static string KnownList(Faction faction)
		{
			List<LocationStructure> known = Knowledge.KnownStanding(faction);
			if (known.Count == 0)
			{
				return $"{faction.name} know demons are here, but of none of your buildings still standing.";
			}
			// Where each building is known, and by how many: "- Portal: Mysa (4), Ulric (1)".
			return $"{faction.name} know of:\n- " + string.Join("\n- ", known.Select(s =>
				$"{s.name}: " + string.Join(", ", Knowledge.VillagesKnowing(faction, s).Select(v => $"{v.name} ({Knowledge.Rememberers(v, s)})"))));
		}
	}

	// A faction becoming aware (or unaware) shows at once, not at the next tick: the game may
	// pause right after posting its "is now aware" alert, and ticks stop while paused.
	[HarmonyPatch(typeof(Faction), nameof(Faction.SetIsAwareOfPlayer))]
	internal static class KnowledgePanel_Awareness
	{
		private static void Postfix()
		{
			try
			{
				KnowledgePanel.Refresh();
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Who Knows of You panel failed: " + e.Message);
			}
		}
	}

	[HarmonyPatch(typeof(GameManager), "TickStarted")]
	internal static class KnowledgePanel_Tick
	{
		private static void Postfix()
		{
			try
			{
				KnowledgePanel.Refresh();
			}
			catch (Exception e)
			{
				RuinarchPlus.Log?.Warning("Who Knows of You panel failed: " + e.Message);
			}
		}
	}

	// The panel sorts its sections by this order, and throws for a value it does not know.
	// Spread the game's own orders out and slot the section in right after Major Events.
	[HarmonyPatch(typeof(Extensions), nameof(Extensions.GetBookmarkCategoryOrder))]
	internal static class KnowledgePanel_Order
	{
		private static bool Prefix(BOOKMARK_CATEGORY p_category, ref int __result)
		{
			if (p_category != KnowledgePanel.Category)
			{
				return true;
			}
			__result = 15;
			return false;
		}

		private static void Postfix(BOOKMARK_CATEGORY p_category, ref int __result)
		{
			if (p_category != KnowledgePanel.Category)
			{
				__result *= 10;
			}
		}
	}

	[HarmonyPatch(typeof(Extensions), nameof(Extensions.LocalizedText), new[] { typeof(BOOKMARK_CATEGORY) })]
	internal static class KnowledgePanel_Header
	{
		private static bool Prefix(BOOKMARK_CATEGORY p_category, ref string __result)
		{
			if (p_category != KnowledgePanel.Category)
			{
				return true;
			}
			__result = KnowledgePanel.Header;
			return false;
		}
	}
}
