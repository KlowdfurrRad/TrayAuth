using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace TrayAuth.Core;

public enum OtpAlgorithm
{
    Sha1,
    Sha256,
    Sha512,
}

/// <summary>A generated code plus the state needed to render its countdown.</summary>
public readonly record struct TotpCode(string Code, int SecondsRemaining, long Counter, int Period)
{
    /// <summary>Fraction of the current step still remaining, in [0, 1].</summary>
    public double Fraction => Period <= 0 ? 0d : (double)SecondsRemaining / Period;

    /// <summary>The code split into readable groups: "482913" renders as "482 913".</summary>
    public string Grouped => Format(Code);

    public static string Format(string code) => code.Length switch
    {
        6 => string.Concat(code.AsSpan(0, 3), " ", code.AsSpan(3, 3)),
        8 => string.Concat(code.AsSpan(0, 4), " ", code.AsSpan(4, 4)),
        _ => code,
    };
}

/// <summary>HOTP (RFC 4226) and TOTP (RFC 6238).</summary>
public static class Totp
{
    public const int DefaultDigits = 6;
    public const int DefaultPeriod = 30;

    private static readonly int[] Pow10 = [1, 10, 100, 1_000, 10_000, 100_000, 1_000_000, 10_000_000, 100_000_000];

    public static TotpCode Generate(
        string secret,
        int digits = DefaultDigits,
        int period = DefaultPeriod,
        OtpAlgorithm algorithm = OtpAlgorithm.Sha1,
        DateTimeOffset? at = null)
    {
        byte[] key = Base32.Decode(secret);
        return Generate(key, digits, period, algorithm, at);
    }

    public static TotpCode Generate(
        byte[] key,
        int digits = DefaultDigits,
        int period = DefaultPeriod,
        OtpAlgorithm algorithm = OtpAlgorithm.Sha1,
        DateTimeOffset? at = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), period, "Period must be positive.");
        }

        long unixSeconds = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();

        // Floor division so that pre-1970 timestamps (only reachable in tests) still step correctly.
        long counter = (long)Math.Floor(unixSeconds / (double)period);
        int elapsed = (int)(unixSeconds - (counter * period));
        int remaining = period - elapsed;

        string code = ComputeHotp(key, counter, digits, algorithm);
        return new TotpCode(code, remaining, counter, period);
    }

    public static string ComputeHotp(byte[] key, long counter, int digits = DefaultDigits, OtpAlgorithm algorithm = OtpAlgorithm.Sha1)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (digits is < 6 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(digits), digits, "Digits must be between 6 and 8.");
        }

        byte[] message = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(message, counter);

        using HMAC hmac = CreateHmac(algorithm, key);
        byte[] hash = hmac.ComputeHash(message);

        // RFC 4226 dynamic truncation.
        int offset = hash[^1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        int value = binary % Pow10[digits];
        return value.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    private static HMAC CreateHmac(OtpAlgorithm algorithm, byte[] key) => algorithm switch
    {
        OtpAlgorithm.Sha1 => new HMACSHA1(key),
        OtpAlgorithm.Sha256 => new HMACSHA256(key),
        OtpAlgorithm.Sha512 => new HMACSHA512(key),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported algorithm."),
    };

    public static string ToName(OtpAlgorithm algorithm) => algorithm switch
    {
        OtpAlgorithm.Sha256 => "SHA256",
        OtpAlgorithm.Sha512 => "SHA512",
        _ => "SHA1",
    };

    public static bool TryParseAlgorithm(string? name, out OtpAlgorithm algorithm)
    {
        switch (Base32.Normalize(name))
        {
            case "SHA1":
                algorithm = OtpAlgorithm.Sha1;
                return true;
            case "SHA256":
                algorithm = OtpAlgorithm.Sha256;
                return true;
            case "SHA512":
                algorithm = OtpAlgorithm.Sha512;
                return true;
            default:
                algorithm = OtpAlgorithm.Sha1;
                return false;
        }
    }

    public static OtpAlgorithm ParseAlgorithm(string? name) =>
        TryParseAlgorithm(name, out OtpAlgorithm algorithm) ? algorithm : OtpAlgorithm.Sha1;
}
