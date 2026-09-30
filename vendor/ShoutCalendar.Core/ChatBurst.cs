namespace ShoutCalendar.Core;

public sealed class ChatBurst
{
    public const int WindowSeconds = 60;

    public const int MaxLines = 4;

    private readonly Dictionary<string, Open> open = new(StringComparer.OrdinalIgnoreCase);

    private string lastWho = "";

    public string Push(string? sender, int channel, DateTimeOffset when, string? text, bool attachToKept, out string? replaceId)
    {
        var who = SenderName.Clean(sender);
        var line = Collapse(text);
        this.Expire(when);
        if (who.Length == 0)
        {
            replaceId = null;
            return line;
        }

        var continues = this.open.TryGetValue(who, out var existing)
            && Similar(existing.Channel, channel)
            && when >= existing.LastAt
            && when - existing.LastAt <= TimeSpan.FromSeconds(WindowSeconds)
            && existing.Lines.Count < MaxLines
            && (attachToKept || existing.KeptId is null);
        Open burst;
        if (!continues || existing is null)
        {
            burst = new Open { Channel = channel };
            this.open[who] = burst;
        }
        else
        {
            burst = existing;
        }

        if (line.Length > 0)
            burst.Lines.Add(line);
        burst.LastAt = when;
        this.lastWho = who;
        replaceId = continues ? burst.KeptId : null;
        return string.Join(' ', burst.Lines);
    }

    public void Remember(string? id)
    {
        if (this.lastWho.Length == 0 || !this.open.TryGetValue(this.lastWho, out var burst))
            return;
        burst.KeptId = id;
    }

    private void Expire(DateTimeOffset when)
    {
        if (this.open.Count == 0)
            return;
        List<string>? drop = null;
        foreach (var pair in this.open)
        {
            if (when - pair.Value.LastAt > TimeSpan.FromSeconds(WindowSeconds))
                (drop ??= new List<string>()).Add(pair.Key);
        }

        if (drop is null)
            return;
        foreach (var key in drop)
            this.open.Remove(key);
    }

    private sealed class Open
    {
        public List<string> Lines { get; } = new();

        public int Channel { get; set; }

        public DateTimeOffset LastAt { get; set; }

        public string? KeptId { get; set; }
    }

    private static bool Similar(int left, int right)
    {
        if (left == right)
            return true;
        return SharePolicy.IsShareable(left) && SharePolicy.IsShareable(right);
    }

    private static string Collapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
