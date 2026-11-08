using System;
using System.Collections.Generic;
using System.Reflection;
using Inner_Maps;

namespace RuinarchDebug
{
    public partial class AutoTest
    {
        private static (bool, string) NativeClimateLookup(object world)
        {
            InnerTileMap map = GridMap.Instance?.mainRegion?.innerMap;
            if (map == null) return (false, "No native region map is available for the climate oracle.");
            var diagram = (WhittakerDiagram)typeof(InnerTileMap).GetField("whittakerDiagram", RevisionMembers).GetValue(map);
            MethodInfo classify = world.GetType().Assembly.GetType("TruePlanet.NativeClimate", true).GetMethod("Classify", RevisionMembers);
            var temperatures = new HashSet<float>(); var moistures = new HashSet<float>();
            for (int i = 0; i <= 10; i++) { temperatures.Add(i / 10f); moistures.Add(i / 10f); }
            foreach (Range range in diagram.temperatureTypeTable.Values)
            { Add(temperatures, range.minimum, diagram.totalTemperatureRange); Add(temperatures, range.maximum, diagram.totalTemperatureRange); }
            foreach (Range range in diagram.precipitationTypeTable.Values)
            { Add(moistures, range.minimum, diagram.totalPrecipitationRange); Add(moistures, range.maximum, diagram.totalPrecipitationRange); }
            int checks = 0;
            foreach (float temperature in temperatures) foreach (float moisture in moistures)
            {
                Biome_Tile_Type expected = diagram.GetTileType(moisture, temperature);
                var actual = (Biome_Tile_Type)classify.Invoke(null, new object[] { temperature, moisture });
                if (actual != expected) return (false, "Climate mismatch at temperature=" + temperature + ", moisture=" + moisture + ": native=" + expected + ", atlas=" + actual);
                checks++;
            }
            return (true, checks + " climate samples, including both sides of every live native range boundary.");
            void Add(HashSet<float> values, float value, Range total)
            {
                float normalized = (value - total.minimum) / (total.maximum - total.minimum);
                foreach (float delta in new[] { -1e-6f, 0, 1e-6f })
                {
                    float sample = normalized + delta;
                    if (sample >= 0 && sample <= 1) values.Add(sample);
                }
            }
        }
    }
}
