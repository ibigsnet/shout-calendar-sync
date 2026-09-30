using System.Numerics;

namespace ShoutCalendar.Core;

public static class PastTone
{
    public static bool Ended(DateOnly? day, TimeOnly? start, TimeOnly? end, DateTime now)
    {
        if (day is not DateOnly date)
            return false;
        if (start is not TimeOnly begin)
            return now > date.ToDateTime(new TimeOnly(23, 59));
        var finish = date.ToDateTime(end ?? begin);
        if (end is TimeOnly stop && stop <= begin)
            finish = date.AddDays(1).ToDateTime(stop);
        return now > finish;
    }

    public static Vector4 Grey(Vector4 color)
    {
        var luma = (color.X * 0.299f) + (color.Y * 0.587f) + (color.Z * 0.114f);
        const float keep = 0.35f;
        return new Vector4(
            (color.X * keep) + (luma * (1f - keep)),
            (color.Y * keep) + (luma * (1f - keep)),
            (color.Z * keep) + (luma * (1f - keep)),
            color.W);
    }
}
