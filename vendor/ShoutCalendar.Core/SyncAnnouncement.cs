using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ShoutCalendar.Core;

public sealed class SyncAnnouncement
{
    public string Id { get; set; } = "";

    public string World { get; set; } = "";

    public int Channel { get; set; }

    public string Text { get; set; } = "";

    public string Title { get; set; } = "";
    public string Place { get; set; } = "";

    public bool FromSync { get; set; }

    public bool HarvestedLocally { get; set; }

    public bool Accepted { get; set; }

    public bool NoteUpdated { get; set; }

    public string Category { get; set; } = "";

    public string Date { get; set; } = "";

    public string Time { get; set; } = "";

    public DateTimeOffset? StartUtc { get; set; }
    public DateTimeOffset? EndUtc { get; set; }
    public string SourceTimeZone { get; set; } = "";
    public string End { get; set; } = "";

    public DateTimeOffset ObservedAt { get; set; }

    public string Repeat { get; set; } = "";

    public int Revision { get; set; }

    public string ContentKey { get; set; } = "";

    public int ShareFormat { get; set; } = global::ShoutCalendar.Core.ShareFormat.Current;

    public string PluginVersion { get; set; } = "";

    public bool ClockEditedLocally { get; set; }

    public string SourceKey { get; set; } = "";
    public List<DateOnly> ExcludedDates { get; set; } = new();
    public DateOnly? RepeatUntil { get; set; }
    public bool SeriesDeleted { get; set; }

    public bool Declined { get; set; }

    public bool Hidden { get; set; }

    public bool IsSyncPending => !this.SeriesDeleted && this.FromSync && !this.HarvestedLocally && !this.Accepted && !this.Declined && !this.Hidden;

    public string ColorToken => this.IsSyncPending ? "sync-pending" : this.Accepted ? "accepted" : "pending";

    public int PayloadBytes => RelayCodec.Encode(this).Length;

    public static SyncAnnouncement FromLocal(CalendarEntry entry, string homeWorld, string? pluginVersion = null, TimeZoneInfo? calendarZone = null)
    {
        var world = ShareWorld.Choose(homeWorld, entry.SpeakerWorld, entry.Server, entry.EventText);

        var item = new SyncAnnouncement
        {
            Id = entry.Id,
            World = world,
            Channel = entry.Channel,
            Text = entry.EventText,
            Title = EventTitle.Readable(EventTitle.Choose(entry.EventText)), Place = entry.Place,
            HarvestedLocally = true,
            Accepted = entry.Accepted,
            NoteUpdated = entry.NoteUpdated,
            Date = entry.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            Time = entry.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "",
            ObservedAt = entry.DetectedAt,
            Repeat = entry.Repeat?.Store() ?? "",
            Revision = Math.Max(entry.Revision, entry.NoteUpdated ? 2 : 1),
            ShareFormat = global::ShoutCalendar.Core.ShareFormat.Current,
            PluginVersion = (pluginVersion ?? "").Trim(),
        };
        SyncClock.Stamp(item, entry, calendarZone ?? TimeZoneInfo.Local);
        return item;
    }
}

public static class EventCategories
{
    public static readonly IReadOnlyList<string> BuiltIn =
    [
        "bard show",
        "summoner show",
        "dance club",
        "gambling event",
    ];

    public static bool TrySet(SyncAnnouncement announcement, string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return false;
        announcement.Category = label.Trim();
        return true;
    }
}

public static class RelayProtocol
{
    public const string Submit = "SUBMIT";

    public const string Fetch = "FETCH";

    public const string Stored = "stored";

    public const string Refused = "refused";

    public const string Denied = "denied";

    public const string Dropped = "dropped";

    public const string Ok = "ok";

    public const string Online = "online";

    public const string Degraded = "degraded";

    public const string Upgrade = "upgrade";

    public const int Version = 1;
}

public static class RelayCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static byte[] Encode(SyncAnnouncement item) =>
        JsonSerializer.SerializeToUtf8Bytes(Wire.From(item), Options);

    public static byte[] EncodeList(IEnumerable<SyncAnnouncement> items) =>
        JsonSerializer.SerializeToUtf8Bytes(items.Select(Wire.From).ToArray(), Options);

    public static SyncAnnouncement? Decode(ReadOnlySpan<byte> payload)
    {
        try
        {
            var wire = JsonSerializer.Deserialize<Wire>(payload, Options);
            return wire?.ToAnnouncement();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<SyncAnnouncement> DecodeList(ReadOnlySpan<byte> payload) =>
        TryDecodeList(payload, out var rows) ? rows : Array.Empty<SyncAnnouncement>();

    public static bool TryDecodeList(ReadOnlySpan<byte> payload, out IReadOnlyList<SyncAnnouncement> rows)
    {
        rows = Array.Empty<SyncAnnouncement>();
        try
        {
            var wires = JsonSerializer.Deserialize<Wire[]>(payload, Options);
            if (wires is null || wires.Any(wire => wire is null)) return false;
            rows = wires.Select(wire => wire.ToAnnouncement()).ToArray();
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static byte[] Frame(string verb, byte[] signature, byte[] body)
    {
        var header = Encoding.ASCII.GetBytes($"{verb}\n{Convert.ToBase64String(signature)}\n{body.Length}\n");
        var framed = new byte[header.Length + body.Length];
        header.CopyTo(framed, 0);
        body.CopyTo(framed, header.Length);
        return framed;
    }

    private sealed class Wire
    {
        public string Id { get; set; } = "";

        public string World { get; set; } = "";

        public int Channel { get; set; }

        public string Text { get; set; } = "";

        public string Title { get; set; } = "";
        public string Place { get; set; } = "";

        public bool Accepted { get; set; }

        public bool NoteUpdated { get; set; }

        public bool HarvestedLocally { get; set; }

        public bool FromSync { get; set; }

        public string Category { get; set; } = "";

        public string Date { get; set; } = "";

        public string Time { get; set; } = "";

        public DateTimeOffset? StartUtc { get; set; }
        public DateTimeOffset? EndUtc { get; set; }
        public string SourceTimeZone { get; set; } = "";
        public string End { get; set; } = "";
        public DateTimeOffset ObservedAt { get; set; }
        public string Repeat { get; set; } = "";

        public int Revision { get; set; }

        public string ContentKey { get; set; } = "";

        public int ShareFormat { get; set; }

        public string PluginVersion { get; set; } = "";

        public static Wire From(SyncAnnouncement item) => new()
        {
            Id = item.Id,
            World = item.World,
            Channel = item.Channel,
            Text = item.Text,
            Title = item.Title, Place = item.Place,
            Accepted = item.Accepted,
            NoteUpdated = item.NoteUpdated,
            HarvestedLocally = item.HarvestedLocally,
            FromSync = item.FromSync,
            Category = item.Category,
            Date = item.Date,
            Time = item.Time,
            StartUtc = item.StartUtc,
            EndUtc = item.EndUtc,
            SourceTimeZone = item.SourceTimeZone ?? "",
            End = item.End ?? "",
            ObservedAt = item.ObservedAt,
            Repeat = item.Repeat,
            Revision = item.Revision,
            ContentKey = item.ContentKey,
            ShareFormat = item.ShareFormat,
            PluginVersion = item.PluginVersion,
        };

        public SyncAnnouncement ToAnnouncement() => new()
        {
            Id = this.Id ?? "",
            World = this.World ?? "",
            Channel = this.Channel,
            Text = this.Text ?? "",
            Title = this.Title ?? "", Place = this.Place ?? "",
            Accepted = this.Accepted,
            NoteUpdated = this.NoteUpdated,
            HarvestedLocally = this.HarvestedLocally,
            FromSync = this.FromSync,
            Category = this.Category ?? "",
            Date = this.Date ?? "",
            Time = this.Time ?? "",
            StartUtc = this.StartUtc,
            EndUtc = this.EndUtc,
            SourceTimeZone = this.SourceTimeZone ?? "",
            End = this.End ?? "",
            ObservedAt = this.ObservedAt,
            Repeat = this.Repeat ?? "",
            Revision = this.Revision,
            ContentKey = this.ContentKey ?? "",
            ShareFormat = this.ShareFormat,
            PluginVersion = this.PluginVersion ?? "",
        };
    }
}
