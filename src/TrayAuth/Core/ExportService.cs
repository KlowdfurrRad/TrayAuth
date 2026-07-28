using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QRCoder;

namespace TrayAuth.Core;

/// <summary>An account as written to an export file: the vault fields plus the otpauth URI.</summary>
public sealed class ExportedAccount
{
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// The setup key as base32 — the standard form. This is what sites print next to their QR
    /// code and what every authenticator app expects, so it is the only encoding written.
    /// </summary>
    [JsonPropertyName("secret")]
    public string Secret { get; set; } = string.Empty;

    [JsonPropertyName("digits")]
    public int Digits { get; set; } = Totp.DefaultDigits;

    [JsonPropertyName("period")]
    public int Period { get; set; } = Totp.DefaultPeriod;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "SHA1";

    /// <summary>The same account in the standard URI form, ready to paste or re-encode as a QR.</summary>
    [JsonPropertyName("otpauth")]
    public string OtpAuth { get; set; } = string.Empty;

    public static ExportedAccount From(Account account) => new()
    {
        Issuer = account.Issuer,
        Label = account.Label,
        Secret = Base32.Normalize(account.Secret),
        Digits = account.Digits,
        Period = account.Period,
        Algorithm = Totp.ToName(account.AlgorithmValue),
        OtpAuth = OtpAuthUri.Build(account),
    };

    public Account ToAccount() => new()
    {
        Issuer = Issuer ?? string.Empty,
        Label = Label ?? string.Empty,
        Secret = Secret ?? string.Empty,
        Digits = Digits,
        Period = Period,
        Algorithm = Algorithm ?? "SHA1",
    };
}

public sealed class ExportDocument
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = ExportService.CombinedFormat;

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("application")]
    public string Application { get; set; } = "TrayAuth";

    [JsonPropertyName("exportedUtc")]
    public string ExportedUtc { get; set; } = string.Empty;

    /// <summary>Present on single-account exports.</summary>
    [JsonPropertyName("account")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ExportedAccount? Account { get; set; }

    /// <summary>Present on combined exports.</summary>
    [JsonPropertyName("accounts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ExportedAccount>? Accounts { get; set; }
}

public sealed record ExportResult(int AccountCount, string Directory, IReadOnlyList<string> Files);

/// <summary>
/// Writes accounts out as plain, readable files: a JSON holding everything needed to restore the
/// account, and a QR PNG of the same account that Google Authenticator can scan directly.
///
/// These files are unencrypted by design. Each one carries a secret that mints that account's codes
/// indefinitely, so they deserve the same handling as the account password itself.
/// </summary>
public static class ExportService
{
    public const string CombinedFormat = "trayauth-export";
    public const string SingleFormat = "trayauth-account";
    public const string CombinedFileName = "TrayAuth-export.json";
    public const string ReadMeFileName = "READ ME - keep these files safe.txt";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // These files are written to be read and copied from, not embedded in a web page. The
        // default HTML-safe encoder would turn the otpauth URI's '&' into '&', which is
        // correct JSON and useless to a person retyping it.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>Default place we offer to export to: local, and deliberately not inside OneDrive.</summary>
    public static string DefaultExportRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TrayAuth",
        "exports");

    /// <summary>Writes the JSON + QR pair for one account into <paramref name="directory"/>.</summary>
    public static ExportResult ExportAccount(Account account, string directory, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        Directory.CreateDirectory(directory);

        var files = new List<string>();
        files.AddRange(WriteAccountFiles(account, directory, now ?? DateTimeOffset.UtcNow));

        return new ExportResult(1, directory, files);
    }

    /// <summary>
    /// Writes a timestamped folder holding a JSON + QR pair per account, plus one combined JSON that
    /// restores the whole vault on its own.
    /// </summary>
    public static ExportResult ExportAll(IEnumerable<Account> accounts, string parentDirectory, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        List<Account> list = [.. accounts];
        DateTimeOffset stamp = now ?? DateTimeOffset.Now;

        string folderName = "TrayAuth-export-" + stamp.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture);
        string directory = UniqueDirectory(Path.Combine(parentDirectory, folderName));
        Directory.CreateDirectory(directory);

        var files = new List<string>();

        var combined = new ExportDocument
        {
            Format = CombinedFormat,
            Version = 1,
            ExportedUtc = stamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Accounts = [.. list.Select(ExportedAccount.From)],
        };

        string combinedPath = Path.Combine(directory, CombinedFileName);
        WriteTextFile(combinedPath, JsonSerializer.Serialize(combined, SerializerOptions));
        files.Add(combinedPath);

        foreach (Account account in list)
        {
            files.AddRange(WriteAccountFiles(account, directory, stamp));
        }

        string readMePath = Path.Combine(directory, ReadMeFileName);
        WriteTextFile(readMePath, BuildReadMe(list.Count, stamp));
        files.Add(readMePath);

        return new ExportResult(list.Count, directory, files);
    }

    private static IEnumerable<string> WriteAccountFiles(Account account, string directory, DateTimeOffset stamp)
    {
        string baseName = UniqueBaseName(directory, SanitizeFileName(account.FullName));

        var document = new ExportDocument
        {
            Format = SingleFormat,
            Version = 1,
            ExportedUtc = stamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Account = ExportedAccount.From(account),
        };

        string jsonPath = Path.Combine(directory, baseName + ".json");
        WriteTextFile(jsonPath, JsonSerializer.Serialize(document, SerializerOptions));
        yield return jsonPath;

        string pngPath = Path.Combine(directory, baseName + ".png");
        File.WriteAllBytes(pngPath, RenderQrPng(OtpAuthUri.Build(account)));
        Vault.HardenFile(pngPath);
        yield return pngPath;
    }

    public static byte[] RenderQrPng(string payload, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);

        // PngByteQRCode writes PNG bytes directly, with no System.Drawing bitmap in the middle.
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }

    private static void WriteTextFile(string path, string content)
    {
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Vault.HardenFile(path);
    }

    private static string BuildReadMe(int count, DateTimeOffset stamp) =>
        $"""
        TrayAuth export - {stamp.ToString("f", CultureInfo.CurrentCulture)}
        {count} account(s).

        WHAT IS IN HERE
          {CombinedFileName}
              Every account in one file. Import this back into TrayAuth
              (tray icon -> Import...) to restore the whole vault.

          <account>.json
              One account on its own, in the same importable format.
              "secret" is the base32 setup key - the standard form, the
              same thing the site showed you next to its QR code.

          <account>.png
              A QR code for that account. Scan it with Google Authenticator,
              Authy or any other authenticator app to add the account there.

        THESE FILES ARE NOT ENCRYPTED
          Each one contains the account's secret key. Anyone who reads it can
          generate that account's codes forever - it is as sensitive as the
          password itself. Keep this folder off shared drives and cloud sync,
          and delete it once you have stored it somewhere you trust.

        WHY YOU WANT A BACKUP
          TrayAuth's vault is encrypted with your Windows user account. If that
          profile is lost or Windows is reinstalled, the vault cannot be read
          again - this export is the only way back.
        """;

    /// <summary>
    /// Makes a display name safe as a Win32 filename: strips invalid characters, collapses runs of
    /// whitespace, and sidesteps the reserved device names that cannot be created at all.
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "account";
        }

        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            builder.Append(Array.IndexOf(InvalidNameChars, c) >= 0 || char.IsControl(c) ? ' ' : c);
        }

        string cleaned = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // Windows also refuses trailing dots and spaces.
        cleaned = cleaned.TrimEnd('.', ' ');

        if (cleaned.Length == 0)
        {
            return "account";
        }

        string withoutExtension = Path.GetFileNameWithoutExtension(cleaned);
        if (ReservedNames.Contains(withoutExtension, StringComparer.OrdinalIgnoreCase))
        {
            cleaned = "_" + cleaned;
        }

        // Leave room for the numeric suffix and extension inside MAX_PATH-friendly limits.
        return cleaned.Length > 100 ? cleaned[..100].TrimEnd('.', ' ') : cleaned;
    }

    private static string UniqueBaseName(string directory, string baseName)
    {
        string candidate = baseName;
        int suffix = 2;

        while (File.Exists(Path.Combine(directory, candidate + ".json"))
            || File.Exists(Path.Combine(directory, candidate + ".png")))
        {
            candidate = $"{baseName} ({suffix++})";
        }

        return candidate;
    }

    private static string UniqueDirectory(string path)
    {
        string candidate = path;
        int suffix = 2;

        while (Directory.Exists(candidate))
        {
            candidate = $"{path} ({suffix++})";
        }

        return candidate;
    }

    /// <summary>
    /// Reads accounts back from any file this app writes: a combined export, a single-account
    /// export, a bare array of accounts, or a plain list of otpauth:// URIs.
    /// </summary>
    public static IReadOnlyList<Account> Import(string filePath)
    {
        string text = File.ReadAllText(filePath).Trim();

        if (text.Length == 0)
        {
            throw new InvalidDataException("The file is empty.");
        }

        List<Account> accounts = text[0] is '{' or '['
            ? ParseJsonImport(text)
            : ParseUriListImport(text);

        if (accounts.Count == 0)
        {
            throw new InvalidDataException("No usable accounts were found in that file.");
        }

        return accounts;
    }

    private static List<Account> ParseJsonImport(string text)
    {
        var accounts = new List<Account>();

        using var json = JsonDocument.Parse(text);
        JsonElement root = json.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            AddRange(accounts, root);
            return accounts;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("The file is not a TrayAuth export.");
        }

        if (root.TryGetProperty("accounts", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            AddRange(accounts, list);
        }

        if (root.TryGetProperty("account", out JsonElement single) && single.ValueKind == JsonValueKind.Object)
        {
            AddOne(accounts, single);
        }

        if (accounts.Count == 0 && root.TryGetProperty("secret", out _))
        {
            // A bare account object with no wrapper.
            AddOne(accounts, root);
        }

        return accounts;
    }

    private static void AddRange(List<Account> accounts, JsonElement array)
    {
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                AddOne(accounts, element);
            }
        }
    }

    private static void AddOne(List<Account> accounts, JsonElement element)
    {
        ExportedAccount? exported = element.Deserialize<ExportedAccount>();
        if (exported is null)
        {
            return;
        }

        Account account = exported.ToAccount();

        // Prefer the otpauth URI when the flat fields are missing or unusable — it is the more
        // complete record of the two.
        if (!account.TryNormalize(out _))
        {
            if (!string.IsNullOrWhiteSpace(exported.OtpAuth)
                && OtpAuthUri.TryParse(exported.OtpAuth, out Account fromUri, out _))
            {
                accounts.Add(fromUri);
            }

            return;
        }

        accounts.Add(account);
    }

    private static List<Account> ParseUriListImport(string text)
    {
        var accounts = new List<Account>();

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase)
                && OtpAuthUri.TryParse(trimmed, out Account account, out _))
            {
                accounts.Add(account);
            }
        }

        return accounts;
    }
}
