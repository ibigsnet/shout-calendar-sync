using System.Globalization;
using System.Text;
using ShoutCalendar.Core;

namespace ShoutCalendar.Sync;

public sealed class RelayMeter
{
    public static readonly TimeSpan ClientWindow = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan Recent = TimeSpan.FromSeconds(60);

    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(500);

    private readonly object gate = new();

    private readonly Queue<DateTimeOffset> drops = new();

    private readonly Queue<DateTimeOffset> slow = new();

    private readonly Dictionary<string, DateTimeOffset> clients = new(StringComparer.Ordinal);

    private readonly Dictionary<string, bool> peers = new(StringComparer.OrdinalIgnoreCase);

    private long stored;

    private long dropped;

    private long denied;

    private long refused;

    private long fetches;

    private long catalogs;

    private long requestTicks;

    private int inFlight;
    private long failures;
    public void BeginRequest() => Interlocked.Increment(ref this.inFlight);
    public void EndRequest() => Interlocked.Decrement(ref this.inFlight);
    public void NoteFailure() => Interlocked.Increment(ref this.failures);

    private long requests;
    private static readonly double[] DurationBounds = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];
    private readonly long[] durationBuckets = new long[DurationBounds.Length];

    public void NotePeer(string peer, bool up)
    {
        if (string.IsNullOrWhiteSpace(peer))
            return;
        lock (this.gate)
            this.peers[peer.Trim()] = up;
    }

    public void NoteClient(string? who, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(who))
            return;
        lock (this.gate)
        {
            if (this.clients.Count >= 10_000 && !this.clients.ContainsKey(who.Trim())) this.clients.Remove(this.clients.Keys.First());
            this.clients[who.Trim()] = now;
            this.TrimClients(now);
        }
    }

    public void NoteSubmit(string status)
    {
        lock (this.gate)
        {
            if (status == RelayProtocol.Stored || status == RelayProtocol.Ok)
                this.stored++;
            else if (status == RelayProtocol.Dropped)
            {
                this.dropped++;
                if (this.drops.Count >= 10_000) this.drops.Dequeue();
                this.drops.Enqueue(DateTimeOffset.UtcNow);
            }
            else if (status == RelayProtocol.Denied)
                this.denied++;
            else if (status == RelayProtocol.Refused)
                this.refused++;
        }
    }

    public void NoteFetch()
    {
        lock (this.gate)
            this.fetches++;
    }

    public void NoteCatalog()
    {
        lock (this.gate)
            this.catalogs++;
    }

    public void NoteDuration(TimeSpan elapsed, DateTimeOffset now)
    {
        lock (this.gate)
        {
            this.requests++;
            this.requestTicks += elapsed.Ticks;
            for (var i = 0; i < DurationBounds.Length; i++)
                if (elapsed.TotalSeconds <= DurationBounds[i]) this.durationBuckets[i]++;
            if (elapsed >= Slow)
            {
                if (this.slow.Count >= 10_000) this.slow.Dequeue();
                this.slow.Enqueue(now);
            }
            Trim(this.drops, now);
            Trim(this.slow, now);
        }
    }

    public RelayLoad Load(int storedBytes, int storedLimit, int peersConfigured, DateTimeOffset now)
    {
        lock (this.gate)
        {
            Trim(this.drops, now);
            Trim(this.slow, now);
            this.TrimClients(now);
            var ready = this.peers.Count >= peersConfigured && peersConfigured > 0;
            return new RelayLoad(
                storedBytes,
                storedLimit,
                this.drops.Count,
                this.slow.Count,
                ready ? peersConfigured : 0,
                this.peers.Count(pair => pair.Value),
                this.clients.Count);
        }
    }

    public string Prometheus(RelayLoad load, int events, int tombstones, int peersConfigured, int legacyRows)
    {
        lock (this.gate)
            return this.PrometheusUnlocked(load, events, tombstones, peersConfigured, legacyRows);
    }

    private string PrometheusUnlocked(RelayLoad load, int events, int tombstones, int peersConfigured, int legacyRows)
    {
        var degraded = RelayHealth.IsDegraded(load) ? 1 : 0;
        var seconds = this.requests == 0 ? 0 : (double)this.requestTicks / TimeSpan.TicksPerSecond;
        var text = new StringBuilder();
        Gauge(text, "shout_relay_in_flight", "Active admitted requests; excess connections receive 429.", Volatile.Read(ref this.inFlight));
        text.AppendLine("# TYPE shout_relay_failures_total counter");
        Series(text, "shout_relay_failures_total", null, Interlocked.Read(ref this.failures));
        Gauge(text, "shout_relay_events", "Stored invites.", events);
        Gauge(text, "shout_relay_stored_bytes", "Stored catalog bytes.", load.StoredBytes);
        Gauge(text, "shout_relay_stored_byte_limit", "Storage cap in bytes.", load.StoredLimit);
        Gauge(text, "shout_relay_tombstones", "Keys the relay will not store again.", tombstones);
        Gauge(text, "shout_relay_peers", "Upstream relays configured.", peersConfigured);
        Gauge(text, "shout_relay_peers_up", "Upstream relays that answered the last pull. These are serving this relay.", load.PeersUp);
        Gauge(text, "shout_relay_clients", "Distinct clients served in the last ten minutes.", load.Clients);
        Gauge(text, "shout_relay_degraded", "1 when the relay is degraded.", degraded);
        Gauge(text, "shout_relay_legacy_rows", "Stored rows from plugins older than the current parse rules.", legacyRows);
        text.AppendLine("# TYPE shout_relay_submits_total counter");
        Series(text, "shout_relay_submits_total", "stored", this.stored);
        Series(text, "shout_relay_submits_total", "dropped", this.dropped);
        Series(text, "shout_relay_submits_total", "denied", this.denied);
        Series(text, "shout_relay_submits_total", "refused", this.refused);
        text.AppendLine("# TYPE shout_relay_fetches_total counter");
        Series(text, "shout_relay_fetches_total", null, this.fetches);
        text.AppendLine("# TYPE shout_relay_catalog_pulls_total counter");
        Series(text, "shout_relay_catalog_pulls_total", null, this.catalogs);
        text.AppendLine("# TYPE shout_relay_request_seconds histogram");
        for (var i = 0; i < DurationBounds.Length; i++)
            text.Append("shout_relay_request_seconds_bucket{le=\"").Append(DurationBounds[i].ToString(CultureInfo.InvariantCulture))
                .Append("\"} ").Append(this.durationBuckets[i].ToString(CultureInfo.InvariantCulture)).AppendLine();
        text.Append("shout_relay_request_seconds_bucket{le=\"+Inf\"} ").Append(this.requests.ToString(CultureInfo.InvariantCulture)).AppendLine();
        text.Append("shout_relay_request_seconds_sum ").Append(seconds.ToString("0.######", CultureInfo.InvariantCulture)).AppendLine();
        text.Append("shout_relay_request_seconds_count ").Append(this.requests.ToString(CultureInfo.InvariantCulture)).AppendLine();
        return text.ToString();
    }

    private void TrimClients(DateTimeOffset now)
    {
        var stale = this.clients.Where(pair => now - pair.Value > ClientWindow).Select(pair => pair.Key).ToArray();
        foreach (var key in stale)
            this.clients.Remove(key);
    }

    private static void Trim(Queue<DateTimeOffset> queue, DateTimeOffset now)
    {
        while (queue.Count > 0 && now - queue.Peek() > Recent)
            queue.Dequeue();
    }

    private static void Gauge(StringBuilder text, string name, string help, int value)
    {
        text.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        text.Append("# TYPE ").Append(name).AppendLine(" gauge");
        text.Append(name).Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).AppendLine();
    }

    private static void Series(StringBuilder text, string name, string? result, long value)
    {
        text.Append(name);
        if (result is not null)
            text.Append("{result=\"").Append(result).Append("\"}");
        text.Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).AppendLine();
    }
}
