using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BayatGames.SaveGameFree.Serializers;
using FullSerializer;

namespace RuinarchPerformance
{
	// One decoder and one selected destination. Consumed graphs belong to the native loader.
	internal sealed class SavePreloadCache
	{
		internal const long MaximumEntryBytes = 128L * 1024 * 1024;
		private readonly object _gate = new object();
		private WorkItem _selected, _pending;
		private bool _working;
		private sealed class WorkItem
		{
			internal string Path;
			internal long ZipLength, Stamp, Length;
			internal Encoding Encoding;
			internal volatile bool Canceled;
			internal bool Finished;
			internal byte[] Hash;
			internal SaveDataCurrentProgress Snapshot;
		}
		internal SavePreloadCache(ISaveGameSerializer serializer)
		{
			if (serializer == null || serializer.GetType() != typeof(SaveGameJsonSerializer))
				throw new ArgumentException("Preloading requires the native JSON serializer", nameof(serializer));
		}
		internal bool IsWorking { get { lock (_gate) return _working; } }
		internal bool Ready { get { lock (_gate) return _selected?.Snapshot != null; } }
		internal void Request(string path, Encoding encoding)
		{
			string fullPath; long length, stamp;
			try
			{
				var file = new FileInfo(System.IO.Path.GetFullPath(path));
				fullPath = file.FullName; length = file.Length; stamp = file.LastWriteTimeUtc.Ticks;
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException)
			{ Cancel(); return; }
			lock (_gate)
			{
				if (_selected != null && SamePath(_selected.Path, fullPath) && _selected.ZipLength == length
					&& _selected.Stamp == stamp && ReferenceEquals(_selected.Encoding, encoding)) return;
				CancelLocked();
				_selected = _pending = new WorkItem { Path = fullPath, ZipLength = length, Stamp = stamp, Encoding = encoding };
				if (!_working)
				{
					_working = true;
					ThreadPool.QueueUserWorkItem(state => ((SavePreloadCache)state).DecodePending(), this);
				}
			}
		}
		internal void Cancel() { lock (_gate) CancelLocked(); }
		private void CancelLocked()
		{
			if (_selected != null) { _selected.Canceled = true; _selected.Snapshot = null; }
			_selected = _pending = null; Monitor.PulseAll(_gate);
		}
		private void DecodePending()
		{
			while (true)
			{
				WorkItem item;
				lock (_gate)
				{
					item = _pending; _pending = null;
					if (item == null) { _working = false; Monitor.PulseAll(_gate); return; }
				}
				SaveDataCurrentProgress snapshot = null;
				try
				{
					using (var file = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
					using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
					{
						var entry = archive.GetEntry("mainSave.sav");
						if (entry == null || entry.Length > MaximumEntryBytes) throw new InvalidDataException("Save entry is absent or exceeds the preload size guard");
						using (var stream = new HashingReadStream(entry.Open(), item))
						using (var reader = new StreamReader(stream, item.Encoding))
						{
							fsData data = fsJsonParser.Parse(reader.ReadToEnd());
							if (item.Canceled) throw new OperationCanceledException();
							// Same native codec/configuration, without emitting speculative load errors.
							new fsSerializer().TryDeserialize(data, ref snapshot).AssertSuccess();
							item.Hash = stream.FinishHash(); item.Length = stream.BytesRead;
						}
					}
					if (snapshot?.worldMapSave == null || snapshot.objectHub == null) snapshot = null;
				}
				catch (Exception error)
				{
					snapshot = null;
					if (!(error is OperationCanceledException)) RuinarchPerformance.Log?.Warning("Save preload skipped: " + error.Message);
				}
				lock (_gate)
				{
					if (ReferenceEquals(item, _selected) && !item.Canceled) item.Snapshot = snapshot;
					item.Finished = true; Monitor.PulseAll(_gate);
				}
			}
		}
		internal bool TryTake(Stream stream, string zipPath, Encoding encoding, out SaveDataCurrentProgress snapshot)
		{
			snapshot = null;
			if (!stream.CanRead || !stream.CanSeek) return false;
			WorkItem item;
			lock (_gate)
			{
				item = _selected;
				if (item == null || !SamePath(item.Path, System.IO.Path.GetFullPath(zipPath)) || !ReferenceEquals(item.Encoding, encoding)) return false;
				while (!item.Finished && !item.Canceled && ReferenceEquals(item, _selected)) Monitor.Wait(_gate);
				if (item.Canceled || !ReferenceEquals(item, _selected) || item.Snapshot == null) return false;
				_selected = null; // Reserve once before leaving the lock; another reader cannot consume it.
			}
			var prepared = item.Snapshot; item.Snapshot = null;
			long position = stream.Position;
			try
			{
				if (position != 0 || stream.Length != item.Length) return false;
				byte[] digest;
				using (var hash = SHA256.Create()) digest = hash.ComputeHash(stream);
				int difference = 0;
				for (int i = 0; i < digest.Length; i++) difference |= digest[i] ^ item.Hash[i];
				if (difference != 0) return false;
				snapshot = prepared; return true;
			}
			finally { stream.Position = position; }
		}
		private static bool SamePath(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

		private sealed class HashingReadStream : Stream
		{
			private readonly Stream _source;
			private readonly WorkItem _item;
			private readonly HashAlgorithm _hash = SHA256.Create();
			internal long BytesRead { get; private set; }
			internal HashingReadStream(Stream source, WorkItem item) { _source = source; _item = item; }
			internal byte[] FinishHash() { _hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0); return _hash.Hash; }
			public override int Read(byte[] buffer, int offset, int count)
			{
				if (_item.Canceled) throw new OperationCanceledException();
				int read = _source.Read(buffer, offset, count); BytesRead += read;
				if (BytesRead > MaximumEntryBytes) throw new InvalidDataException("Preload exceeds size guard");
				if (read != 0) _hash.TransformBlock(buffer, offset, read, buffer, offset);
				return read;
			}
			protected override void Dispose(bool disposing) { if (disposing) { _source.Dispose(); _hash.Dispose(); } base.Dispose(disposing); }
			public override bool CanRead => _source.CanRead;
			public override bool CanSeek => false;
			public override bool CanWrite => false;
			public override long Length => throw new NotSupportedException();
			public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
			public override void Flush() { }
			public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
			public override void SetLength(long value) => throw new NotSupportedException();
			public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		}
	}
}
