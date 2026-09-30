using System.Globalization;

namespace ShoutCalendar.Core;

public readonly record struct SyncPassSample(
    int RelayRows,
    int RelayBytes,
    int Released,
    int Added,
    int UploadBytes,
    TimeSpan Elapsed,
    SyncFillReport Report);

public static class SyncPerf
{
    public const int Keep = 40;

    public static string Pace(SyncPassSample sample)
    {
        if (sample.Report.DroppedCurrent > 0)
            return "catching up";
        if (sample.Report.DeferredPast > 0)
            return "catching up";
        if (sample.Elapsed >= SyncFill.SlowPass)
            return "slow";
        if (sample.Added == 0 && sample.UploadBytes == 0)
            return "idle";
        return "keeping up";
    }

    public static string Line(DateTimeOffset at, SyncPassSample sample, SyncLimits limits, int storedBytes)
    {
        var cap = limits.Clamp();
        var ms = Math.Max(0, (int)sample.Elapsed.TotalMilliseconds);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{at.LocalDateTime:HH:mm:ss}  {Pace(sample)}  {ms} ms  catalog {sample.RelayRows} invites  downloaded {Kb(sample.RelayBytes)} KB  applied {sample.Added}  waiting {sample.Report.DroppedCurrent + sample.Report.DeferredPast} ({sample.Report.DroppedCurrent} current, {sample.Report.DeferredPast} past)  uploaded {Kb(sample.UploadBytes)} KB  upload cap {Kb(cap.UploadBytesPerSecond)} KB/s  download cap {Kb(cap.BytesPerSecond)} KB/s  invites/pass {cap.MaxItemsPerTick}  stored {Kb(storedBytes)}/{Kb(cap.MaxStoredBytes)} KB");
    }

    private static int Kb(int bytes) => Math.Max(0, bytes) / 1000;
}
