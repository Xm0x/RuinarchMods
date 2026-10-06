using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace TruePlanet
{
	public static class PlanetStore
	{
		private static readonly JsonSerializerSettings Json = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error, Formatting = Formatting.Indented };
		public static Planet Load(string path)
		{
			Planet planet;
			try { planet = JsonConvert.DeserializeObject<Planet>(File.ReadAllText(path), Json); }
			catch (JsonException e) { throw new InvalidDataException("The atlas is not valid planet JSON: " + e.Message, e); }
			if (planet == null) throw new InvalidDataException("The atlas file is empty.");
			planet.Validate(); return planet;
		}
		public static string Save(string root, Planet planet)
		{
			planet.Validate();
			string directory = Path.Combine(root, planet.Id), path = Path.Combine(directory, "planet.json"), temp = path + ".tmp";
			Directory.CreateDirectory(directory);
			try
			{
				using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
				using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
				{
					writer.Write(JsonConvert.SerializeObject(planet, Json)); writer.Flush(); stream.Flush(true);
				}
				if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
			}
			finally { if (File.Exists(temp)) File.Delete(temp); }
			return path;
		}
		public static string[] Files(string root) => Directory.Exists(root)
			? Directory.GetDirectories(root).Select(d => Path.Combine(d, "planet.json")).Where(File.Exists).OrderBy(p => p, StringComparer.Ordinal).ToArray()
			: new string[0];
	}
}
