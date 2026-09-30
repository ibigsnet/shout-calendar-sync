using System.Globalization;

namespace ShoutCalendar.Core;

public static class SyncClock
{
    public static void Stamp(SyncAnnouncement item, CalendarEntry entry, TimeZoneInfo calendarZone)
    {
        var day = entry.Date;
        var time = entry.Time;
        var end = entry.End;
        var source = entry.SourceTimeZone;
        if (entry.Repeat is null && entry.StartUtc is DateTimeOffset instant && source.Length > 0)
        {
            try
            {
                var origin = TimeZoneInfo.FindSystemTimeZoneById(source);
                var civil = TimeZoneInfo.ConvertTime(instant, origin);
                day = DateOnly.FromDateTime(civil.DateTime);
                time = TimeOnly.FromDateTime(civil.DateTime);
                if (entry.EndUtc is DateTimeOffset finish) end = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(finish, origin).DateTime);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        var walls = ZoneClock.Walls(entry.EventText);
        if (day is DateOnly date && time is not null && source.Length == 0 && !entry.NoteUpdated
            && !ZoneClock.TryNowUntil(entry.EventText, out _) && walls.Count > 0 && ZoneClock.SourceId(walls[0].Label) is string zone)
        {
            day = ZoneClock.CivilDate(date, walls[0], time, calendarZone);
            time = walls[0].Time;
            if (walls.Count > 1) end = walls[1].Time;
            source = zone;
        }
        else if (time is not null && source.Length == 0 && (entry.NoteUpdated || ZoneClock.TryNowUntil(entry.EventText, out _) || LiveInvite.IsGathering(entry.EventText)))
            source = calendarZone.Id;
        item.Date = day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        item.Time = time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
        item.End = end?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
        item.SourceTimeZone = source;
        item.StartUtc = entry.StartUtc ?? Instant(day, time, source);
        item.EndUtc = entry.EndUtc ?? Instant(EndDate(day, time, end), end, source);
    }

    public static DateOnly? EndDate(DateOnly? day, TimeOnly? start, TimeOnly? end) =>
        day is DateOnly date && start is TimeOnly begin && end is TimeOnly finish && finish <= begin
            ? date == DateOnly.MaxValue ? null : date.AddDays(1)
            : day;

    public static DateTimeOffset? Instant(DateOnly? date, TimeOnly? time, string? zone)
    {
        if (date is not DateOnly day || time is not TimeOnly clock || string.IsNullOrEmpty(zone)) return null;
        try
        {
            var source = TimeZoneInfo.FindSystemTimeZoneById(zone);
            var civil = DateTime.SpecifyKind(day.ToDateTime(clock), DateTimeKind.Unspecified);
            if (source.IsInvalidTime(civil)) return null;
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(civil, source));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return null; }
    }

    public static CalendarEntry Entry(SyncAnnouncement item, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        DateOnly? day = DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        TimeOnly? time = TimeOnly.TryParseExact(item.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
        TimeOnly? end = TimeOnly.TryParseExact(item.End, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e) ? e : null;
        var repeat = EventRepeat.Parse(item.Repeat);
        if (item.ShareFormat < 2)
        {
            var heard = item.ObservedAt != default ? item.ObservedAt
                : day is DateOnly savedDay ? new DateTimeOffset(savedDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : DateTimeOffset.Now;
            var parsed = ShoutHarvest.TryHarvest(item.Text, item.Channel, heard, aggressive: false, zone: zone);
            if (parsed is not null && ZoneClock.Labeled(item.Text))
            {
                day = parsed.Date ?? day;
                time = parsed.Time ?? time;
                end = parsed.End ?? end;
            }
            repeat ??= parsed?.Repeat;
        }
        var walls = ZoneClock.Walls(item.Text);
        if (!item.NoteUpdated)
        {
            time ??= walls.Count > 0 ? walls[0].Time : null;
            end ??= walls.Count > 1 ? walls[1].Time : null;
        }
        var result = new CalendarEntry(day, time, end, null, item.World, item.Place, item.Text, "", item.Accepted, item.Id,
            item.ObservedAt, repeat, item.Channel, item.NoteUpdated, SourceTimeZone: item.SourceTimeZone,
            StartUtc: item.StartUtc, EndUtc: item.EndUtc, Revision: item.Revision,
            ExcludedDates: item.ExcludedDates.ToArray(), RepeatUntil: item.RepeatUntil, SeriesDeleted: item.SeriesDeleted);
        if (repeat is null && item.StartUtc is DateTimeOffset start)
        {
            var local = TimeZoneInfo.ConvertTime(start, zone);
            result = result with { Date = DateOnly.FromDateTime(local.DateTime), Time = TimeOnly.FromDateTime(local.DateTime),
                End = item.EndUtc is DateTimeOffset finish ? TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(finish, zone).DateTime) : end };
        }
        return result;
    }

    public static ZoneClock.Range Range(CalendarEntry entry, TimeZoneInfo zone)
    {
        var start = entry.Repeat is null ? entry.StartUtc ?? Instant(entry.Date, entry.Time, entry.SourceTimeZone)
            : Instant(entry.Date, entry.Time, entry.SourceTimeZone);
        var end = entry.Repeat is null ? entry.EndUtc ?? Instant(EndDate(entry.Date, entry.Time, entry.End), entry.End, entry.SourceTimeZone)
            : Instant(EndDate(entry.Date, entry.Time, entry.End), entry.End, entry.SourceTimeZone);
        if (start is not DateTimeOffset instant)
            return new ZoneClock.Range(entry.Date ?? DateOnly.FromDateTime(DateTime.Today), entry.Time, entry.End);
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return new ZoneClock.Range(DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime),
            end is DateTimeOffset finish ? TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(finish, zone).DateTime) : entry.End);
    }

    public static CalendarEntry? OnDate(CalendarEntry entry, DateOnly localDay, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        if (entry.SeriesDeleted || (entry.Date is null && entry.StartUtc is null)) return null;
        if (entry.Repeat is null) return ZoneClock.Shown(entry, zone).Date == localDay ? entry : null;
        if (entry.Date is not DateOnly anchor) return null;
        foreach (var shift in new[] { 0, -1, 1 })
        {
            if (localDay.DayNumber + shift < DateOnly.MinValue.DayNumber || localDay.DayNumber + shift > DateOnly.MaxValue.DayNumber) continue;
            var civil = localDay.AddDays(shift);
            if (!EventRepeat.FallsOn(entry, civil)) continue;
            var occurrence = entry with { Date = civil, StartUtc = null, EndUtc = null };
            if (ZoneClock.Shown(occurrence, zone).Date == localDay) return occurrence;
        }
        return null;
    }
}
