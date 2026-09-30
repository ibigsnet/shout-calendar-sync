using System.Globalization;

namespace ShoutCalendar.Core;

public static class LocalCopy
{
    public static CalendarEntry From(SyncAnnouncement item, PlaceCatalog? places, DateTimeOffset now)
    {
        var channel = item.Channel is SharePolicy.YellChannel or ShoutHarvest.ShoutChannel
            ? item.Channel
            : ShoutHarvest.ShoutChannel;
        var parsed = ShoutHarvest.TryHarvest(item.Text, channel, now, places, aggressive: false);
        var clock = SyncClock.Entry(item);
        var server = string.IsNullOrWhiteSpace(parsed?.Server) ? item.World : parsed!.Server;
        return new CalendarEntry(
            clock.Date,
            clock.Time,
            clock.End ?? parsed?.End,
            parsed?.Ward,
            server,
            item.Place.Length > 0 ? item.Place : parsed?.Place ?? "",
            item.Text ?? "",
            "",
            true,
            "local-" + (string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id),
            now,
            clock.Repeat ?? parsed?.Repeat,
            0,
            false,
            true,
            item.World ?? "", StartUtc: clock.StartUtc, EndUtc: clock.EndUtc, SourceTimeZone: clock.SourceTimeZone, ExcludedDates: clock.ExcludedDates, RepeatUntil: clock.RepeatUntil, SeriesDeleted: clock.SeriesDeleted);
    }
}
