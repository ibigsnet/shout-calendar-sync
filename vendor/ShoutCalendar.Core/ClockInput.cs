using System.Globalization;
using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

public readonly record struct ClockInputErrors(string? Date = null, string? Time = null, string? Message = null)
{
    public bool Any => !string.IsNullOrEmpty(this.Date) || !string.IsNullOrEmpty(this.Time) || !string.IsNullOrEmpty(this.Message);
}

public readonly record struct ClockInput(DateOnly? Date, TimeOnly? Start, TimeOnly? End)
{
    private const string Clock = @"(?:(?:[01]?\d|2[0-3]):[0-5]\d|(?:0?[1-9]|1[0-2])(?::[0-5]\d)?\s*[ap](?:\.?m\.?)?)";
    private static readonly Regex Typed = new(@"\A\s*(?<start>" + Clock + @")(?:\s*(?:[-–—]|to)\s*(?<end>" + Clock + @"))?\s*\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string DateText => this.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    public string TimeText => this.Start is not TimeOnly start ? "" : this.End is TimeOnly end
        ? $"{start:HH:mm}-{end:HH:mm}" : start.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static ClockInput FromEntry(CalendarEntry entry, TimeZoneInfo? zone = null)
    {
        var shown = ZoneClock.ShownRange(entry, zone ?? TimeZoneInfo.Local);
        return new ClockInput(entry.Date is null && entry.StartUtc is null ? null : shown.Date, shown.Start, shown.End);
    }

    public static bool TryParse(string? dateText, string? timeText, out ClockInput value, out ClockInputErrors errors)
    {
        DateOnly? day = null;
        TimeOnly? start = null, end = null;
        string? dateError = null, timeError = null;
        if (!string.IsNullOrWhiteSpace(dateText))
        {
            if (DateOnly.TryParseExact(dateText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) day = date;
            else dateError = "Enter a valid date as yyyy-MM-dd, or leave it empty.";
        }
        if (!string.IsNullOrWhiteSpace(timeText))
        {
            var match = Typed.Match(timeText);
            if (!match.Success || !ZoneClock.TryTyped(match.Groups["start"].Value, out var first, out _))
                timeError = "Use 21:00 or 9pm; add an end such as 21:00-23:00, or leave it empty.";
            else
            {
                start = first;
                if (match.Groups["end"].Success)
                {
                    if (ZoneClock.TryTyped(match.Groups["end"].Value, out var last, out _)) end = last;
                    else timeError = "Enter a valid end time.";
                }
            }
        }
        value = new ClockInput(day, start, end);
        errors = new ClockInputErrors(dateError, timeError);
        return !errors.Any;
    }

    public bool ValidateZone(TimeZoneInfo zone, out ClockInputErrors errors)
    {
        errors = default;
        if (this.Date is not DateOnly day || this.Start is not TimeOnly start) return true;
        var endDay = SyncClock.EndDate(day, start, this.End);
        if (this.End is not null && endDay is null)
            errors = new ClockInputErrors(Date: "The end extends beyond the supported date range.");
        else if (SyncClock.Instant(day, start, zone.Id) is null
            || (this.End is not null && SyncClock.Instant(endDay, this.End, zone.Id) is null))
            errors = new ClockInputErrors(Time: "That time is skipped by a daylight-saving change. Choose another time.");
        return !errors.Any;
    }
}
