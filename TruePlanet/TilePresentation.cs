using System.Linq;
using System.Text;

namespace TruePlanet
{
    internal static class TilePresentation
    {
        internal const string Heading = "Land details";
        internal static string Status(bool inGame) => inGame ? "Ready to travel." : "Ready to play.";
        internal static string BiomeName(TerrainSample sample) =>
            NativeClimate.Classify((float)sample.Temperature, (float)sample.Moisture).ToString();
        internal static string MapSize(LocalMapSize size)
        {
            var areas = TileGame.Areas(size);
            return (size == LocalMapSize.ExtraLarge ? "Extra Large" : size.ToString()) + " (" + areas.x + "x" + areas.y + ")";
        }
        internal static string SettlementKind(Village village, LocalMapSize size) => village.Capital ? "Capital"
            : TileGame.NativeVillageSize(village, size) == VILLAGE_SIZE.Small ? "Village" : "City";
        internal static string Details(Planet planet, int tile, TerrainSample sample, LocalTerrainPlan plan,
            CampaignManifest campaign, int activeTile, bool activeHasPortal)
        {
            int owner = planet.TileOwners[tile]; Province province = owner < 0 ? null : planet.Provinces[owner];
            var text = new StringBuilder();
            text.AppendLine(province == null ? "Unclaimed wilderness" : "Province: " + province.Name);
            if (province != null) text.AppendLine("Nation: " + planet.Nations[province.Nation].Name);
            text.AppendLine("Temperature: " + (sample.Temperature * 100).ToString("0") + "%");
            text.AppendLine("Moisture: " + (sample.Moisture * 100).ToString("0") + "%");
            text.AppendLine(plan.TerrainSummary());
            int villages = 0;
            if (province != null) foreach (Village village in province.Villages) if (village.TileId == tile)
            {
                text.AppendLine("\n" + village.Name + "\n" + SettlementKind(village, planet.Options.LocalSize)
                    + "\n" + (village.Elven ? "Elven" : "Human")); villages++;
            }
            if (villages == 0) text.AppendLine("\nNo settlements on this land.");
            if (campaign != null)
            {
                TileSnapshot snapshot = campaign.Tiles.FirstOrDefault(item => item.TileId == tile);
                text.AppendLine(tile == activeTile ? "\nCurrent tile" : snapshot == null ? "\nUnvisited" : "\nVisited");
                text.AppendLine(tile == activeTile ? (activeHasPortal ? "Portal held" : "No standing Portal") : snapshot != null && snapshot.Held ? "Portal held" : "Not held");
            }
            return text.ToString();
        }
    }
}
