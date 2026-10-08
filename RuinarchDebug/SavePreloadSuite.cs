using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using BayatGames.SaveGameFree.Serializers;
using HarmonyLib;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace RuinarchDebug
{
	public partial class AutoTest
	{
		private IEnumerator SavePreloadChecks()
		{
			Type type = AccessTools.TypeByName("RuinarchPerformance.SavePreloadCache");
			if (type == null)
			{
				Check("selected saves support a one-use native preload", () => (false, "production preload cache is absent"));
				yield break;
			}
			string directory = Path.Combine(Path.GetTempPath(), "ruinarch-preload-check-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			object cache = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null, new object[] { new SaveGameJsonSerializer() }, null);
			MethodInfo request = AccessTools.Method(type, "Request"), cancel = AccessTools.Method(type, "Cancel"), take = AccessTools.Method(type, "TryTake");
			bool Working() => (bool)AccessTools.Property(type, "IsWorking").GetValue(cache, null);
			bool Ready() => (bool)AccessTools.Property(type, "Ready").GetValue(cache, null);
			void Request(string path) => request.Invoke(cache, new object[] { path, Encoding.UTF8 });
			SaveDataCurrentProgress Take(string zip, Stream stream)
			{
				object[] args = { stream, zip, Encoding.UTF8, null };
				return (bool)take.Invoke(cache, args) ? (SaveDataCurrentProgress)args[3] : null;
			}
			IEnumerator Settle()
			{
				float deadline = Time.realtimeSinceStartup + 30f;
				while (Working() && Time.realtimeSinceStartup < deadline) yield return null;
				if (Working()) throw new TimeoutException("Isolated preload worker did not finish");
			}
			string first = Path.Combine(directory, "first.zip"), second = Path.Combine(directory, "second.zip");
			string extracted = Path.Combine(directory, "mainSave.sav");
			try
			{
				WritePreloadFixture(first, 123, extracted);
				Request(first); yield return Settle();
				Check("preloaded native state is handed over exactly once", () =>
				{
					using (var stream = File.OpenRead(extracted))
					{
						var snapshot = Take(first, stream);
						stream.Position = 0;
						return (snapshot != null && snapshot.tick == 123 && Take(first, stream) == null,
							"saved tick retained; consumed graph cannot be reused");
					}
				});

				Request(first); yield return Settle();
				DateTime stamp = File.GetLastWriteTimeUtc(first);
				long originalLength = new FileInfo(first).Length;
				WritePreloadFixture(first, 456, extracted); File.SetLastWriteTimeUtc(first, stamp);
				Check("preload rejects changed save bytes despite preserved timestamps", () =>
				{
					using (var stream = File.OpenRead(extracted))
					{
						bool rejected = Take(first, stream) == null;
						var native = new SaveGameJsonSerializer().Deserialize<SaveDataCurrentProgress>(stream, Encoding.UTF8);
						return (rejected && new FileInfo(first).Length == originalLength && native != null && native.tick == 456,
							$"rejected={rejected}; ZIP length={originalLength}/{new FileInfo(first).Length}; decoded tick={native?.tick}");
					}
				});

				WritePreloadFixture(second, 789, extracted);
				Request(first); Request(second); yield return Settle();
				Check("latest selected destination wins over obsolete decoding", () =>
				{
					using (var stream = File.OpenRead(extracted))
					{
						var snapshot = Take(second, stream);
						return (snapshot != null && snapshot.tick == 789, "only the latest ZIP can supply the loaded tick");
					}
				});

				Request(second); cancel.Invoke(cache, null); yield return Settle();
				Check("canceled preparation cannot resurrect a snapshot", () =>
				{
					using (var stream = File.OpenRead(extracted))
						return (!Ready() && Take(second, stream) == null, "worker completion cannot publish canceled state");
				});

				Request(first); yield return Settle();
				Check("another ZIP cannot consume the selected snapshot", () =>
				{
					using (var stream = File.OpenRead(extracted))
						return (Take(second, stream) == null, "destination path must match before bytes are considered");
				});

				string malformed = Path.Combine(directory, "malformed.zip"); File.WriteAllText(malformed, "not a zip");
				Request(malformed); yield return Settle();
				Check("invalid optional preparation never supplies a graph", () =>
				{
					using (var stream = File.OpenRead(extracted))
						return (!Ready() && Take(malformed, stream) == null, "normal native loading remains responsible for invalid-save errors");
				});

				Request(first); yield return Settle();
				string missing = Path.Combine(directory, "missing.zip");
				Request(missing); yield return Settle();
				Check("a missing destination retires the previous prepared save", () =>
				{
					using (var stream = File.OpenRead(extracted))
						return (!Ready() && Take(first, stream) == null, "old selected state is not retained after a missing-file request");
				});

				string oversized = Path.Combine(directory, "oversized.zip");
				using (var archive = ZipFile.Open(oversized, ZipArchiveMode.Create))
				using (var entry = archive.CreateEntry("mainSave.sav", CompressionLevel.Fastest).Open())
				{
					byte[] block = new byte[8192];
					for (long bytes = 0; bytes <= 128L * 1024 * 1024; bytes += block.Length) entry.Write(block, 0, block.Length);
				}
				Request(oversized); yield return Settle();
				Check("oversized save entries do not enter the RAM cache", () =>
				{
					using (var stream = File.OpenRead(extracted))
						return (!Ready() && Take(oversized, stream) == null, "entry-size guard rejects before constructing a native graph");
				});
			}
			finally
			{
				cancel.Invoke(cache, null);
				// All workers above have settled; only this fixture directory is removed.
				if (!Working()) Directory.Delete(directory, true);
			}
		}

		private static void WritePreloadFixture(string zip, int tick, string extracted)
		{
			var snapshot = new SaveDataCurrentProgress
			{
				fileName = "preload fixture", tick = tick, worldMapSave = new WorldMapSave(),
				objectHub = new System.Collections.Generic.Dictionary<OBJECT_TYPE, BaseSaveDataHub>()
			};
			var serializer = new SaveGameJsonSerializer();
			using (var stream = File.Create(extracted)) serializer.Serialize(snapshot, stream, Encoding.UTF8);
			if (File.Exists(zip)) File.Delete(zip);
			using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
			using (var output = archive.CreateEntry("mainSave.sav", CompressionLevel.NoCompression).Open())
			using (var input = File.OpenRead(extracted)) input.CopyTo(output);
			// Mono still deflates NoCompression entries. A ZIP comment makes both
			// fixture versions exactly the same length without altering entry bytes.
			using (var stream = File.OpenWrite(zip))
			using (var writer = new BinaryWriter(stream))
			{
				long length = stream.Length;
				int padding = checked((int)(4096 - length));
				if (padding < 0) throw new InvalidOperationException("Preload fixture exceeds its fixed ZIP size");
				stream.Position = length - 2; writer.Write((ushort)padding);
				stream.Position = length; writer.Write(new byte[padding]);
			}
		}
	}
}
