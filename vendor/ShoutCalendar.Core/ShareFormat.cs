namespace ShoutCalendar.Core;

public static class ShareFormat
{
    public const int Legacy = 0;

    public const int Current = 2;

    public const int Minimum = 0;

    public static bool Accepts(int format) => format >= Minimum && format <= Current;
}
