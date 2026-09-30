namespace ShoutCalendar.Core;

public readonly record struct RelayLoad(
    int StoredBytes,
    int StoredLimit,
    int DropsRecent,
    int SlowRecent,
    int Peers,
    int PeersUp,
    int Clients);

public static class RelayHealth
{
    public const int FullPercent = 85;

    public static bool IsDegraded(RelayLoad load)
    {
        if (load.StoredLimit > 0 && (long)load.StoredBytes * 100 >= (long)load.StoredLimit * FullPercent)
            return true;
        if (load.DropsRecent > 0 || load.SlowRecent >= 3)
            return true;
        if (load.Peers > 0 && load.PeersUp == 0)
            return true;
        return false;
    }
}
