namespace ShoutCalendar.Core;

public readonly record struct DayInterval(TimeOnly Start, TimeOnly End, bool Range, bool Wraps);

public static class DayNest
{
    public static int[] Parents(IReadOnlyList<DayInterval> items)
    {
        var parents = new int[items.Count];
        Array.Fill(parents, -1);
        for (var child = 0; child < items.Count; child++)
        {
            var best = -1;
            var bestLength = TimeSpan.MaxValue;
            for (var parent = 0; parent < items.Count; parent++)
            {
                if (parent == child || !Holds(items[parent], items[child]))
                    continue;
                var length = Length(items[parent]);
                if (length >= bestLength)
                    continue;
                best = parent;
                bestLength = length;
            }

            parents[child] = best;
        }

        return parents;
    }

    private static bool Holds(DayInterval parent, DayInterval child)
    {
        if (!parent.Range)
            return false;
        if (child.Range && Length(child) >= Length(parent))
            return false;
        if (!Covers(parent, child.Start))
            return false;
        if (!child.Range)
            return true;
        if (parent.Wraps && child.Wraps)
            return child.End <= parent.End;
        if (parent.Wraps)
            return true;
        if (child.Wraps)
            return false;
        return child.End <= parent.End;
    }

    private static bool Covers(DayInterval parent, TimeOnly time)
    {
        if (parent.Wraps)
            return time >= parent.Start;
        return time >= parent.Start && time <= parent.End;
    }

    private static TimeSpan Length(DayInterval item)
    {
        if (!item.Range)
            return TimeSpan.Zero;
        if (!item.Wraps)
            return item.End - item.Start;
        return (TimeOnly.MaxValue - item.Start) + (item.End - TimeOnly.MinValue);
    }
}
