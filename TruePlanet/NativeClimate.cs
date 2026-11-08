namespace TruePlanet
{
    internal static class NativeClimate
    {
        // Serialized RegionInnerTileMap climate ranges from the supported native game asset.
        // Keep the game's own Whittaker table; do not invent forest/desert cutoffs from moisture.
        private static readonly WhittakerDiagram Diagram = BuildDiagram();

        private static WhittakerDiagram BuildDiagram()
        {
            float[] rain = { 0, 80, 140, 240, 320, 420, 500 };
            float[] temperature = { -10, -6, -2, 10, 16, 25, 30 };
            var diagram = new WhittakerDiagram
            {
                totalPrecipitationRange = new Range(0, 500), totalTemperatureRange = new Range(-10, 30),
                precipitationTypeTable = new PrecipitationTypeDictionary(), temperatureTypeTable = new TemperatureTypeDictionary()
            };
            for (int i = 0; i < 6; i++)
            {
                diagram.precipitationTypeTable.Add((Precipitation_Type)i, new Range(rain[i], rain[i + 1]));
                diagram.temperatureTypeTable.Add((Temperature_Type)i, new Range(temperature[i], temperature[i + 1]));
            }
            return diagram;
        }

        internal static Biome_Tile_Type Classify(float temperature, float moisture) =>
            Diagram.GetTileType(moisture, temperature);
    }
}
