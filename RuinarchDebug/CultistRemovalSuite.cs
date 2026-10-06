using System.Collections;
using System.Linq;
using HarmonyLib;
using Inner_Maps;
using UnityEngine;
using TMPro;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private IEnumerator CultistRemovalSuite()
		{
			Character character = null;
			var mover = InnerMapCameraMove.Instance;
			var camera = mover.camera;
			var cameraPosition = mover.transform.position;
			var previousTarget = mover.target;
			bool vectorTarget = (bool)AccessTools.Field(typeof(BaseCameraMove), "isUsingVectorTarget").GetValue(mover);
			var targetPosition = (Vector3)AccessTools.Field(typeof(BaseCameraMove), "_targetPos").GetValue(mover);
			float zoom = camera.orthographicSize;
			try
			{
				character = CharacterManager.Instance.CreateNewCharacter("Logger", RACE.HUMANS, GENDER.MALE,
					faction: FactionManager.Instance.vagrantFaction, randomizeTraits: false);
				var tile = Villages().SelectMany(v => v.cityCenter.passableTiles).First(t => !t.isOccupied);
				character.CreateMarker();
				character.InitialCharacterPlacement(tile);
				character.marker.UpdatePosition();
				character.traitContainer.AddTrait(character, "Demon Cultist");
				// Start with a visible cultist name, as after load or a faction refresh.
				character.marker.UpdateName();
				var plate = AccessTools.FieldRefAccess<CharacterMarker, CharacterMarkerNameplate>("_nameplate")(character.marker);
				var label = AccessTools.FieldRefAccess<CharacterMarkerNameplate, TextMeshProUGUI>("nameLbl")(plate);
				string cultistName = label.text;
				character.traitContainer.RemoveTrait(character, "Demon Cultist");
				string expected = character.visuals.GetCharacterStringIcon() + character.firstNameWithColor;
				yield return null;
				Check("removing Demon Cultist refreshes the actual map nameplate icon", () =>
					(!character.traitContainer.HasTrait("Demon Cultist") && cultistName != expected && label.text == expected,
					$"before={cultistName}, expected={expected}, actual={label.text}"));
				if (_only.Contains("CultistRemovalSuite"))
				{
					camera.orthographicSize = 8f;
					mover.CenterCameraOn(character.marker.gameObject, instantCenter: true);
					yield return Screenshot("cultist-removed.png");
				}
			}
			finally
			{
				if (_only.Contains("CultistRemovalSuite"))
				{
					mover.ClearOutCameraTargets();
					if (vectorTarget) mover.CenterCameraOn(targetPosition);
					else if (previousTarget != null) mover.CenterCameraOn(previousTarget.gameObject);
					mover.MoveCamera(cameraPosition);
				}
				camera.orthographicSize = zoom;
				if (character != null)
				{
					if (character.stateComponent.currentState != null) character.stateComponent.ExitCurrentState();
					character.DestroyMarker(removeFromGame: false);
					character.faction?.LeaveFaction(character);
					CharacterManager.Instance.RemoveCharacter(character);
					DatabaseManager.Instance.characterDatabase.CleanUpCharacter(character);
				}
			}
		}
	}
}
