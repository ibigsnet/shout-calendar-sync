namespace ShoutCalendar.Core;

public interface ISyncTransport
{
    IReadOnlyList<SyncAnnouncement> Fetch(IReadOnlyList<string> worlds, byte[] proof);
}

public static class SyncLoop
{
    public static int Step(SyncBook book, ISyncTransport transport, byte[]? proof)
    {
        if (!SyncGate.AllowRead(proof) || proof is null)
            return 0;

        IReadOnlyList<SyncAnnouncement> incoming;
        try
        {
            incoming = transport.Fetch(book.Worlds.Fetched(), proof) ?? Array.Empty<SyncAnnouncement>();
        }
        catch (Exception)
        {
            return 0;
        }

        var local = book.CopyEvents().Where(item => item.HarvestedLocally).Select(item => (item.Id, item.Text)).ToArray();
        var added = book.Ingest(proof, incoming, openConnections: 1);
        foreach (var (id, text) in local)
        {
            var kept = book.CopyEvents().FirstOrDefault(item => item.Id == id && item.HarvestedLocally);
            if (kept is null || kept.Text != text)
                throw new InvalidOperationException("A background sync changed a local event.");
        }

        return Math.Max(0, added);
    }
}

public static class SyncStore
{
    public static void Save(string path, SyncBook book)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        AtomicFile.Write(path, System.Text.Encoding.UTF8.GetBytes(book.ToJson()));
    }

    public static bool Load(string path, SyncBook book)
    {
        if (!File.Exists(path))
            return false;
        return book.ApplyJson(File.ReadAllText(path));
    }

    public static void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
