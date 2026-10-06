using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private IEnumerator HarpyDragonSuite()
		{
			var characters = new List<Character>();
			var region = (Region)AccessTools.Constructor(typeof(Region), Type.EmptyTypes).Invoke(null);
			var randomState = UnityEngine.Random.state;
			try
			{
				var harpy = CharacterManager.Instance.CreateNewSummon(SUMMON_TYPE.Harpy);
				characters.Add(harpy);
				var dragon = CharacterManager.Instance.CreateNewSummon(SUMMON_TYPE.Dragon);
				characters.Add(dragon);
				var human = CharacterManager.Instance.CreateNewCharacter("Logger", RACE.HUMANS, GENDER.MALE,
					faction: FactionManager.Instance.vagrantFaction, randomizeTraits: false);
				characters.Add(human);
				var behaviour = new HarpyBehaviour();
				foreach (object selector in new object[] { behaviour, harpy })
				{
					var method = AccessTools.Method(selector.GetType(), "GetTargetForCapture");
					Func<Character> select = () => (Character)method.Invoke(selector, new object[] { harpy, region });
					string path = selector is Harpy ? "agitated harpy" : "wild harpy";
					region.charactersAtLocation.Clear();
					region.charactersAtLocation.Add(dragon);
					Check(path + " cannot abduct its only candidate, a dragon", () => (select() == null, "dragon-only region"));
					region.charactersAtLocation.Add(human);
					UnityEngine.Random.InitState(1729);
					Check(path + " selects eligible prey instead of a dragon", () =>
					{
						bool valid = true;
						for (int i = 0; i < 32; i++) valid &= select() == human;
						return (valid, "human and dragon candidates");
					});
					region.charactersAtLocation.Clear();
					region.charactersAtLocation.Add(human);
					Check(path + " still captures eligible human prey", () => (select() == human, "human-only region"));
					region.charactersAtLocation.Clear();
					region.charactersAtLocation.Add(harpy);
					Check(path + " does not capture itself", () => (select() == null, "actor-only region"));
				}
			}
			finally
			{
				region.charactersAtLocation.Clear();
				foreach (var character in characters)
				{
					character.faction?.LeaveFaction(character);
					CharacterManager.Instance.RemoveCharacter(character);
					DatabaseManager.Instance.characterDatabase.CleanUpCharacter(character);
				}
				UnityEngine.Random.state = randomState;
			}
			yield break;
		}
	}
}
