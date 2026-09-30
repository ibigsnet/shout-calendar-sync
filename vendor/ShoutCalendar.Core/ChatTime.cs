namespace ShoutCalendar.Core;

public static class ChatTime
{
    public static DateTimeOffset FromUnixOrNow(long timestamp, DateTimeOffset now)
    {
        try
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(timestamp);
            if (when.UtcDateTime.Year is >= 2000 and <= 2100)
                return when;
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return now;
    }
}
