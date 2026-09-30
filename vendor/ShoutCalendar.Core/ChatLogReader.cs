using System.Buffers.Binary;
using System.Text;

namespace ShoutCalendar.Core;

public sealed record ChatLogLine(uint TimestampUnix, byte Filter, byte Channel, string Sender, string Message);

public static class ChatLogReader
{
    public static IReadOnlyList<ChatLogLine> Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 8)
            return Array.Empty<ChatLogLine>();

        var begin = BinaryPrimitives.ReadUInt32LittleEndian(file);
        var end = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(4));
        if (end < begin)
            return Array.Empty<ChatLogLine>();

        var countLong = (long)end - begin;
        if (countLong > 100_000)
            return Array.Empty<ChatLogLine>();

        var count = (int)countLong;
        if (8L + (count * 4L) > file.Length)
            return Array.Empty<ChatLogLine>();

        var body = 8 + (count * 4);
        var lines = new List<ChatLogLine>(count);
        var start = 0;
        for (var i = 0; i < count; i++)
        {
            var relEnd = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(8 + (i * 4)));
            if (relEnd > int.MaxValue || (int)relEnd < start || body + relEnd > file.Length)
            {
                start = relEnd > int.MaxValue ? start : (int)relEnd;
                continue;
            }

            var entry = file.Slice(body + start, (int)relEnd - start);
            if (TryParseEntry(entry, out var line))
                lines.Add(line);
            start = (int)relEnd;
        }

        return lines;
    }

    private static bool TryParseEntry(ReadOnlySpan<byte> entry, out ChatLogLine line)
    {
        line = null!;
        if (entry.Length < 9 || entry[8] != 0x1F)
            return false;
        if (!TryFindSeparator(entry, 9, out var separator))
            return false;

        var timestamp = BinaryPrimitives.ReadUInt32LittleEndian(entry);
        var sender = PlainText(entry.Slice(9, separator - 9));
        var message = PlainText(entry.Slice(separator + 1));
        var channel = entry[5] != 0 ? entry[5] : entry[4];
        line = new ChatLogLine(timestamp, entry[4], channel, sender, message);
        return true;
    }

    private static bool TryFindSeparator(ReadOnlySpan<byte> entry, int from, out int index)
    {
        var i = from;
        while (i < entry.Length)
        {
            if (entry[i] == 0x02)
            {
                if (!TryTakePayload(entry, ref i, out _, out _))
                {
                    index = -1;
                    return false;
                }

                continue;
            }

            if (entry[i] == 0x1F)
            {
                index = i;
                return true;
            }

            i++;
        }

        index = -1;
        return false;
    }

    private static string PlainText(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder();
        var i = 0;
        var start = 0;
        while (i < bytes.Length)
        {
            if (bytes[i] == 0x00)
                break;
            if (bytes[i] == 0x02)
            {
                if (i > start)
                    text.Append(Encoding.UTF8.GetString(bytes.Slice(start, i - start)));
                if (!TryTakePayload(bytes, ref i, out var type, out var payload))
                    break;
                if (type == 0x27)
                    AppendVisible(text, payload);
                start = i;
                continue;
            }

            i++;
        }

        if (i > start)
            text.Append(Encoding.UTF8.GetString(bytes.Slice(start, i - start)));
        return text.ToString();
    }

    private static bool TryTakePayload(ReadOnlySpan<byte> bytes, ref int index, out byte type, out ReadOnlySpan<byte> payload)
    {
        type = 0;
        payload = default;
        if (index + 2 >= bytes.Length)
            return false;
        var payloadLength = unchecked((sbyte)bytes[index + 2]);
        if (payloadLength < 0)
            return false;
        var advance = 3 + payloadLength;
        if (index + advance > bytes.Length)
            return false;
        type = bytes[index + 1];
        var dataLength = Math.Max(0, payloadLength - 1);
        payload = bytes.Slice(index + 3, dataLength);
        index += advance;
        return true;
    }

    private static void AppendVisible(StringBuilder text, ReadOnlySpan<byte> payload)
    {
        var i = 0;
        while (i < payload.Length)
        {
            if (payload[i] < 0x20)
            {
                i++;
                continue;
            }

            var start = i;
            while (i < payload.Length && payload[i] >= 0x20)
                i++;
            text.Append(Encoding.UTF8.GetString(payload.Slice(start, i - start)));
        }
    }
}
