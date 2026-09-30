using System.Collections.Concurrent;
using System.Text.Json;

namespace ShoutCalendar.Core;

public static class RelayFiles
{
    private static readonly ConcurrentDictionary<string, object> Writers = new(StringComparer.Ordinal);
    public static string EventsFile(string directory) => Path.Combine(directory, "events.json");
    public static string TombstoneFile(string directory) => Path.Combine(directory, "tombstones.txt");
    public static string SnapshotFile(string directory) => Path.Combine(directory, "store.json");

    public static IDisposable Acquire(string directory)
    {
        Directory.CreateDirectory(directory);
        try { return new FileStream(Path.Combine(directory, "store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("Relay store is in use. Stop the relay or use its HTTP administration endpoint.", ex); }
    }

    public static void Load(string directory, RelayLog log)
    {
        var path = SnapshotFile(directory);
        if (File.Exists(path) || File.Exists(path + ".bak"))
        {
            Snapshot snapshot;
            try { snapshot = Read(path); }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            {
                snapshot = Read(path + ".bak");
                AtomicFile.Write(path, File.ReadAllBytes(path + ".bak"));
            }
            log.Restore(snapshot.Events!);
            log.RestoreTombstones(snapshot.Tombstones!);
            return;
        }
        if (File.Exists(EventsFile(directory)))
        {
            var bytes = File.ReadAllBytes(EventsFile(directory));
            if (!RelayCodec.TryDecodeList(bytes, out var rows)) throw new InvalidDataException("Invalid legacy event store; refusing to replace it.");
            if (rows.Any(item => !RelayLog.Valid(item))) throw new InvalidDataException("Invalid legacy event record; refusing partial recovery.");
            log.Restore(rows);
        }
        if (File.Exists(TombstoneFile(directory))) log.RestoreTombstones(File.ReadAllLines(TombstoneFile(directory)));
    }

    private static Snapshot Read(string path)
    {
        var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllBytes(path));
        if (snapshot is not { Version: 1, Events: not null, Tombstones: not null }
            || snapshot.Events.Any(item => item is null || !RelayLog.Valid(item)) || snapshot.Tombstones.Any(key => key is null || key.Length > 256))
            throw new InvalidDataException("Invalid relay snapshot.");
        return snapshot;
    }

    public static void Save(string directory, RelayLog log)
    {
        lock (Writers.GetOrAdd(Path.GetFullPath(directory), static _ => new object()))
        {
            Directory.CreateDirectory(directory);
            var path = SnapshotFile(directory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Snapshot
            {
                Events = log.Events.ToArray(), Tombstones = log.Tombstones.ToArray(),
            });
            if (File.Exists(path)) AtomicFile.Write(path + ".bak", File.ReadAllBytes(path));
            AtomicFile.Write(path, bytes);
        }
    }

    private sealed class Snapshot
    {
        public int Version { get; set; } = 1;
        public SyncAnnouncement[]? Events { get; set; }
        public string[]? Tombstones { get; set; }
    }
}

public static class AtomicFile
{
    public static void Write(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
