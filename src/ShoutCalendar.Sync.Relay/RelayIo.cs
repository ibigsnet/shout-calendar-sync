using System.Net.Sockets;
using System.Text;
using ShoutCalendar.Core;

namespace ShoutCalendar.Sync;

static class RelayIo
{
    public const int MaxBody = 262144;

    public static void Send(NetworkStream stream, byte[] bytes)
    {
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    public static void SendLine(NetworkStream stream, string line)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\n");
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    public static string ReadLine(NetworkStream stream)
    {
        var bytes = new List<byte>();
        while (bytes.Count <= 8192)
        {
            var next = stream.ReadByte();
            if (next < 0)
                throw new EndOfStreamException();
            if (next == '\n') return Encoding.ASCII.GetString(bytes.ToArray());
            if (next != '\r')
                bytes.Add((byte)next);
        }

        throw new InvalidDataException("Relay header is too long.");
    }

    public static byte[] ReadExact(NetworkStream stream, int length)
    {
        if (length < 0 || length > MaxBody)
            throw new InvalidDataException();
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = stream.Read(buffer, offset, length - offset);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }

        return buffer;
    }

    public static bool TrySignature(string line, out byte[] signature)
    {
        try
        {
            signature = Convert.FromBase64String(line);
            return signature.Length > 0;
        }
        catch (FormatException)
        {
            signature = [];
            return false;
        }
    }
}
