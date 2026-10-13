using System;
using System.Threading;

namespace TruePlanet
{
	internal sealed class GenerationProgress
	{
		internal readonly string Stage;
		internal readonly int Completed, Total;
		internal GenerationProgress(string stage, int completed, int total) { Stage = stage; Completed = completed; Total = total; }
	}
	internal sealed class GenerationOperation
	{
		private GenerationProgress _progress = new GenerationProgress("Preparing planet", 0, 4);
		internal GenerationProgress Progress => Volatile.Read(ref _progress);
		internal Planet Run(int seed, PlanetOptions options) => PlanetGenerator.GenerateTracked(seed, options,
			(stage, completed) => Volatile.Write(ref _progress, new GenerationProgress(stage, completed, 4)));
	}
}
