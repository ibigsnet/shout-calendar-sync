namespace ShoutCalendar.Core;

public sealed class ChannelModes
{
    public bool Listen { get; set; } = true;

    public bool Add { get; set; } = true;

    public bool Receive { get; set; }

    public bool Contribute { get; set; }
}

public sealed class ShareSettings
{
    public ChannelModes Shout { get; set; } = new();

    public ChannelModes Yell { get; set; } = new();

    public bool ShareUnaccepted { get; set; }

    public bool ShareAccepted { get; set; }

    public bool ShareNoteUpdates { get; set; }
}

public enum ContributionKind
{
    Unaccepted,
    Accepted,
    NoteUpdated,
}

public static class SharePolicy
{
    public const int ShoutChannel = 11;

    public const int YellChannel = 30;

    public static bool IsShareable(int channel) => channel is ShoutChannel or YellChannel;

    public static ChannelModes Modes(ShareSettings settings, int channel) =>
        channel == YellChannel ? settings.Yell : settings.Shout;

    public static bool ExportAllowed(int channel, ShareSettings settings)
    {
        if (!IsShareable(channel))
            return false;
        return Modes(settings, channel).Contribute;
    }

    public static bool ShouldContribute(int channel, ContributionKind kind, ShareSettings settings)
    {
        if (!ExportAllowed(channel, settings))
            return false;
        return kind switch
        {
            ContributionKind.Unaccepted => settings.ShareUnaccepted,
            ContributionKind.Accepted => settings.ShareAccepted,
            ContributionKind.NoteUpdated => settings.ShareNoteUpdates,
            _ => false,
        };
    }

    public static bool ShouldReceive(int channel, ShareSettings settings)
    {
        if (!IsShareable(channel))
            return false;
        return Modes(settings, channel).Receive;
    }
}

public static class SyncExport
{
    public static IReadOnlyList<SyncAnnouncement> FromLocal(SyncBook book, IEnumerable<CalendarEntry> local, PlaceCatalog? places = null, string? pluginVersion = null)
    {
        var rows = new List<SyncAnnouncement>();
        foreach (var entry in local)
        {
            var kind = entry.NoteUpdated
                ? ContributionKind.NoteUpdated
                : entry.Accepted ? ContributionKind.Accepted : ContributionKind.Unaccepted;
            if (entry.SeriesDeleted || entry.Manual || !SharePolicy.ShouldContribute(entry.Channel, kind, book.Settings))
                continue;
            var when = entry.DetectedAt == default ? DateTimeOffset.UtcNow : entry.DetectedAt;
            if (!ShoutHarvest.IsSharedEvent(entry.EventText, entry.Channel, when, places))
                continue;
            rows.Add(SyncAnnouncement.FromLocal(entry, book.Worlds.Home, pluginVersion));
        }

        return rows;
    }
}
