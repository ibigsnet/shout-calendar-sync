using System.Numerics;

namespace ShoutCalendar.Core;

public readonly record struct NightCurve(Vector2 From, Vector2 To, Vector2 C1, Vector2 C2)
{
    public static NightCurve Between(Vector2 from, Vector2 to)
    {
        var mid = new Vector2((from.X + to.X) * 0.5f, (from.Y + to.Y) * 0.5f);
        return new NightCurve(from, to, mid, mid);
    }
}
