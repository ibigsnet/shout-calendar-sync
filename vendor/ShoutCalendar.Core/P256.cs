using System.Numerics;
using System.Security.Cryptography;

namespace ShoutCalendar.Core;

public static class P256
{
    private static readonly BigInteger Prime = Hex("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");
    private static readonly BigInteger A = Prime - 3;
    private static readonly BigInteger Order = Hex("FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551");
    private static readonly Point Generator = new(
        Hex("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296"),
        Hex("4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5"),
        false);

    public static byte[] Sign(ReadOnlySpan<byte> privateKey, ReadOnlySpan<byte> payload)
    {
        var d = U(privateKey);
        var z = Hash(payload);
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var k = RandomScalar();
            var point = Multiply(Generator, k);
            var r = Mod(point.X, Order);
            if (r.IsZero)
                continue;
            var s = Mod(Inv(k, Order) * Mod(z + Mod(r * d, Order), Order), Order);
            if (s.IsZero)
                continue;
            var signature = new byte[64];
            Fixed(r).CopyTo(signature, 0);
            Fixed(s).CopyTo(signature, 32);
            return signature;
        }

        throw new CryptographicException("Could not sign.");
    }

    public static bool Verify(ReadOnlySpan<byte> publicPoint, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != 64 || publicPoint.Length < 64)
            return false;
        var r = U(signature[..32]);
        var s = U(signature[32..]);
        if (r.Sign <= 0 || s.Sign <= 0 || r >= Order || s >= Order)
            return false;
        var offset = publicPoint.Length == 65 && publicPoint[0] == 0x04 ? 1 : 0;
        if (publicPoint.Length - offset < 64)
            return false;
        var key = new Point(U(publicPoint.Slice(offset, 32)), U(publicPoint.Slice(offset + 32, 32)), false);
        var w = Inv(s, Order);
        var point = Add(Multiply(Generator, Mod(Hash(payload) * w, Order)), Multiply(key, Mod(r * w, Order)));
        return !point.Infinity && Mod(point.X, Order) == r;
    }

    public static byte[] PrivateScalar(string pem)
    {
        var der = Der(pem);
        for (var i = 0; i + 34 <= der.Length; i++)
        {
            if (der[i] == 0x04 && der[i + 1] == 0x20)
                return der[(i + 2)..(i + 34)];
        }

        throw new CryptographicException("Key material is missing.");
    }

    public static byte[] PublicPoint(string pem)
    {
        var der = Der(pem);
        for (var i = 0; i + 66 <= der.Length; i++)
        {
            if (der[i] == 0x00 && der[i + 1] == 0x04)
                return der[(i + 1)..(i + 66)];
        }

        throw new CryptographicException("Key material is missing.");
    }

    private static byte[] Der(string pem)
    {
        var body = string.Concat(pem.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("-----", StringComparison.Ordinal)));
        return Convert.FromBase64String(body);
    }

    private static BigInteger Hash(ReadOnlySpan<byte> payload) => Mod(U(SHA256.HashData(payload)), Order);

    private static BigInteger RandomScalar()
    {
        Span<byte> bytes = stackalloc byte[32];
        while (true)
        {
            RandomNumberGenerator.Fill(bytes);
            var value = U(bytes);
            if (value.Sign > 0 && value < Order)
                return value;
        }
    }

    private readonly record struct Point(BigInteger X, BigInteger Y, bool Infinity);

    private static Point Add(Point left, Point right)
    {
        if (left.Infinity)
            return right;
        if (right.Infinity)
            return left;
        if (left.X == right.X)
            return Mod(left.Y + right.Y, Prime).IsZero ? new Point(0, 0, true) : Double(left);
        var slope = Mod((right.Y - left.Y) * Inv(right.X - left.X, Prime), Prime);
        var x = Mod(slope * slope - left.X - right.X, Prime);
        var y = Mod(slope * (left.X - x) - left.Y, Prime);
        return new Point(x, y, false);
    }

    private static Point Double(Point point)
    {
        if (point.Infinity || point.Y.IsZero)
            return new Point(0, 0, true);
        var slope = Mod((3 * point.X * point.X + A) * Inv(point.Y << 1, Prime), Prime);
        var x = Mod(slope * slope - (point.X << 1), Prime);
        var y = Mod(slope * (point.X - x) - point.Y, Prime);
        return new Point(x, y, false);
    }

    private static Point Multiply(Point point, BigInteger scalar)
    {
        var result = new Point(0, 0, true);
        var length = (int)scalar.GetBitLength();
        for (var bit = length - 1; bit >= 0; bit--)
        {
            result = Double(result);
            if (((scalar >> bit) & 1) == 1)
                result = Add(result, point);
        }

        return result;
    }

    private static BigInteger Inv(BigInteger value, BigInteger modulus) =>
        BigInteger.ModPow(Mod(value, modulus), modulus - 2, modulus);

    private static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        var remainder = value % modulus;
        return remainder.Sign < 0 ? remainder + modulus : remainder;
    }

    private static BigInteger Hex(string hex) => U(Convert.FromHexString(hex));

    private static BigInteger U(ReadOnlySpan<byte> bigEndian)
    {
        var little = new byte[bigEndian.Length + 1];
        for (var i = 0; i < bigEndian.Length; i++)
            little[bigEndian.Length - 1 - i] = bigEndian[i];
        return new BigInteger(little);
    }

    private static byte[] Fixed(BigInteger value)
    {
        var raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length == 32)
            return raw;
        var padded = new byte[32];
        raw.CopyTo(padded.AsSpan(32 - raw.Length));
        return padded;
    }
}
