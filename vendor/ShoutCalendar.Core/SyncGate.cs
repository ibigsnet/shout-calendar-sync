using System.Text;

namespace ShoutCalendar.Core;

public static class SyncGate
{
    public const string ChallengeText = "shout-calendar-sync-attach";

    public static ReadOnlyMemory<byte> ChallengeBytes { get; } = Encoding.UTF8.GetBytes(ChallengeText);

    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE82dVlJ5TCocy1+n/1OV546eyJLmt
        GyR0sH7vnFoU58khXSv648GODyNULICD7aWszBh4Hrb4AHhvbtFi1mFrDQ==
        -----END PUBLIC KEY-----
        """;

    private static readonly object Gate = new();
    private static SyncBook? panel;
    private static byte[]? verifiedChallenge;

    public static bool IsAttached
    {
        get
        {
            lock (Gate)
                return panel is not null;
        }
    }

    public static SyncBook? Panel
    {
        get
        {
            lock (Gate)
                return panel;
        }
    }

    public static bool Verify(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
    {
        if (payload.IsEmpty || signature.IsEmpty)
            return false;
        try
        {
            return P256.Verify(P256.PublicPoint(PublicKeyPem), payload, signature);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    public static bool AllowRead(byte[]? signature)
    {
        if (signature is not { Length: 64 }) return false;
        lock (Gate)
            if (verifiedChallenge is not null && signature.AsSpan().SequenceEqual(verifiedChallenge)) return true;
        if (!Verify(ChallengeBytes.Span, signature)) return false;
        lock (Gate) verifiedChallenge = signature.ToArray();
        return true;
    }

    public static bool AllowWrite(byte[]? signature) => AllowRead(signature);

    public static bool TryAttach(byte[]? signature, SyncBook? book)
    {
        if (book is null || !AllowRead(signature) || !AllowWrite(signature))
            return false;
        lock (Gate)
        {
            panel = book;
            return true;
        }
    }

    public static void Detach()
    {
        lock (Gate)
            panel = null;
    }
}
