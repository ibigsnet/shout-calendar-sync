using System.Text.Json;

namespace ShoutCalendar.Core;

public static class SyncResume
{
    public readonly record struct Decision(bool RestoreStore, bool SaveMemory);

    public static Decision Choose(string? memoryJson, string? storeJson)
    {
        var memory = Peek(memoryJson);
        var store = Peek(storeJson);
        var sameBook = memory.BookId.Length > 0
            && store.BookId.Length > 0
            && string.Equals(memory.BookId, store.BookId, StringComparison.Ordinal);
        if (store.HasUserState && !memory.HasUserState && !sameBook)
            return new Decision(true, false);
        if (memory.HasUserState || sameBook)
            return new Decision(false, true);
        return new Decision(false, false);
    }

    private static Peeked Peek(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Peeked("", false);
        try
        {
            var snap = JsonSerializer.Deserialize<SyncSnapshot>(json, SnapshotJson.Options);
            if (snap is null)
                return new Peeked("", false);
            return new Peeked(snap.BookId ?? "", HasUserState(snap));
        }
        catch (JsonException)
        {
            return new Peeked("", false);
        }
    }

    private static bool HasUserState(SyncSnapshot snap)
    {
        if (snap.Events is { Count: > 0 })
            return true;
        if (snap.CheckedWorlds is { Count: > 0 })
            return true;
        if (!string.IsNullOrWhiteSpace(snap.RelayHost) || snap.RelayPort > 0)
            return true;
        if (!string.IsNullOrWhiteSpace(snap.BackupRelayHost) || snap.BackupRelayChoice is SyncRelays.CustomLabel or SyncRelays.OffLabel)
            return true;
        if (!snap.ShowSync || snap.Informedaholic || snap.MirrorRelay || snap.DebugPerf)
            return true;
        var settings = snap.Settings;
        if (settings is not null && (
            settings.ShareUnaccepted
            || settings.ShareAccepted
            || settings.ShareNoteUpdates
            || settings.Shout is { Contribute: true } or { Receive: true }
            || settings.Yell is { Contribute: true } or { Receive: true }))
            return true;
        if (snap.Limits is not null && !SameLimits(snap.Limits, new SyncLimits()))
            return true;
        return false;
    }

    private static int UploadBytes(SyncLimits limits) =>
        limits.UploadBytesPerSecond < 1 ? 125_000 : limits.UploadBytesPerSecond;

    private static bool SameLimits(SyncLimits left, SyncLimits right) =>
        left.MaxConnections == right.MaxConnections
        && left.BytesPerSecond == right.BytesPerSecond
        && UploadBytes(left) == UploadBytes(right)
        && left.MaxStoredBytes == right.MaxStoredBytes
        && left.MaxItemsPerTick == right.MaxItemsPerTick
        && left.MaxMemoryBytes == right.MaxMemoryBytes;

    private readonly record struct Peeked(string BookId, bool HasUserState);
}
