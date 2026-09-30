using System.Numerics;

namespace ShoutCalendar.Core;

public enum CalendarLayout { Flat, Nested }
public enum OvernightStyle { Bars, Split, Straight, Curved }
public enum InviteListStyle { Tree, Summaries, Full }
public enum TextContrast { Auto, Light, Dark }
public enum LowHeightMode { Auto, On, Off }
public enum PendingScope { CurrentWorld, OpenCalendars, SyncedWorlds }

public sealed class CalendarAppearance
{
    public CalendarLayout Layout { get; set; }
    public OvernightStyle Overnight { get; set; }
    public InviteListStyle Lists { get; set; } = InviteListStyle.Tree;
    public float Shade { get; set; }
    public bool ShowServerSummary { get; set; } = true;
    public bool ShowDrawTiming { get; set; }
    public LowHeightMode LowHeight { get; set; }
    public Dictionary<string, TextContrast> Text { get; set; } = new(StringComparer.Ordinal);

    public void Normalize()
    {
        if (!Enum.IsDefined(this.Layout)) this.Layout = CalendarLayout.Flat;
        if (!Enum.IsDefined(this.Overnight)) this.Overnight = OvernightStyle.Bars;
        if (!Enum.IsDefined(this.Lists)) this.Lists = InviteListStyle.Tree;
        if (!Enum.IsDefined(this.LowHeight)) this.LowHeight = LowHeightMode.Auto;
        this.Shade = float.IsFinite(this.Shade) ? Math.Clamp(this.Shade, 0f, 1f) : 0f;
        this.Text ??= new(StringComparer.Ordinal);
    }

    public bool Compact(float displayHeight) => this.LowHeight == LowHeightMode.On
        || (this.LowHeight == LowHeightMode.Auto && displayHeight > 0 && displayHeight <= 768);

    public static CalendarAppearance Migrate(float? shade, IEnumerable<string>? flipped)
    {
        var appearance = new CalendarAppearance { Shade = Math.Clamp((shade ?? 0f) / 3f, 0f, 1f) };
        foreach (var key in flipped ?? [])
            appearance.Text[key] = key is "Pending" or "Cactpot" ? TextContrast.Light : TextContrast.Dark;
        appearance.Normalize();
        return appearance;
    }

    public void Preset(string name)
    {
        this.Layout = name == "Layered" ? CalendarLayout.Nested : CalendarLayout.Flat;
        this.Overnight = name == "Minimal" ? OvernightStyle.Split : OvernightStyle.Bars;
        this.Shade = name == "Soft" ? 0.25f : name == "Layered" ? 0.15f : 0f;
        this.Lists = InviteListStyle.Tree;
    }

    public Vector4 Ink(string key, Vector4 background)
    {
        var mode = this.Text.GetValueOrDefault(key);
        if (mode == TextContrast.Light) return new(0.97f, 0.97f, 0.97f, 1f);
        if (mode == TextContrast.Dark) return new(0.06f, 0.06f, 0.07f, 1f);
        static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        var a = Math.Clamp(background.W, 0, 1);
        var rgb = new Vector3(background.X, background.Y, background.Z) * a + new Vector3(0.10f) * (1 - a);
        var luminance = 0.2126 * Linear(rgb.X) + 0.7152 * Linear(rgb.Y) + 0.0722 * Linear(rgb.Z);
        return luminance > 0.179 ? new(0.06f, 0.06f, 0.07f, 1f) : new(0.97f, 0.97f, 0.97f, 1f);
    }
}
