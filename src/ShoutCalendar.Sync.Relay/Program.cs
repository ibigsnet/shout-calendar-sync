using ShoutCalendar.Core;

namespace ShoutCalendar.Sync;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
            return Usage();
        try { return args[0] switch
        {
            "serve" => Serve(args),
            "submit" => Submit(args),
            "fetch" => Fetch(args),
            "purge" => Purge(args),
            "health" => Health(args),
            _ => Usage(),
        }; }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.Net.Http.HttpRequestException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Purge(string[] args)
    {
        var store = Value(args, "--store");
        var id = Value(args, "--id");
        if (string.IsNullOrWhiteSpace(store) || string.IsNullOrWhiteSpace(id))
            return Usage();
        using var lease = RelayFiles.Acquire(store);
        var log = new RelayLog();
        RelayFiles.Load(store, log);
        if (!log.Purge(id))
        {
            Console.Error.WriteLine("not found");
            return 1;
        }

        RelayFiles.Save(store, log);
        Console.WriteLine("purged");
        return 0;
    }

    private static int Serve(string[] args)
    {
        var store = Value(args, "--store");
        var portFile = Value(args, "--port-file");
        var http = Value(args, "--http");
        if (string.IsNullOrWhiteSpace(store))
            return Usage();
        if (int.TryParse(http, out var httpPort) && httpPort > 0)
            return ServeHttp(store, httpPort, args);
        var limits = ReadLimits(args, http: false);

        if (string.IsNullOrWhiteSpace(portFile))
            return Usage();
        using var server = new RelayServer(store, limits);
        try
        {
            server.Start();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        File.WriteAllText(portFile, server.Port.ToString());
        Console.WriteLine($"listening {server.Port}");
        using var exit = new ManualResetEvent(false);
        using var term = OperatingSystem.IsWindows() ? null : System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM, context => { context.Cancel = true; exit.Set(); });
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            exit.Set();
        };
        exit.WaitOne();
        return 0;
    }

    private static int ServeHttp(string store, int port, string[] args)
    {
        var peers = (Value(args, "--peers") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var trusted = (Value(args, "--trusted-proxy") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var adminVariable = Value(args, "--admin-token-env") ?? "SHOUT_RELAY_ADMIN_TOKEN";
        var tokenFile = Value(args, "--admin-token-file");
        var adminToken = tokenFile is null ? Environment.GetEnvironmentVariable(adminVariable) : File.ReadAllText(tokenFile).Trim();
        if (tokenFile is not null && string.IsNullOrEmpty(adminToken)) throw new ArgumentException("Admin token file is empty.");
        using var server = new RelayHttp(store, port, ReadLimits(args, http: true), peers,
            adminToken: adminToken, trustedProxies: trusted,
            retentionDays: Number(args, "--retention-days", 14), requestsPerClient: Number(args, "--requests-per-client", 120),
            globalRequestsPerSecond: Number(args, "--requests-per-second", 2000));
        try
        {
            server.Start();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }

        Console.WriteLine($"listening {port}");
        using var exit = new ManualResetEvent(false);
        using var term = OperatingSystem.IsWindows() ? null : System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM, context => { context.Cancel = true; exit.Set(); });
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            exit.Set();
        };
        exit.WaitOne();
        return 0;
    }

    internal static SyncLimits ReadLimits(string[] args, bool http)
    {
        var limits = new SyncLimits { MaxConnections = http ? 128 : 4 };
        if (int.TryParse(Value(args, "--max-connections"), out var connections))
            limits.MaxConnections = connections;
        if (int.TryParse(Value(args, "--bytes-per-second"), out var rate))
            limits.BytesPerSecond = rate;
        if (int.TryParse(Value(args, "--max-stored-bytes"), out var stored))
            limits.MaxStoredBytes = stored;
        if (int.TryParse(Value(args, "--max-items"), out var items))
            limits.MaxItemsPerTick = items;
        if (int.TryParse(Value(args, "--max-memory"), out var memory))
            limits.MaxMemoryBytes = memory;

        return limits.Clamp();
    }

    private static int Number(string[] args, string option, int fallback) =>
        int.TryParse(Value(args, option), out var value) ? value : fallback;

    private static int Health(string[] args)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Get, Value(args, "--url") ?? "http://127.0.0.1:8787/v1/status");
        request.Headers.Add("X-Sync-Protocol", RelayProtocol.Version.ToString());
        request.Headers.Add("X-Sync-Share-Format", ShareFormat.Current.ToString());
        using var response = client.Send(request);
        return response.IsSuccessStatusCode && response.Headers.TryGetValues("X-Sync-Status", out var status)
            && status.FirstOrDefault() == RelayProtocol.Online ? 0 : 1;
    }

    private static int Submit(string[] args)
    {
        if (!Endpoint(args, out var port))
            return Usage();
        if (!int.TryParse(Value(args, "--channel"), out var channel))
            return Usage();
        var item = new SyncAnnouncement
        {
            Id = Value(args, "--id") ?? "",
            World = Value(args, "--world") ?? "",
            Channel = channel,
            Text = Value(args, "--text") ?? "",
        };
        if (item.Id.Length == 0 || item.World.Length == 0)
            return Usage();
        Console.WriteLine(RelayClient.Submit("127.0.0.1", port, item));
        return 0;
    }

    private static int Fetch(string[] args)
    {
        if (!Endpoint(args, out var port))
            return Usage();
        var world = Value(args, "--world");
        if (string.IsNullOrWhiteSpace(world))
            return Usage();
        var rows = RelayClient.Fetch("127.0.0.1", port, world);
        var says = 0;
        foreach (var item in rows)
        {
            if (item.Channel == 10)
                says++;
            Console.WriteLine($"world={item.World}");
            Console.WriteLine($"id={item.Id}");
            Console.WriteLine($"text={item.Text}");
            Console.WriteLine($"channel={item.Channel}");
            Console.WriteLine($"color={item.ColorToken}");
            Console.WriteLine($"pending={item.IsSyncPending.ToString().ToLowerInvariant()}");
        }

        Console.WriteLine($"says={says}");
        return 0;
    }

    private static bool Endpoint(string[] args, out int port)
    {
        port = 0;
        return int.TryParse(Value(args, "--port"), out port) && port > 0;
    }

    private static string? Value(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }

        return null;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("serve --store DIR --port-file FILE");
        Console.Error.WriteLine("serve --store DIR --http PORT [--peers URL,URL] [--trusted-proxy IP,IP] [--retention-days 14] [--requests-per-client 120] [--requests-per-second 2000]");
        Console.Error.WriteLine("submit --port N --id ID --world WORLD --channel N --text TEXT");
        Console.Error.WriteLine("fetch --port N --world WORLD");
        Console.Error.WriteLine("purge --store DIR --id ID (relay must be stopped)");
        Console.Error.WriteLine("health [--url http://127.0.0.1:8787/v1/status]");
        Console.Error.WriteLine("Server limits: --max-connections N --bytes-per-second N --max-stored-bytes N --max-items N --max-memory N");
        Console.Error.WriteLine("HTTP administration token: SHOUT_RELAY_ADMIN_TOKEN, --admin-token-env NAME, or --admin-token-file PATH");
        return 2;
    }
}
