using System.Numerics;

namespace ShoutCalendar.Core;

public static class LayerShade
{
    public static Vector4 Lift(Vector4 color, int depth, float strength = 1f)
    {
        var scale = Math.Clamp(strength, 0f, 3f);
        var amount = Math.Clamp(depth * 0.16f * scale, 0f, 0.85f);
        return new Vector4(
            color.X + ((1f - color.X) * amount),
            color.Y + ((1f - color.Y) * amount),
            color.Z + ((1f - color.Z) * amount),
            color.W);
    }

    public static Vector4 Shadow(Vector4 color, float strength = 1f)
    {
        var scale = Math.Clamp(strength, 0f, 3f);
        var keep = Math.Clamp(1f - (0.28f * scale), 0.2f, 1f);
        return new Vector4(color.X * keep, color.Y * keep, color.Z * keep, color.W);
    }
}
