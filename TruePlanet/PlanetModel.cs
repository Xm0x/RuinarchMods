using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace TruePlanet
{
	public struct UnitVector
	{
		public double X, Y, Z;
		public UnitVector(double x, double y, double z) { X = x; Y = y; Z = z; }
		public static UnitVector operator +(UnitVector a, UnitVector b) => new UnitVector(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
		public static UnitVector operator -(UnitVector a, UnitVector b) => new UnitVector(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
		public static UnitVector operator *(UnitVector a, double b) => new UnitVector(a.X * b, a.Y * b, a.Z * b);
		public static double Dot(UnitVector a, UnitVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
		public static UnitVector Cross(UnitVector a, UnitVector b) => new UnitVector(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
		public UnitVector Normalized() => this * (1 / Math.Sqrt(Dot(this, this)));
		public static UnitVector FromMap(double longitude, double latitude)
		{
			double c = Math.Cos(latitude);
			return new UnitVector(c * Math.Cos(longitude), Math.Sin(latitude), c * Math.Sin(longitude));
		}
	}

	public enum ProvinceSize { Large, ExtraLarge, Huge }

	public sealed class PlanetOptions
	{
		public int Provinces = 120;
		public double LandShare = .4;
		public int Nations = 10;
		public double WildShare = .25;
		public ProvinceSize LargestProvince = ProvinceSize.Huge;
		public void Validate()
		{
			if (Provinces < 30 || Provinces > 400 || Nations < 2 || Nations > 40
				|| !Finite(LandShare) || LandShare < .2 || LandShare > .8
				|| !Finite(WildShare) || WildShare < 0 || WildShare > .75
				|| !Enum.IsDefined(typeof(ProvinceSize), LargestProvince))
				throw new ArgumentException("Use 30-400 provinces, 2-40 nations, 20-80% land and 0-75% wild land.");
		}
		internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
	}

	public sealed class Province
	{
		public int Id;
		public string Name;
		public UnitVector Direction;
		public int[] Neighbours;
		public bool Land, Visited, Held;
		public double Height, Area, Temperature, Moisture, Mountains, Water;
		// Grassland, forest, desert, snow, in that order.
		public double[] Biomes;
		public ProvinceSize Size;
		public int Nation = -1;
		public Village[] Villages = new Village[0];
	}
	public sealed class Village
	{
		public string Name;
		public int Nation = -1;
		public bool Elven;
		public bool Capital;
	}
	public sealed class Nation
	{
		public int Id, Capital;
		public string Name, Emblem;
		public bool Elven;
		public double Hue;
	}
	public sealed class Road { public int[] Path; }

	public sealed class Planet
	{
		public int FormatVersion = 1;
		public string Id, Name;
		public int Seed;
		public PlanetOptions Options;
		public Province[] Provinces;
		public Nation[] Nations = new Nation[0];
		public Road[] Roads = new Road[0];
		public int Nearest(UnitVector point)
		{
			int best = 0; double distance = double.NegativeInfinity;
			for (int i = 0; i < Provinces.Length; i++)
			{
				double dot = UnitVector.Dot(point, Provinces[i].Direction);
				if (dot > distance) { distance = dot; best = i; }
			}
			return best;
		}
		public void Validate()
		{
			if (FormatVersion != 1) throw new InvalidDataException("Unsupported planet format " + FormatVersion + ".");
			if (Options == null || string.IsNullOrWhiteSpace(Name) || string.IsNullOrEmpty(Id)
				|| Id.Any(c => !(char.IsLetterOrDigit(c) || c == '-')))
				throw new InvalidDataException("The planet identity or options are missing or invalid.");
			Options.Validate();
			if (Provinces == null || Provinces.Length != Options.Provinces || Nations == null || Roads == null)
				throw new InvalidDataException("The planet's collections do not match its options.");
			for (int i = 0; i < Provinces.Length; i++)
			{
				Province p = Provinces[i];
				if (p == null || p.Id != i || string.IsNullOrWhiteSpace(p.Name) || p.Neighbours == null || p.Neighbours.Length < 3
					|| !PlanetOptions.Finite(UnitVector.Dot(p.Direction, p.Direction)) || Math.Abs(UnitVector.Dot(p.Direction, p.Direction) - 1) > 1e-8
					|| p.Neighbours.Distinct().Count() != p.Neighbours.Length || p.Neighbours.Any(n => n < 0 || n >= Provinces.Length || n == i)
					|| p.Biomes == null || p.Biomes.Length != 4 || p.Biomes.Any(b => !PlanetOptions.Finite(b) || b < 0) || Math.Abs(p.Biomes.Sum() - 1) > 1e-8
					|| p.Nation < -1 || p.Nation >= Nations.Length || p.Villages == null || p.Villages.Length > 4
					|| !Enum.IsDefined(typeof(ProvinceSize), p.Size) || p.Size > Options.LargestProvince
					|| !(p.Area > 0) || !PlanetOptions.Finite(p.Area)
					|| !PlanetOptions.Finite(p.Height) || !PlanetOptions.Finite(p.Temperature) || !PlanetOptions.Finite(p.Moisture) || !PlanetOptions.Finite(p.Mountains) || !PlanetOptions.Finite(p.Water)
					|| p.Temperature < 0 || p.Temperature > 1 || p.Moisture < 0 || p.Moisture > 1 || p.Mountains < 0 || p.Mountains > 1 || p.Water < 0 || p.Water > 1
					|| (!p.Land && (p.Nation != -1 || p.Villages.Length != 0 || p.Held))
					|| p.Villages.Any(v => v == null || string.IsNullOrWhiteSpace(v.Name) || v.Nation < -1 || v.Nation >= Nations.Length))
					throw new InvalidDataException("Invalid province " + i + ".");
			}
			foreach (Province p in Provinces)
				foreach (int n in p.Neighbours)
					if (!Provinces[n].Neighbours.Contains(p.Id)) throw new InvalidDataException("Non-reciprocal province border.");
			var reached = new HashSet<int>();
			Reach(0, p => true, reached);
			if (reached.Count != Provinces.Length) throw new InvalidDataException("The sphere contains disconnected provinces.");
			for (int i = 0; i < Nations.Length; i++)
			{
				Nation n = Nations[i];
				if (n == null || n.Id != i || string.IsNullOrWhiteSpace(n.Name) || string.IsNullOrWhiteSpace(n.Emblem)
					|| n.Capital < 0 || n.Capital >= Provinces.Length || !PlanetOptions.Finite(n.Hue) || n.Hue < 0 || n.Hue >= 1
					|| Provinces[n.Capital].Nation != i || !Provinces[n.Capital].Land
					|| !Provinces[n.Capital].Villages.Any(v => v.Capital && v.Nation == i))
					throw new InvalidDataException("Invalid national capital.");
				reached.Clear(); Reach(n.Capital, p => p.Nation == i, reached);
				if (reached.Count != Provinces.Count(p => p.Nation == i)) throw new InvalidDataException("A nation's land is disconnected from its capital.");
			}
			foreach (Road road in Roads)
			{
				if (road == null || road.Path == null || road.Path.Length < 2 || road.Path.Any(n => n < 0 || n >= Provinces.Length || !Provinces[n].Land))
					throw new InvalidDataException("A road leaves land.");
				for (int i = 1; i < road.Path.Length; i++)
					if (!Provinces[road.Path[i - 1]].Neighbours.Contains(road.Path[i])) throw new InvalidDataException("A road crosses non-neighbouring provinces.");
			}
		}
		private void Reach(int start, Func<Province, bool> allowed, HashSet<int> reached)
		{
			var queue = new Queue<int>(); reached.Add(start); queue.Enqueue(start);
			while (queue.Count != 0)
				foreach (int n in Provinces[queue.Dequeue()].Neighbours)
					if (allowed(Provinces[n]) && reached.Add(n)) queue.Enqueue(n);
		}
	}
}
