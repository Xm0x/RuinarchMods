using System.Linq;
using System.Text;

namespace TruePlanet
{
    internal static class TilePresentation
    {
        internal const string Heading = "Land details";
        internal static string Status(bool inGame) => inGame ? "Ready to travel." : "Ready to play.";
        internal static string BiomeName(TerrainSample sample)
        {
            if (sample.Temperature < .22) return sample.Moisture > .55 ? "Boreal forest" : "Snow";
            if (sample.Moisture < .3) return sample.Temperature > .72 ? "Hot desert" : "Arid scrubland";
            if (sample.Moisture > .55) return sample.Temperature > .72 ? "Hot rainforest" : "Temperate forest";
            return sample.Temperature > .72 ? "Arid scrubland" : "Grassland";
        }
        internal static string MapSize(LocalMapSize size)
        {
            var areas = TileGame.Areas(size);
            return (size == LocalMapSize.ExtraLarge ? "Extra Large" : size.ToString()) + " (" + areas.x + "x" + areas.y + ")";
        }
        internal static string Details(Planet planet, int tile, TerrainSample sample, LocalTerrainPlan plan,
            CampaignManifest campaign, int activeTile, bool activeHasPortal)
        {
            int owner = planet.TileOwners[tile]; Province province = owner < 0 ? null : planet.Provinces[owner];
            var text = new StringBuilder(); text.AppendLine(BiomeName(sample));
            text.AppendLine(province == null ? "Unclaimed wilderness" : "Province: " + province.Name);
            if (province != null) text.AppendLine("Nation: " + planet.Nations[province.Nation].Name);
            text.AppendLine("Map size: " + MapSize(planet.Options.LocalSize));
            text.AppendLine("Temperature: " + (sample.Temperature * 100).ToString("0") + "%");
            text.AppendLine("Moisture: " + (sample.Moisture * 100).ToString("0") + "%");
            text.AppendLine(plan.TerrainSummary());
            int villages = 0;
            if (province != null) foreach (Village village in province.Villages) if (village.TileId == tile)
            {
                text.AppendLine("\n" + village.Name + (village.Capital ? " (capital)" : "") + "\n" + (village.Elven ? "Elven" : "Human")); villages++;
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
