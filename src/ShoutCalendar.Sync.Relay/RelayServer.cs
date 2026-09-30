using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ShoutCalendar.Core;

namespace ShoutCalendar.Sync;

public sealed class RelayServer : IDisposable
{
    private readonly RelayLog log = new();
    private readonly string directory;
    private readonly IDisposable lease;
    private readonly object gate = new();
    private readonly ConcurrentDictionary<TcpClient, Task> clients = new();
    private readonly SemaphoreSlim slots;
    private readonly CancellationTokenSource cancel = new();
    private TcpListener? listener;
    private Task? loop;
    private long second;
    private int bytesThisSecond;
    private int itemsThisSecond;
    private long persisted;

    public RelayServer(string storeDirectory, SyncLimits? limits = null)
    {
        this.directory = storeDirectory;
        this.log.Limits = (limits ?? new()).Clamp();
        this.slots = new SemaphoreSlim(this.log.Limits.MaxConnections);
        this.lease = RelayFiles.Acquire(storeDirectory);
        try { RelayFiles.Load(storeDirectory, this.log); this.persisted = this.log.Revision; }
        catch { this.lease.Dispose(); throw; }
    }
    public int Port { get; private set; }
    public void Start()
    {
        this.listener = new TcpListener(IPAddress.Loopback, 0);
        this.listener.Start();
        this.Port = ((IPEndPoint)this.listener.LocalEndpoint).Port;
        var token = this.cancel.Token;
        this.loop = Task.Run(() => this.Run(token));
    }
    public void Dispose()
    {
        this.cancel.Cancel();
        this.listener?.Stop();
        foreach (var client in this.clients.Keys) client.Dispose();
        try { this.loop?.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        this.cancel.Dispose(); this.slots.Dispose(); this.lease.Dispose();
    }
    private async Task Run(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await this.listener!.AcceptTcpClientAsync(token);
                if (!this.slots.Wait(0))
                {
                    try { client.SendTimeout = 1000; RelayIo.SendLine(client.GetStream(), RelayProtocol.Dropped); }
                    catch (Exception ex) when (ex is IOException or SocketException) { }
                    client.Dispose();
                    continue;
                }
                var task = Task.Run(() => this.Handle(client), CancellationToken.None);
                this.clients[client] = task;
                _ = task.ContinueWith(_ => this.clients.TryRemove(client, out Task? removed),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (token.IsCancellationRequested && ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        finally { await Task.WhenAll(this.clients.Values); }
    }
    private void Handle(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = 5000; client.SendTimeout = 5000;
                var stream = client.GetStream();
                var verb = RelayIo.ReadLine(stream);
                var signatureText = RelayIo.ReadLine(stream);
                if (!int.TryParse(RelayIo.ReadLine(stream), out var length)) throw new InvalidDataException();
                var body = RelayIo.ReadExact(stream, length);
                if (!RelayIo.TrySignature(signatureText, out var signature)) { RelayIo.SendLine(stream, RelayProtocol.Denied); return; }
                using var signer = SyncProof.CreatePlatformSigner();
                if (signature.Length != 64 || !signer.VerifyData(body, signature, HashAlgorithmName.SHA256))
                { RelayIo.SendLine(stream, RelayProtocol.Denied); return; }
                string status;
                byte[]? payload = null;
                lock (this.gate)
                {
                    var now = DateTimeOffset.UtcNow;
                    if (now.ToUnixTimeSeconds() != this.second)
                    {
                        this.second = now.ToUnixTimeSeconds(); this.bytesThisSecond = 0; this.itemsThisSecond = 0;
                        this.log.PruneExpired(now, 14);
                    }
                    if (verb == RelayProtocol.Submit)
                    {
                        var candidate = RelayCodec.Decode(body);
                        var cutoff = now.AddDays(-14);
                        status = candidate is not null && RelayLog.Expired(candidate, cutoff, DateOnly.FromDateTime(cutoff.UtcDateTime))
                            ? RelayProtocol.Refused : this.log.AcceptVerified(body, 1, this.bytesThisSecond, this.itemsThisSecond);
                        this.bytesThisSecond = (int)Math.Min(int.MaxValue, (long)this.bytesThisSecond + body.Length);
                        this.itemsThisSecond++;
                    }
                    else if (verb == RelayProtocol.Fetch)
                    {
                        status = this.log.ReadVerified(Encoding.UTF8.GetString(body), out var rows);
                        if (status == RelayProtocol.Ok) payload = RelayCodec.EncodeList(rows.Concat(this.log.Tombstones.Select(
                            key => new SyncAnnouncement { Id = "gone:" + key, ContentKey = key })));
                    }
                    else status = RelayProtocol.Refused;
                    if (this.persisted != this.log.Revision)
                    {
                        RelayFiles.Save(this.directory, this.log); this.persisted = this.log.Revision;
                    }
                }
                RelayIo.SendLine(stream, status);
                if (payload is not null)
                {
                    RelayIo.SendLine(stream, Convert.ToBase64String(signer.SignData(payload, HashAlgorithmName.SHA256)));
                    RelayIo.SendLine(stream, payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    RelayIo.Send(stream, payload);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or CryptographicException)
        {
            if (!this.cancel.IsCancellationRequested) Console.Error.WriteLine($"relay tcp: {ex.GetType().Name}");
        }
        finally { this.slots.Release(); }
    }
}
