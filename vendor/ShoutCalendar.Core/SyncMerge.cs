using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace ShoutCalendar.Core;

public enum SyncMergeResult
{
    New,
    Duplicate,
    Updated,
}

public static class SyncMerge
{
    public static string Key(SyncAnnouncement item)
    {
        var details = string.Join("|", LegacyKey(item), item.End, item.EndUtc?.ToString("O", CultureInfo.InvariantCulture),
            item.StartUtc?.ToString("O", CultureInfo.InvariantCulture), item.SourceTimeZone, item.Repeat, item.Category, item.ShareFormat, item.Title, item.Place);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(details)));
    }

    public static string LegacyKey(SyncAnnouncement item)
    {
        var text = string.Join(' ', (item.Text ?? "").Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        var clock = item.StartUtc is DateTimeOffset instant && string.IsNullOrEmpty(item.Repeat)
            ? instant.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            : $"{item.Date}|{item.Time}|{item.SourceTimeZone}|{item.Repeat}";
        var raw = $"{(item.World ?? "").Trim().ToLowerInvariant()}|{item.Channel}|{text}|{clock}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    public static SyncMergeResult Apply(List<SyncAnnouncement> events, SyncAnnouncement incoming)
    {
        incoming.ContentKey = Key(incoming);
        var sourceKey = incoming.ContentKey;
        var index = events.FindIndex(row => CanMergeAmong(events, row, incoming));
        if (index < 0)
        {
            if (incoming.Revision < 1)
                incoming.Revision = 1;
            EnrichDetails(incoming);
            incoming.ContentKey = Key(incoming);
            incoming.SourceKey = sourceKey;
            events.Add(incoming);
            return SyncMergeResult.New;
        }

        var current = events[index];
        if (incoming.Id == current.Id && incoming.Revision < current.Revision && incoming.ObservedAt <= current.ObservedAt) return SyncMergeResult.Duplicate;
        if (current.ContentKey == incoming.ContentKey && incoming.Revision < current.Revision && incoming.ObservedAt <= current.ObservedAt) return SyncMergeResult.Duplicate;
        PreferBody(current, incoming);
        var sameBody = Key(current) == Key(incoming);
        if (incoming.Revision <= current.Revision && sameBody)
        {
            current.SourceKey = sourceKey;
            if (incoming.ObservedAt > current.ObservedAt)
            {
                current.ObservedAt = incoming.ObservedAt;
                return SyncMergeResult.Updated;
            }
            return SyncMergeResult.Duplicate;
        }

        incoming.Revision = Math.Max(current.Revision, incoming.Revision);
        if (!string.IsNullOrEmpty(current.Id))
            incoming.Id = current.Id;
        incoming.Accepted = current.Accepted || incoming.Accepted;
        incoming.Declined = current.Declined;
        incoming.Hidden = current.Hidden;
        incoming.HarvestedLocally |= current.HarvestedLocally;
        incoming.ExcludedDates = current.ExcludedDates.ToList();
        incoming.RepeatUntil = current.RepeatUntil;
        incoming.SeriesDeleted = current.SeriesDeleted;
        incoming.SourceKey = sourceKey;
        if (current.Declined)
            incoming.Accepted = false;
        incoming.ContentKey = SyncMerge.Key(incoming);
        events[index] = incoming;
        return SyncMergeResult.Updated;
    }

    public static void Combine(SyncAnnouncement kept, SyncAnnouncement extra)
    {
        var correctedWorld = EventIdentity.CanCorrectWorld(kept, extra) ? extra.World : kept.World;
        PreferBody(kept, extra);
        kept.World = correctedWorld;
        kept.Text = extra.Text ?? kept.Text;
        kept.Title = extra.Title; kept.Place = extra.Place;
        if (!string.IsNullOrEmpty(extra.Date))
            kept.Date = extra.Date;
        if (!string.IsNullOrEmpty(extra.Time))
            kept.Time = extra.Time;
        if (string.IsNullOrEmpty(kept.World))
            kept.World = extra.World;
        if (extra.StartUtc is not null) kept.StartUtc = extra.StartUtc;
        if (extra.EndUtc is not null) kept.EndUtc = extra.EndUtc;
        if (extra.SourceTimeZone.Length > 0) kept.SourceTimeZone = extra.SourceTimeZone;
        if (extra.End.Length > 0) kept.End = extra.End;
        if (extra.Repeat.Length > 0) kept.Repeat = extra.Repeat;
        kept.ObservedAt = kept.ObservedAt > extra.ObservedAt ? kept.ObservedAt : extra.ObservedAt;
        kept.ExcludedDates = kept.ExcludedDates.Concat(extra.ExcludedDates).Distinct().ToList();
        kept.SeriesDeleted |= extra.SeriesDeleted;
        if (extra.RepeatUntil is DateOnly stop && (kept.RepeatUntil is null || stop < kept.RepeatUntil)) kept.RepeatUntil = stop;
        kept.Accepted = kept.Accepted || extra.Accepted;
        kept.Declined = kept.Declined || extra.Declined;
        kept.Hidden = kept.Hidden || extra.Hidden;
        kept.ClockEditedLocally |= extra.ClockEditedLocally;
        kept.HarvestedLocally |= extra.HarvestedLocally;
        if (kept.Declined)
            kept.Accepted = false;
        if (extra.Revision > kept.Revision)
            kept.Revision = extra.Revision;
        kept.ContentKey = Key(kept);
    }

    public static SyncAnnouncement? PrepareReplacement(IEnumerable<SyncAnnouncement> events, SyncAnnouncement incoming)
    {
        incoming.ContentKey = Key(incoming);
        var current = events.FirstOrDefault(row => CanMergeAmong(events, row, incoming));
        if (current is not null) PreferBody(current, incoming);
        else EnrichDetails(incoming);
        return current;
    }

    public static void PreferBody(SyncAnnouncement current, SyncAnnouncement incoming)
    {
        var correctWorld = EventIdentity.CanCorrectWorld(current, incoming);
        var preserveWorld = EventIdentity.CanCorrectWorld(incoming, current);
        var canonicalWorld = correctWorld ? incoming.World : preserveWorld ? current.World : "";
        var canonicalText = correctWorld ? incoming.Text : preserveWorld ? current.Text : "";
        var newer = incoming.ObservedAt > current.ObservedAt || (incoming.Id == current.Id && incoming.Revision > current.Revision);
        var title = incoming.Title.Length > 0 ? incoming.Title : EventTitle.Readable(EventTitle.Choose(incoming.Text));
        var oldTitle = current.Title.Length > 0 ? current.Title : EventTitle.Readable(EventTitle.Choose(current.Text));
        incoming.Title = title.Length == 0 || (!newer && oldTitle.Length > 0) ? oldTitle : title;
        var place = PlaceDetails(incoming);
        var oldPlace = PlaceDetails(current);
        var address = HousingTravel.DistrictName(place) is not null || NumberedPlace(place);
        var oldAddress = HousingTravel.DistrictName(oldPlace) is not null || NumberedPlace(oldPlace);
        incoming.Place = (!newer && oldPlace.Length > 0) || (oldAddress && !address) || place.Length == 0 ? oldPlace : place;
        if (!newer && current.ObservedAt > incoming.ObservedAt)
        {
            incoming.Text = current.Text;
            incoming.World = current.World;
            incoming.Date = current.Date.Length > 0 ? current.Date : incoming.Date;
            incoming.Time = current.Time.Length > 0 ? current.Time : incoming.Time;
            incoming.End = current.End.Length > 0 ? current.End : incoming.End;
            incoming.StartUtc = current.StartUtc ?? incoming.StartUtc;
            incoming.EndUtc = current.EndUtc ?? incoming.EndUtc;
            incoming.SourceTimeZone = current.SourceTimeZone.Length > 0 ? current.SourceTimeZone : incoming.SourceTimeZone;
            incoming.Repeat = current.Repeat.Length > 0 ? current.Repeat : incoming.Repeat;
        }
        if (canonicalWorld.Length > 0)
        {
            incoming.World = canonicalWorld;
            incoming.Text = canonicalText;
        }
        if (incoming.World != current.World && current.World.Length > 0)
            incoming.Place = incoming.Place.Replace(current.World, incoming.World, StringComparison.OrdinalIgnoreCase);
        if (incoming.Category.Length == 0) incoming.Category = current.Category;
        if (EventIdentity.SameSeries(current, incoming) || (canonicalWorld.Length > 0
            && EventIdentity.RepeatOf(current) is not null && EventIdentity.RepeatOf(current) == EventIdentity.RepeatOf(incoming)))
        {
            incoming.Repeat = EventIdentity.RepeatOf(current)!.Store();
            if (string.CompareOrdinal(current.Date, incoming.Date) < 0) incoming.Date = current.Date;
        }
        if (current.Repeat.Length > 0 && current.Repeat == incoming.Repeat
            && string.CompareOrdinal(current.Date, incoming.Date) < 0)
            incoming.Date = current.Date;

        if (current.ClockEditedLocally)
        {
            incoming.Date = current.Date;
            incoming.Time = current.Time;
            incoming.End = current.End;
            incoming.StartUtc = current.StartUtc;
            incoming.EndUtc = current.EndUtc;
            incoming.SourceTimeZone = current.SourceTimeZone;
            incoming.Repeat = current.Repeat;
            incoming.ClockEditedLocally = true;
            incoming.NoteUpdated = current.NoteUpdated;
        }
        var currentNow = ZoneClock.TryNowUntil(current.Text, out _);
        var incomingNow = ZoneClock.TryNowUntil(incoming.Text, out _);
        if (incomingNow && !currentNow)
        {
            incoming.Text = current.Text ?? "";
            incoming.StartUtc = current.StartUtc;
            incoming.EndUtc = current.EndUtc;
            incoming.SourceTimeZone = current.SourceTimeZone;
            incoming.End = current.End;
            if (!string.IsNullOrEmpty(current.Time))
                incoming.Time = current.Time;
            if (!string.IsNullOrEmpty(current.Date))
                incoming.Date = current.Date;
        }
        else if ((current.Text?.Length ?? 0) > (incoming.Text?.Length ?? 0) && !currentNow
            && (!newer || EventIdentity.IsAbbreviation(current.Text, incoming.Text)))
        {
            incoming.Text = current.Text ?? "";
        }

        if (string.IsNullOrEmpty(incoming.Time))
            incoming.Time = current.Time;
        if (string.IsNullOrEmpty(incoming.Date))
            incoming.Date = current.Date;
        if (string.IsNullOrEmpty(incoming.World))
            incoming.World = current.World;
        if (incoming.StartUtc is null && current.StartUtc is not null && incoming.Date == current.Date && incoming.Time == current.Time)
        {
            incoming.StartUtc = current.StartUtc;
            incoming.EndUtc = incoming.End.Length == 0 || incoming.End == current.End ? current.EndUtc
                : SyncClock.Instant(SyncClock.EndDate(DateOnly.TryParse(incoming.Date, out var day) ? day : null,
                    TimeOnly.TryParse(incoming.Time, out var start) ? start : null,
                    TimeOnly.TryParse(incoming.End, out var end) ? end : null), end, current.SourceTimeZone);
            incoming.SourceTimeZone = current.SourceTimeZone;
        }
        if (incoming.End.Length == 0) incoming.End = current.End;
        if (incoming.Repeat.Length == 0) incoming.Repeat = current.Repeat;
        if (canonicalWorld.Length > 0) incoming.Text = canonicalText;
        incoming.ObservedAt = current.ObservedAt > incoming.ObservedAt ? current.ObservedAt : incoming.ObservedAt;
    }

    public static bool CanMergeAmong(IEnumerable<SyncAnnouncement> events, SyncAnnouncement left, SyncAnnouncement right)
    {
        if (!Same(left, right)) return false;
        if (left.World.Equals(right.World, StringComparison.OrdinalIgnoreCase) || (left.Id.Length > 0 && left.Id == right.Id)) return true;
        var inferred = EventIdentity.CanCorrectWorld(left, right) ? left : right;
        return events.Append(left).Append(right)
            .Where(row => EventIdentity.CanCorrectWorld(inferred, row))
            .Select(row => row.World).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() == 1;
    }

    private static void EnrichDetails(SyncAnnouncement item)
    {
        if (item.Title.Length == 0) item.Title = EventTitle.Readable(EventTitle.Choose(item.Text));
        if (item.Place.Length == 0) item.Place = PlaceDetails(item);
    }

    private static bool NumberedPlace(string text) => System.Text.RegularExpressions.Regex.IsMatch(text,
        @"\b(?:ward|plot|[WP])\s*\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string PlaceDetails(SyncAnnouncement item) => item.Place.Length > 0 ? item.Place
        : ShoutHarvest.TryHarvest(item.Text, item.Channel, DateTimeOffset.UnixEpoch, aggressive: false)?.Place ?? "";

    public static bool Same(SyncAnnouncement row, SyncAnnouncement incoming)
    {
        if (incoming.Id.Length > 0 && row.Id == incoming.Id && incoming.Revision < row.Revision
            && incoming.ObservedAt <= row.ObservedAt) return true;
        if (!string.IsNullOrEmpty(incoming.Id) && row.Id == incoming.Id
            && (row.World.Equals(incoming.World, StringComparison.OrdinalIgnoreCase)
                || EventIdentity.CanCorrectWorld(row, incoming))
            && (incoming.Revision != row.Revision || row.ClockEditedLocally)) return true;
        if (EventIdentity.CanCorrectWorld(row, incoming) || EventIdentity.CanCorrectWorld(incoming, row)) return true;
        if (EventIdentity.SameSeries(row, incoming)) return true;
        if (!EventIdentity.Compatible(row, incoming)) return false;
        if (!string.IsNullOrEmpty(incoming.Id) && row.Id == incoming.Id
            && row.World.Equals(incoming.World, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrEmpty(row.ContentKey) && row.ContentKey == incoming.ContentKey)
            return true;
        if (EventIdentity.SameShout(row.World, row.Text, incoming.World, incoming.Text))
            return true;
        return EventIdentity.SameRepost(row, incoming);
    }
}

public sealed class SyncBuffer
{
    private readonly Dictionary<string, Held> waiting = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> acknowledged = new(StringComparer.Ordinal);
    private sealed record Held(DateTimeOffset Seen, SyncAnnouncement Item);

    public IReadOnlyList<SyncAnnouncement> Push(DateTimeOffset now, int holdOffSeconds, IEnumerable<SyncAnnouncement> incoming)
    {
        foreach (var item in incoming)
        {
            var key = SyncMerge.Key(item);
            if (this.acknowledged.TryGetValue(key, out var revision) && item.Revision <= revision) continue;
            if (this.waiting.TryGetValue(key, out var held))
            {
                if (item.Revision > held.Item.Revision) this.waiting[key] = held with { Item = item };
            }
            else if (this.waiting.Count < 16_384) this.waiting[key] = new Held(now, item);
        }
        var hold = TimeSpan.FromSeconds(Math.Max(0, holdOffSeconds));
        return this.waiting.Values.Where(row => now - row.Seen >= hold).Select(row => row.Item).ToArray();
    }

    public void Acknowledge(IEnumerable<SyncAnnouncement> consumed)
    {
        foreach (var item in consumed)
        {
            var key = SyncMerge.Key(item);
            this.waiting.Remove(key);
            if (this.acknowledged.Count >= 16_384) this.acknowledged.Remove(this.acknowledged.Keys.First());
            this.acknowledged[key] = item.Revision;
        }
    }

    public void Reset() { this.waiting.Clear(); this.acknowledged.Clear(); }
}

public sealed record RelayEndpoint(string Host, int Port);

public static class SyncRelays
{
    public const string PublicLabel = "Public relay";

    public const string CustomLabel = "Custom";
    public const string OffLabel = "Off";

    public static string PublicHost { get; } = typeof(SyncRelays).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
        .Cast<System.Reflection.AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "PublicRelayHost")?.Value?.Trim() is { Length: > 0 } host
            ? host : "public-relay.invalid";

    public static bool PublicConfigured => PublicHost != "public-relay.invalid";

    public const int PublicPort = 443;

    public static bool IsPublic(string? host, int port) =>
        !string.IsNullOrWhiteSpace(host) && SameEndpoint(host, port, PublicHost, PublicPort);

    private static bool IsPublicHost(string? host) =>
        string.Equals(HostOnly(host), HostOnly(PublicHost), StringComparison.OrdinalIgnoreCase);

    private static string HostOnly(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return "";
        var value = host.Trim();
        if (Uri.TryCreate(value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value,
            UriKind.Absolute, out var uri)) return uri.IdnHost.TrimEnd('.');
        return value.TrimEnd('.');
    }

    public static bool SameEndpoint(string firstHost, int firstPort, string secondHost, int secondPort) =>
        EndpointKey(firstHost, firstPort) == EndpointKey(secondHost, secondPort);

    private static string EndpointKey(string host, int port)
    {
        var http = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if ((http || port == PublicPort) && Uri.TryCreate(http ? host : "https://" + host, UriKind.Absolute, out var uri))
            return $"{uri.Scheme}://{uri.IdnHost.TrimEnd('.').ToLowerInvariant()}:{uri.Port}{uri.AbsolutePath.TrimEnd('/')}";
        return $"tcp://{host.Trim().TrimEnd('.').ToLowerInvariant()}:{port}";
    }

    public static string Display(string? host, int port)
    {
        if (string.IsNullOrWhiteSpace(host)) return "";
        if (IsPublicHost(host) || host == PublicLabel) return PublicLabel;
        if (Uri.TryCreate(host, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri.Authority;
        return HostOnly(host) + ":" + port;
    }

    public static string Redact(string text) => string.IsNullOrEmpty(text) ? text
        : text.Replace(PublicHost + ":" + PublicPort, PublicLabel, StringComparison.OrdinalIgnoreCase)
            .Replace(PublicHost, PublicLabel, StringComparison.OrdinalIgnoreCase);
}

public static class RelayReach
{
    private static readonly HttpClient StatusClient = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        { Timeout = TimeSpan.FromSeconds(4) };
    public const string Resolving = "Resolving";

    public const string Online = "Online";

    public const string Degraded = "Degraded";

    public const string Offline = "Offline";

    public const string OutOfDate = "Out of date";

    public const string ViaPublicRelay = "Via public relay";

    public static string Prefer(string primary, bool mirror, bool primaryIsPublic, string publicStatus)
    {
        if (primary == Online || primary == Degraded || primary == OutOfDate)
            return primary;
        if (!mirror || primaryIsPublic)
            return primary;
        if (publicStatus == Online)
            return ViaPublicRelay;
        if (publicStatus == OutOfDate)
            return OutOfDate;
        return Offline;
    }

    public static string Read(string host, int port, bool mirror = false)
    {
        var primary = Probe(host, port);
        if (primary == Online || primary == Degraded || primary == OutOfDate || !mirror || SyncRelays.IsPublic(host, port))
            return primary;
        return Prefer(primary, true, false, Probe(SyncRelays.PublicHost, SyncRelays.PublicPort));
    }

    public static async Task<string> ReadAsync(string host, int port, bool mirror, CancellationToken token)
    {
        var status = await ProbeAsync(host, port, token);
        if (status != Offline || !mirror || SyncRelays.IsPublic(host, port)) return status;
        return Prefer(status, true, false, await ProbeAsync(SyncRelays.PublicHost, SyncRelays.PublicPort, token));
    }

    private static async Task<string> ProbeAsync(string host, int port, CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            if (port == SyncRelays.PublicPort || host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var url = host.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? host.TrimEnd('/') : "https://" + host;
                using var request = new HttpRequestMessage(HttpMethod.Get, url + "/v1/status");
                request.Headers.Add("X-Sync-Protocol", RelayProtocol.Version.ToString(CultureInfo.InvariantCulture));
                request.Headers.Add("X-Sync-Share-Format", ShareFormat.Current.ToString(CultureInfo.InvariantCulture));
                using var response = await StatusClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var value = response.Headers.TryGetValues("X-Sync-Status", out var values) ? values.FirstOrDefault() : null;
                return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.UpgradeRequired ? Describe(value) : Offline;
            }
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(host, port, timeout.Token);
            return Online;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return Offline; }
    }

    private static string Probe(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host) || port < 1)
            return Offline;
        try
        {
            if (port == SyncRelays.PublicPort)
                return ReadHttp($"https://{host}/v1/status");
            using var client = new System.Net.Sockets.TcpClient();
            if (!client.ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(4)) || !client.Connected)
                return Offline;
            return Online;
        }
        catch (Exception)
        {
            return Offline;
        }
    }

    public static string ReadHttp(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("X-Sync-Protocol", RelayProtocol.Version.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Sync-Share-Format", ShareFormat.Current.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Sync-Signature", Convert.ToBase64String(new byte[64]));
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("1"));
        using var response = StatusClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
        var status = response.Headers.TryGetValues("X-Sync-Status", out var values) ? values.FirstOrDefault() : "";
        return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.UpgradeRequired ? Describe(status) : Offline;
    }

    public static string Describe(string? status)
    {
        if (string.Equals(status, RelayProtocol.Upgrade, StringComparison.OrdinalIgnoreCase))
            return OutOfDate;
        if (string.Equals(status, RelayProtocol.Degraded, StringComparison.OrdinalIgnoreCase))
            return Degraded;
        return string.Equals(status, RelayProtocol.Online, StringComparison.OrdinalIgnoreCase) ? Online : Offline;
    }
}
