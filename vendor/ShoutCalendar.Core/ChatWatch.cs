namespace ShoutCalendar.Core;

public static class ChatWatch
{
    public static string Report(IEnumerable<string> logDirectories, PlaceCatalog? places = null, TimeZoneInfo? zone = null)
    {
        var newest = NewestLog(logDirectories);
        if (newest is null)
            return "no log is present";

        var lines = ChatLogReader.Read(File.ReadAllBytes(newest));
        var report = new List<string> { "log: " + newest };
        foreach (var line in lines)
        {
            var when = Stamp(line.TimestampUnix);
            var loose = ShoutHarvest.TryHarvest(line.Message, line.Channel, when, places, aggressive: false, zone: zone);
            if (loose is null)
                continue;
            var kept = ShoutHarvest.TryHarvest(line.Message, line.Channel, when, places, aggressive: true, zone: zone);
            report.Add("line: " + line.Message.Replace('\n', ' ').Replace('\r', ' '));
            report.Add(kept is not null ? "kept: yes" : "kept: no");
            var clock = (kept ?? loose).Time;
            report.Add(clock is TimeOnly time ? "clock: " + time.ToString("HH:mm") : "clock: none");
        }

        if (report.Count == 1)
            report.Add("no invite-like lines");
        return string.Join('\n', report);
    }

    public static string? NewestLog(IEnumerable<string> logDirectories)
    {
        string? newest = null;
        var newestTime = DateTime.MinValue;
        foreach (var directory in logDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                continue;
            foreach (var file in Directory.GetFiles(directory, "*.log"))
            {
                DateTime written;
                try
                {
                    written = File.GetLastWriteTimeUtc(file);
                }
                catch (IOException)
                {
                    continue;
                }

                if (newest is not null && written <= newestTime)
                    continue;
                newest = file;
                newestTime = written;
            }
        }

        return newest;
    }

    private static DateTimeOffset Stamp(uint unix)
    {
        try
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(unix);
            if (when.UtcDateTime.Year is >= 2000 and <= 2100)
                return when;
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return DateTimeOffset.UnixEpoch;
    }
}
