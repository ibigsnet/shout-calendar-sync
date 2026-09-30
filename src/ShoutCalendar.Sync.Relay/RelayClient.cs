using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ShoutCalendar.Core;

namespace ShoutCalendar.Sync;

public sealed class RelayException(string status, HttpStatusCode? code = null, TimeSpan? retryAfter = null)
    : IOException($"Relay returned {status}.")
{
    public string Status { get; } = status;
    public HttpStatusCode? StatusCode { get; } = code;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed class RelayTransport : IDisposable
{
    private readonly HttpClient client;
    private readonly Dictionary<string, Cached> cache = new(StringComparer.Ordinal);
    private readonly TransferPacer download = new();
    private readonly TransferPacer upload = new();
    private sealed record Cached(string ETag, byte[] Payload);
    public int LastDownloadBytes { get; private set; }

    public RelayTransport(HttpMessageHandler? handler = null)
    {
        this.client = new HttpClient(handler ?? new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxConnectionsPerServer = 4, AutomaticDecompression = DecompressionMethods.None,
        }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public void Dispose() => this.client.Dispose();
    public void ResetCache() => this.cache.Clear();
    public static bool IsHttp(string host, int port) => port == SyncRelays.PublicPort
        || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || host.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    public async Task<string> SubmitAsync(string host, int port, SyncAnnouncement item, SyncLimits limits, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15)); token = timeout.Token;
        var body = RelayCodec.Encode(item);
        if (body.Length > 16_384) throw new RelayException(RelayProtocol.Refused);
        await this.upload.Pace(body.Length, limits.Clamp().UploadBytesPerSecond, token);
        if (!IsHttp(host, port)) return await Tcp(host, port, RelayProtocol.Submit, body, limits, token);
        using var response = await this.Send(host, "/v1/submit", body, null, token);
        var status = Status(response);
        if (response.IsSuccessStatusCode && status is RelayProtocol.Stored or RelayProtocol.Ok) return status;
        throw Failure(response);
    }

    public async Task<IReadOnlyList<SyncAnnouncement>?> FetchCatalogAsync(string host, SyncLimits limits, CancellationToken token = default) =>
        await this.HttpFetch(host, "/v1/catalog", "catalog", limits, true, token);

    public async Task<IReadOnlyList<SyncAnnouncement>> FetchAsync(string host, int port, string world, SyncLimits limits, CancellationToken token = default)
    {
        if (IsHttp(host, port)) return (await this.HttpFetch(host, "/v1/fetch", world, limits, false, token))!;
        var payload = await Tcp(host, port, RelayProtocol.Fetch, Encoding.UTF8.GetBytes(world), limits, token);
        if (!RelayCodec.TryDecodeList(Encoding.UTF8.GetBytes(payload), out var rows)) throw new InvalidDataException("Invalid relay catalog.");
        return Mark(rows);
    }

    private async Task<IReadOnlyList<SyncAnnouncement>?> HttpFetch(string host, string path, string body, SyncLimits limits, bool optional, CancellationToken token)
    {
        var cap = limits.Clamp();
        var maximum = Math.Min(cap.MaxMemoryBytes, 64_000_000);
        var key = host.ToLowerInvariant() + path + "|" + body;
        this.cache.TryGetValue(key, out var cached);
        if (cached is not null && cached.Payload.Length > maximum) { this.cache.Remove(key); cached = null; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(10.0 + (double)maximum / cap.BytesPerSecond, 15, 120)));
        token = timeout.Token;
        using var response = await this.Send(host, path, Encoding.UTF8.GetBytes(body), cached?.ETag, token);
        this.LastDownloadBytes = 0;
        byte[] payload;
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null) payload = cached.Payload;
        else
        {
            if (optional && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed) return null;
            if (!response.IsSuccessStatusCode || Status(response) != RelayProtocol.Ok) throw Failure(response);
            if (response.Content.Headers.ContentLength is long length && length > maximum)
                throw new InvalidDataException("Relay catalog exceeds the download buffer limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            payload = await this.ReadBounded(stream, maximum, cap.BytesPerSecond, token);
            this.LastDownloadBytes = payload.Length;
            var signature = response.Headers.TryGetValues("X-Sync-Signature", out var signatures) ? signatures.FirstOrDefault() : null;
            if (signature is null || !ValidSignature(payload, signature)) throw new InvalidDataException("Invalid relay signature.");
            if (!RelayCodec.TryDecodeList(payload, out _)) throw new InvalidDataException("Invalid relay catalog.");
            if (response.Headers.TryGetValues("X-Sync-Tombstones", out var removed))
            {
                var rows = RelayCodec.DecodeList(payload);
                var gone = string.Join(',', removed).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(value => value.Length <= 256).Select(value => new SyncAnnouncement { Id = "gone:" + value, ContentKey = value });
                payload = RelayCodec.EncodeList(rows.Concat(gone));
            }
            if (response.Headers.ETag is not null && payload.Length <= maximum)
            {
                if (this.cache.Count >= 8 || this.cache.Values.Sum(value => (long)value.Payload.Length) + payload.Length > maximum) this.cache.Clear();
                this.cache[key] = new Cached(response.Headers.ETag.ToString(), payload);
            }
        }
        if (!RelayCodec.TryDecodeList(payload, out var decoded)) throw new InvalidDataException("Invalid cached relay catalog.");
        return Mark(decoded);
    }

    private async Task<HttpResponseMessage> Send(string host, string path, byte[] body, string? etag, CancellationToken token)
    {
        var baseUrl = host.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? host.TrimEnd('/') : "https://" + host.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + path);
        request.Headers.Add("X-Sync-Protocol", RelayProtocol.Version.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-Sync-Share-Format", ShareFormat.Current.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-Sync-Signature", Convert.ToBase64String(SyncProof.Sign(body)));
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        request.Content = new ByteArrayContent(body);
        return await this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }

    private static string Status(HttpResponseMessage response) => response.Headers.TryGetValues("X-Sync-Status", out var values)
        ? values.FirstOrDefault() ?? "" : "";

    private static RelayException Failure(HttpResponseMessage response) => new(
        Status(response) is { Length: > 0 } status ? status : ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
        response.StatusCode, response.Headers.RetryAfter?.Delta);

    private static bool ValidSignature(byte[] payload, string value)
    {
        try { return SyncGate.Verify(payload, Convert.FromBase64String(value)); }
        catch (FormatException) { return false; }
    }

    private async Task<byte[]> ReadBounded(Stream stream, int maximum, int bytesPerSecond, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[Math.Min(16_384, bytesPerSecond)];
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) != 0)
        {
            if (buffer.Length + read > maximum) throw new InvalidDataException("Relay catalog exceeds the download buffer limit.");
            await this.download.Pace(read, bytesPerSecond, token);
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static IReadOnlyList<SyncAnnouncement> Mark(IReadOnlyList<SyncAnnouncement> rows)
    {
        foreach (var row in rows) { row.FromSync = true; row.HarvestedLocally = false; row.Accepted = false; }
        return rows;
    }

    private async Task<string> Tcp(string host, int port, string verb, byte[] body, SyncLimits limits, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30)); token = timeout.Token;
        using var socket = new TcpClient();
        await socket.ConnectAsync(host, port, token);
        using var stream = socket.GetStream();
        await stream.WriteAsync(RelayCodec.Frame(verb, SyncProof.Sign(body), body), token);
        var status = await ReadLine(stream, token);
        if (verb == RelayProtocol.Submit) return status is RelayProtocol.Stored or RelayProtocol.Ok ? status : throw new RelayException(status);
        if (status != RelayProtocol.Ok) throw new RelayException(status);
        var signature = await ReadLine(stream, token);
        var size = await ReadLine(stream, token);
        if (!int.TryParse(size, out var length) || length < 0 || length > Math.Min(limits.Clamp().MaxMemoryBytes, 64_000_000))
            throw new InvalidDataException("Invalid relay response length.");
        using var payload = new MemoryStream();
        var chunk = new byte[Math.Min(16_384, limits.Clamp().BytesPerSecond)];
        var remaining = length;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), token);
            if (read == 0) throw new EndOfStreamException();
            await this.download.Pace(read, limits.Clamp().BytesPerSecond, token);
            payload.Write(chunk, 0, read); remaining -= read;
        }
        var bytes = payload.ToArray();
        this.LastDownloadBytes = bytes.Length;
        if (!ValidSignature(bytes, signature)) throw new InvalidDataException("Invalid relay signature.");
        return Encoding.UTF8.GetString(bytes);
    }

    private static async Task<string> ReadLine(Stream stream, CancellationToken token)
    {
        var line = new List<byte>(); var one = new byte[1];
        while (line.Count <= 8192)
        {
            if (await stream.ReadAsync(one, token) == 0) throw new EndOfStreamException();
            if (one[0] == '\n') return Encoding.ASCII.GetString(line.ToArray());
            if (one[0] != '\r') line.Add(one[0]);
        }
        throw new InvalidDataException("Relay header is too long.");
    }
}

public sealed class TransferPacer
{
    private double scheduled;
    public async Task Pace(int bytes, int rate, CancellationToken token)
    {
        var now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        this.scheduled = Math.Max(this.scheduled, now - 1) + (double)bytes / Math.Max(1, rate);
        var delay = this.scheduled - now;
        if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), token);
    }
}

public static class RelayClient
{
    public static string Submit(string host, int port, SyncAnnouncement item, SyncLimits? limits = null, int openConnections = 1)
    {
        if (limits is not null && openConnections > limits.Clamp().MaxConnections) return RelayProtocol.Dropped;
        using var transport = new RelayTransport();
        try { return transport.SubmitAsync(host, port, item, limits ?? new()).GetAwaiter().GetResult(); }
        catch (RelayException ex) { return ex.Status; }
    }
    public static IReadOnlyList<SyncAnnouncement>? FetchCatalog(string host, SyncLimits? limits = null, int openConnections = 1)
    {
        if (limits is not null && openConnections > limits.Clamp().MaxConnections) throw new RelayException(RelayProtocol.Dropped);
        using var transport = new RelayTransport();
        return transport.FetchCatalogAsync(host, limits ?? new()).GetAwaiter().GetResult();
    }
    public static IReadOnlyList<SyncAnnouncement> Fetch(string host, int port, string world, SyncLimits? limits = null, int openConnections = 1)
    {
        if (limits is not null && openConnections > limits.Clamp().MaxConnections) throw new RelayException(RelayProtocol.Dropped);
        using var transport = new RelayTransport();
        return transport.FetchAsync(host, port, world, limits ?? new()).GetAwaiter().GetResult();
    }
}
