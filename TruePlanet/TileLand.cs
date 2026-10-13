using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TruePlanet
{
	internal struct TileLandSample
	{
		internal UnitVector DryPoint;
		internal double LandArea;
		internal bool Eligible, Coastal;
	}

	// Geography only. Political ownership never affects these cached samples or links.
	internal sealed class TileLand
	{
		internal readonly TileLandSample[] Cells;
		internal readonly int[][] Links;
		private TileLand(TileLandSample[] cells, int[][] links) { Cells = cells; Links = links; }
		internal static TileLand For(Planet planet)
		{
			if (planet.FormatVersion != 3 || planet.GeographyVersion != 3 || planet.TopologyVersion != 1)
				throw new InvalidDataException("Unsupported tile land geography.");
			if (planet.Land == null) planet.Land = Build(planet.Mesh, new PlanetGeography(planet).Sample);
			return planet.Land;
		}
		internal static TileLand Build(TileMesh mesh, Func<UnitVector, TerrainSample> sample)
		{
			var cells = new TileLandSample[mesh.Centers.Length];
			for (int tile = 0; tile < cells.Length; tile++)
			{
				UnitVector center = mesh.Centers[tile], dry = center; double best = double.NegativeInfinity, landArea = 0;
				bool coastal = false;
				double Height(UnitVector point)
				{
					// Shared boundary points belong to both polygons. The recorded point must belong to this cell.
					point = (point + center * 1e-6).Normalized();
					double height = sample(point).Height;
					if (!PlanetOptions.Finite(height)) throw new InvalidDataException("Non-finite terrain height.");
					if (height > 0 && height > best) { best = height; dry = point; }
					if (height <= 0) coastal = true;
					return height;
				}
				void Triangle(UnitVector a, UnitVector b, UnitVector c)
				{
					double area = TileMesh.Triangle(a, b, c);
					int drySamples = (Height(a) > 0 ? 1 : 0) + (Height(b) > 0 ? 1 : 0) + (Height(c) > 0 ? 1 : 0)
						+ (Height((a + b + c).Normalized()) > 0 ? 1 : 0);
					landArea += area * drySamples * .25;
				}
				foreach (int edgeId in mesh.CellEdges[tile])
				{
					TileEdge edge = mesh.Edges[edgeId];
					UnitVector ab = (center + edge.First).Normalized(), bc = (edge.First + edge.Second).Normalized(), ca = (edge.Second + center).Normalized();
					Triangle(center, ab, ca); Triangle(ab, edge.First, bc); Triangle(ca, bc, edge.Second); Triangle(ab, bc, ca);
				}
				cells[tile] = new TileLandSample { DryPoint = dry, LandArea = landArea, Eligible = best > 0, Coastal = coastal };
			}
			var links = new List<int>[cells.Length];
			for (int i = 0; i < links.Length; i++) links[i] = new List<int>(mesh.Neighbours[i].Length);
			bool DryPath(UnitVector a, UnitVector b)
			{
				for (int i = 0; i <= 8; i++)
				{
					double t = i / 8.0;
					if (sample((a * (1 - t) + b * t).Normalized()).Height <= 0) return false;
				}
				return true;
			}
			foreach (TileEdge edge in mesh.Edges)
			{
				if (!cells[edge.A].Eligible || !cells[edge.B].Eligible) continue;
				for (int i = 1; i < 16; i++)
				{
					double t = i / 16.0; UnitVector point = (edge.First * (1 - t) + edge.Second * t).Normalized();
					if (sample(point).Height <= 0) continue;
					UnitVector a = (point * .95 + mesh.Centers[edge.A] * .05).Normalized(), b = (point * .95 + mesh.Centers[edge.B] * .05).Normalized();
					if (!DryPath(cells[edge.A].DryPoint, a) || !DryPath(a, point) || !DryPath(point, b) || !DryPath(b, cells[edge.B].DryPoint)) continue;
					links[edge.A].Add(edge.B); links[edge.B].Add(edge.A); break;
				}
			}
			return new TileLand(cells, links.Select(neighbours => neighbours.OrderBy(id => id).ToArray()).ToArray());
		}
	}
}
