namespace ShoutCalendar.Core;

public static class TravelNear
{
    public const float Leeway = 1.5f;

    public static float Distance(float x1, float y1, float x2, float y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    public static bool Skip(bool sameTerritory, float playerToTarget, float aetheryteToTarget)
    {
        if (!sameTerritory)
            return false;
        if (float.IsPositiveInfinity(aetheryteToTarget) || aetheryteToTarget <= 0.05f)
            return true;
        return playerToTarget < aetheryteToTarget * Leeway;
    }
}
