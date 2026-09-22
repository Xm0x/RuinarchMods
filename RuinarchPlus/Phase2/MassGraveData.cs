using Ruinarch.ModContent;
using UnityEngine;

// Global namespace (matches how the game's demonic-structure skills are declared).
// The `type` getter resolves the framework-allocated virtual PLAYER_SKILL_TYPE lazily,
// so it reads correctly after MassGraveFeature.Register() runs. structureType is bound to
// the allocated virtual STRUCTURE_TYPE by ModContent.RegisterStructure (via reflection).
public class MassGraveData : DemonicStructurePlayerSkill
{
	public override string name => "Mass Grave";

	public override PLAYER_SKILL_TYPE type => ModContent.SkillTypeFor("ruinarch.plus.mass_grave");

	public override string description => "A pit that consumes corpses, keeping your territory clear of the dead.";

	public override string localizedName => "Mass Grave";

	public override string localizedDescription => description;

	public override Vector2Int size => new Vector2Int(6, 4);
}
