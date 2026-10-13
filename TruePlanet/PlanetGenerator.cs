using System;
using System.Globalization;

namespace TruePlanet
{
	public static class PlanetGenerator
	{
		public static Planet Generate(int seed, PlanetOptions options)
		{
			options.Validate();
			var copy = new PlanetOptions { Provinces = options.Provinces, LandShare = options.LandShare, Nations = options.Nations,
				WildShare = options.WildShare, LocalSize = options.LocalSize, Resolution = options.Resolution };
			string identity = seed.ToString("X8") + "-" + copy.Provinces + "-" + copy.Nations + "-" + (int)copy.LocalSize
				+ "-" + BitConverter.DoubleToInt64Bits(copy.LandShare).ToString("X16") + "-" + BitConverter.DoubleToInt64Bits(copy.WildShare).ToString("X16");
			var planet = new Planet { Id = "planet-" + identity + "-g3-t1r" + (int)copy.Resolution + "-n1-l1",
				Name = "World " + seed.ToString(CultureInfo.InvariantCulture), Seed = seed, Options = copy,
				GeographyVersion = 3, FormatVersion = 3, TopologyVersion = 1, NationalGenerationVersion = 1, LocalMapVersion = 1 };
			planet.SeaLevel = new PlanetGeography(planet).CalibrateSeaLevel(copy.LandShare);
			NationalProvinces.Build(planet, new Random(seed), TileLand.For(planet));
			planet.Validate();
			return planet;
		}
	}
}
