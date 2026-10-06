using System.Collections;
using System.Linq;
using HarmonyLib;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private IEnumerator FrozenVigilantSuite()
		{
			Character actor = null, target = null;
			var node = new ActualGoapNode();
			try
			{
				actor = CharacterManager.Instance.CreateNewCharacter("Logger", RACE.HUMANS, GENDER.MALE,
					faction: FactionManager.Instance.vagrantFaction, randomizeTraits: false);
				target = CharacterManager.Instance.CreateNewCharacter("Logger", RACE.HUMANS, GENDER.MALE,
					faction: FactionManager.Instance.vagrantFaction, randomizeTraits: false);
				var tile = Villages().SelectMany(v => v.cityCenter.tiles).First(t => t != null && !t.isOccupied);
				target.CreateMarker();
				target.InitialCharacterPlacement(tile);
				target.marker.UpdatePosition();
				var action = InteractionManager.Instance.goapActionData[INTERACTION_TYPE.KNOCKOUT_CHARACTER];
				node.SetActionData(action, actor, target, null, 10);
				AccessTools.Property(typeof(ActualGoapNode), "isStealth").SetValue(node, true);
				var predicate = AccessTools.Method(typeof(ActualGoapNode), "ShouldDoVigilantEffect");
				System.Func<bool> detects = () => (bool)predicate.Invoke(node, null);
				Check("ordinary target does not detect stealth through Vigilant", () => (!detects(), "Vigilant absent"));
				target.traitContainer.AddTrait(target, "Vigilant");
				Check("awake Vigilant target detects stealth knockout", () => (detects(), "Vigilant awake"));
				target.traitContainer.AddTrait(target, "Frozen");
				Check("Frozen Vigilant target cannot detect stealth knockout", () => (!detects(), "Vigilant encased in ice"));
				target.traitContainer.RemoveTrait(target, "Frozen");
				Check("thawed Vigilant target detects stealth again", () => (detects(), "Frozen removed"));
				AccessTools.Property(typeof(ActualGoapNode), "isStealth").SetValue(node, false);
				Check("non-stealth action never uses Vigilant detection", () => (!detects(), "stealth disabled"));
			}
			finally
			{
				node.Reset();
				foreach (var character in new[] { actor, target })
				{
					if (character == null) continue;
					if (character.stateComponent.currentState != null) character.stateComponent.ExitCurrentState();
					character.DestroyMarker(removeFromGame: false);
					character.faction?.LeaveFaction(character);
					CharacterManager.Instance.RemoveCharacter(character);
					DatabaseManager.Instance.characterDatabase.CleanUpCharacter(character);
				}
			}
			yield break;
		}
	}
}
