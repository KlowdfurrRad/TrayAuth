namespace TrayAuth.Core;

/// <summary>
/// RFC 4648 base32, deliberately forgiving on input. Sites print setup keys in wildly different
/// shapes — lowercase, space-separated groups of four, hyphenated, with or without '=' padding —
/// and every one of those should decode to the same key rather than produce "invalid secret".
/// </summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Strips formatting noise and upper-cases, leaving only candidate base32 characters.</summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (char.IsWhiteSpace(c) || c == '-' || c == '_' || c == '=')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    public static bool TryDecode(string? input, out byte[] result)
    {
        result = [];

        string normalized = Normalize(input);
        if (normalized.Length == 0)
        {
            return false;
        }

        var output = new List<byte>((normalized.Length * 5 / 8) + 1);
        int buffer = 0;
        int bitsBuffered = 0;

        foreach (char c in normalized)
        {
            int value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                return false;
            }

            buffer = (buffer << 5) | value;
            bitsBuffered += 5;

            if (bitsBuffered >= 8)
            {
                bitsBuffered -= 8;
                output.Add((byte)((buffer >> bitsBuffered) & 0xFF));
            }
        }

        // Any leftover bits are padding. We ignore them rather than rejecting the key: some
        // providers emit keys whose final group has non-zero trailing bits, and every mainstream
        // authenticator accepts those.
        if (output.Count == 0)
        {
            return false;
        }

        result = [.. output];
        return true;
    }

    public static byte[] Decode(string input)
    {
        if (!TryDecode(input, out byte[] result))
        {
            throw new FormatException("The value is not a valid base32 secret.");
        }

        return result;
    }

    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(((data.Length + 4) / 5) * 8);
        int buffer = 0;
        int bitsBuffered = 0;

        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bitsBuffered += 8;

            while (bitsBuffered >= 5)
            {
                bitsBuffered -= 5;
                builder.Append(Alphabet[(buffer >> bitsBuffered) & 0x1F]);
            }
        }

        if (bitsBuffered > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bitsBuffered)) & 0x1F]);
        }

        return builder.ToString();
    }
}
