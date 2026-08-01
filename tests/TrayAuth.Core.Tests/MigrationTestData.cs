using System.Text;

namespace TrayAuth.Tests;

/// <summary>
/// Builds otpauth-migration payloads byte by byte, so the tests encode the protobuf with code
/// that shares nothing with the reader under test. Public because the Windows-only test project
/// reuses it for the bitmap QR round-trips.
/// </summary>
public static class MigrationTestData
{
    /// <summary>Decodes from base32 JBSWY3DPEHPK3PXP - "Hello!" followed by DE AD BE EF.</summary>
    public static readonly byte[] KnownSecret = [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x21, 0xDE, 0xAD, 0xBE, 0xEF];

    public static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
            {
                b |= 0x80;
            }

            bytes.Add(b);
        }
        while (value != 0);

        return [.. bytes];
    }

    public static byte[] Concat(params byte[][] parts)
    {
        var result = new List<byte>();
        foreach (byte[] part in parts)
        {
            result.AddRange(part);
        }

        return [.. result];
    }

    public static byte[] LenField(int field, byte[] payload) =>
        Concat(Varint((ulong)((field << 3) | 2)), Varint((ulong)payload.Length), payload);

    public static byte[] VarintField(int field, ulong value) =>
        Concat(Varint((ulong)(field << 3)), Varint(value));

    /// <summary>One OtpParameters submessage with Google's field numbering.</summary>
    public static byte[] OtpEntry(
        byte[] secret,
        string name,
        string issuer = "",
        ulong algorithm = 1,
        ulong digits = 1,
        ulong type = 2)
    {
        byte[] issuerField = issuer.Length > 0 ? LenField(3, Encoding.UTF8.GetBytes(issuer)) : [];

        return Concat(
            LenField(1, secret),
            LenField(2, Encoding.UTF8.GetBytes(name)),
            issuerField,
            VarintField(4, algorithm),
            VarintField(5, digits),
            VarintField(6, type));
    }

    /// <summary>Wraps OtpParameters entries into a MigrationPayload and then into the QR URI.</summary>
    public static string MigrationUri(byte[][] entries, int batchSize = 1, int batchIndex = 0)
    {
        var parts = new List<byte[]>();
        foreach (byte[] entry in entries)
        {
            parts.Add(LenField(1, entry));
        }

        parts.Add(VarintField(2, 1));                    // version
        parts.Add(VarintField(3, (ulong)batchSize));
        if (batchIndex != 0)
        {
            parts.Add(VarintField(4, (ulong)batchIndex));
        }

        byte[] payload = Concat([.. parts]);
        return "otpauth-migration://offline?data=" + Uri.EscapeDataString(Convert.ToBase64String(payload));
    }
}
