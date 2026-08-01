using System.Text;

namespace TrayAuth.Core;

/// <summary>One batch marker from a Google Authenticator transfer ("QR x of y").</summary>
public sealed record MigrationBatch(int Index, int Size);

public sealed record MigrationResult(
    IReadOnlyList<Account> Accounts,
    int SkippedCounterBased,
    int SkippedUnsupported,
    MigrationBatch? Batch);

/// <summary>
/// Reads the QR payload Google Authenticator produces under "Transfer accounts": an
/// otpauth-migration://offline?data=... URI whose data parameter is a base64-encoded protobuf
/// holding every account in the batch.
///
/// The protobuf is decoded by the ~60-line reader at the bottom of this file rather than a
/// Google.Protobuf dependency: the schema is tiny, uses only two wire types, and is frozen in
/// practice - every authenticator that imports these QRs depends on it staying as it is.
///
/// MigrationPayload:            OtpParameters:
///   1: repeated OtpParameters    1: bytes  secret          (raw key bytes, not base32)
///   2: int32 version             2: string name            (often "Issuer:account")
///   3: int32 batch_size          3: string issuer
///   4: int32 batch_index         4: enum   algorithm       (1 SHA1, 2 SHA256, 3 SHA512, 4 MD5)
///   5: int32 batch_id            5: enum   digits          (1 = six, 2 = eight)
///                                6: enum   type            (1 HOTP, 2 TOTP)
///                                7: int64  counter         (HOTP only)
/// </summary>
public static class GoogleAuthMigration
{
    public const string UriPrefix = "otpauth-migration://";

    public static bool IsMigrationUri(string? text) =>
        text is not null && text.TrimStart().StartsWith(UriPrefix, StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string? uriText, out MigrationResult result, out string error)
    {
        result = new MigrationResult([], 0, 0, null);

        if (!IsMigrationUri(uriText))
        {
            error = "Not a Google Authenticator transfer URI.";
            return false;
        }

        string? data = ExtractDataParameter(uriText!.Trim());
        if (string.IsNullOrEmpty(data))
        {
            error = "The QR does not carry a data payload.";
            return false;
        }

        byte[] payload;
        try
        {
            payload = FromBase64Lenient(data);
        }
        catch (FormatException)
        {
            error = "The QR payload is not valid base64.";
            return false;
        }

        try
        {
            result = ParsePayload(payload);
        }
        catch (InvalidDataException)
        {
            error = "The QR payload could not be decoded.";
            return false;
        }

        if (result.Accounts.Count == 0 && result.SkippedCounterBased == 0 && result.SkippedUnsupported == 0)
        {
            error = "The QR contains no accounts.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Pulls the raw data= value out of the query by hand. Generic query parsing is wrong here:
    /// '+' inside the base64 must stay '+', while a '+' that some copy path already turned into
    /// a space has to be turned back.
    /// </summary>
    private static string? ExtractDataParameter(string uri)
    {
        int question = uri.IndexOf('?');
        if (question < 0)
        {
            return null;
        }

        foreach (string pair in uri[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals <= 0 || !pair[..equals].Equals("data", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(pair[(equals + 1)..]).Replace(' ', '+');
        }

        return null;
    }

    private static byte[] FromBase64Lenient(string data)
    {
        string cleaned = data.Trim();
        int remainder = cleaned.Length % 4;
        if (remainder != 0)
        {
            cleaned += new string('=', 4 - remainder);
        }

        return Convert.FromBase64String(cleaned);
    }

    // ---- payload ------------------------------------------------------------------------

    private static MigrationResult ParsePayload(ReadOnlySpan<byte> payload)
    {
        var accounts = new List<Account>();
        int counterBased = 0;
        int unsupported = 0;
        int batchIndex = 0;
        int batchSize = 0;

        int pos = 0;
        while (pos < payload.Length)
        {
            ulong tag = ReadVarint(payload, ref pos);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 0x7);

            if (field == 1 && wire == 2)
            {
                switch (ParseOtpParameters(ReadLengthDelimited(payload, ref pos), out Account? account))
                {
                    case OtpEntryKind.TimeBased:
                        accounts.Add(account!);
                        break;
                    case OtpEntryKind.CounterBased:
                        counterBased++;
                        break;
                    default:
                        unsupported++;
                        break;
                }
            }
            else if (field == 3 && wire == 0)
            {
                batchSize = (int)ReadVarint(payload, ref pos);
            }
            else if (field == 4 && wire == 0)
            {
                batchIndex = (int)ReadVarint(payload, ref pos);
            }
            else
            {
                Skip(payload, ref pos, wire);
            }
        }

        MigrationBatch? batch = batchSize > 1 ? new MigrationBatch(batchIndex, batchSize) : null;
        return new MigrationResult(accounts, counterBased, unsupported, batch);
    }

    private enum OtpEntryKind
    {
        TimeBased,
        CounterBased,
        Unsupported,
    }

    private static OtpEntryKind ParseOtpParameters(ReadOnlySpan<byte> data, out Account? account)
    {
        account = null;

        byte[] secret = [];
        string name = string.Empty;
        string issuer = string.Empty;
        ulong algorithm = 0;
        ulong digits = 0;
        ulong type = 0;

        int pos = 0;
        while (pos < data.Length)
        {
            ulong tag = ReadVarint(data, ref pos);
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 0x7);

            switch (field, wire)
            {
                case (1, 2):
                    secret = ReadLengthDelimited(data, ref pos).ToArray();
                    break;
                case (2, 2):
                    name = Encoding.UTF8.GetString(ReadLengthDelimited(data, ref pos));
                    break;
                case (3, 2):
                    issuer = Encoding.UTF8.GetString(ReadLengthDelimited(data, ref pos));
                    break;
                case (4, 0):
                    algorithm = ReadVarint(data, ref pos);
                    break;
                case (5, 0):
                    digits = ReadVarint(data, ref pos);
                    break;
                case (6, 0):
                    type = ReadVarint(data, ref pos);
                    break;
                default:
                    Skip(data, ref pos, wire);
                    break;
            }
        }

        // HOTP needs a counter we do not track; importing it would show codes that are simply wrong.
        if (type == 1)
        {
            return OtpEntryKind.CounterBased;
        }

        if (secret.Length == 0)
        {
            return OtpEntryKind.Unsupported;
        }

        string algorithmName = algorithm switch
        {
            0 or 1 => "SHA1",
            2 => "SHA256",
            3 => "SHA512",
            _ => string.Empty, // MD5 (4) and anything newer than the schema we know.
        };

        int digitCount = digits switch
        {
            0 or 1 => 6,
            2 => 8,
            _ => 0,
        };

        if (algorithmName.Length == 0 || digitCount == 0)
        {
            return OtpEntryKind.Unsupported;
        }

        // Google stores the display name as "Issuer:account" when it knows the issuer.
        string label = name.Trim();
        issuer = issuer.Trim();

        if (issuer.Length == 0)
        {
            int colon = label.IndexOf(':');
            if (colon > 0)
            {
                issuer = label[..colon].Trim();
                label = label[(colon + 1)..].Trim();
            }
        }
        else if (label.StartsWith(issuer + ":", StringComparison.OrdinalIgnoreCase))
        {
            label = label[(issuer.Length + 1)..].Trim();
        }

        var candidate = new Account
        {
            Issuer = issuer,
            Label = label,
            Secret = Base32.Encode(secret),
            Digits = digitCount,
            Period = Totp.DefaultPeriod, // the payload carries no period; Google Authenticator is always 30s
            Algorithm = algorithmName,
        };

        if (!candidate.TryNormalize(out _))
        {
            return OtpEntryKind.Unsupported;
        }

        account = candidate;
        return OtpEntryKind.TimeBased;
    }

    // ---- minimal protobuf reader ----------------------------------------------------------

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int pos)
    {
        ulong value = 0;
        int shift = 0;

        while (true)
        {
            if (pos >= data.Length)
            {
                throw new InvalidDataException("Truncated varint.");
            }

            byte b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;

            if ((b & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
            if (shift > 63)
            {
                throw new InvalidDataException("Varint too long.");
            }
        }
    }

    private static ReadOnlySpan<byte> ReadLengthDelimited(ReadOnlySpan<byte> data, ref int pos)
    {
        int length = checked((int)ReadVarint(data, ref pos));
        if (length < 0 || pos + length > data.Length)
        {
            throw new InvalidDataException("Truncated length-delimited field.");
        }

        ReadOnlySpan<byte> slice = data.Slice(pos, length);
        pos += length;
        return slice;
    }

    private static void Skip(ReadOnlySpan<byte> data, ref int pos, int wire)
    {
        switch (wire)
        {
            case 0:
                ReadVarint(data, ref pos);
                break;
            case 1:
                pos += 8;
                break;
            case 2:
                ReadLengthDelimited(data, ref pos);
                break;
            case 5:
                pos += 4;
                break;
            default:
                throw new InvalidDataException($"Unsupported wire type {wire}.");
        }

        if (pos > data.Length)
        {
            throw new InvalidDataException("Truncated field.");
        }
    }
}
