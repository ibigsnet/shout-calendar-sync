using ShoutCalendar.Core;
using System.Security.Cryptography;

namespace ShoutCalendar.Sync;

public static class SyncProof
{
    private const string PrivateKeyPem = """
        -----BEGIN EC PRIVATE KEY-----
        MHcCAQEEIAL0bor+IFMcMXLvTJeGJZ3P+ZMZUfsqzEy73juQosMPoAoGCCqGSM49
        AwEHoUQDQgAE82dVlJ5TCocy1+n/1OV546eyJLmtGyR0sH7vnFoU58khXSv648GO
        DyNULICD7aWszBh4Hrb4AHhvbtFi1mFrDQ==
        -----END EC PRIVATE KEY-----
        """;

    public static byte[] Sign(ReadOnlySpan<byte> payload) =>
        P256.Sign(P256.PrivateScalar(PrivateKeyPem), payload);

    public static ECDsa CreatePlatformSigner()
    {
        var signer = ECDsa.Create();
        try
        {
            signer.ImportFromPem(PrivateKeyPem);
            return signer;
        }
        catch
        {
            signer.Dispose();
            throw;
        }
    }
}
