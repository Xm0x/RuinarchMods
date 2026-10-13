using System;
using System.Globalization;

namespace TruePlanet
{
	public static class PlanetGenerator
	{
		public static Planet Generate(int seed, PlanetOptions options) => GenerateTracked(seed, options, null);
		internal static Planet GenerateTracked(int seed, PlanetOptions options, Action<string, int> progress)
		{
			options.Validate();
			var copy = new PlanetOptions { Provinces = options.Provinces, LandShare = options.LandShare, Nations = options.Nations,
				WildShare = options.WildShare, LocalSize = options.LocalSize, Resolution = options.Resolution };
			string identity = seed.ToString("X8") + "-" + copy.Provinces + "-" + copy.Nations + "-" + (int)copy.LocalSize
				+ "-" + BitConverter.DoubleToInt64Bits(copy.LandShare).ToString("X16") + "-" + BitConverter.DoubleToInt64Bits(copy.WildShare).ToString("X16");
			var planet = new Planet { Id = "planet-" + identity + "-g3-t1r" + (int)copy.Resolution + "-n1-l1",
				Name = "World " + seed.ToString(CultureInfo.InvariantCulture), Seed = seed, Options = copy,
				GeographyVersion = 3, FormatVersion = 3, TopologyVersion = 1, NationalGenerationVersion = 1, LocalMapVersion = 1 };
			progress?.Invoke("Calibrating oceans", 0);
			planet.SeaLevel = new PlanetGeography(planet).CalibrateSeaLevel(copy.LandShare);
			progress?.Invoke("Sampling land and coastlines", 1);
			TileLand land = TileLand.For(planet);
			progress?.Invoke("Placing nations, villages and roads", 2);
			NationalProvinces.Build(planet, new Random(seed), land);
			progress?.Invoke("Checking world integrity", 3);
			planet.Validate();
			progress?.Invoke("Planet generated", 4);
			return planet;
		}
	}
}
