using System.Text;
using TrayAuth.Core;
using Xunit;

namespace TrayAuth.Tests;

public class ExportTests : IDisposable
{
    private readonly string _directory;

    public ExportTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "TrayAuthTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    private static Account Sample(string issuer = "GitHub", string label = "user@example.com") => new()
    {
        Issuer = issuer,
        Label = label,
        Secret = "JBSWY3DPEHPK3PXP",
    };

    [Fact]
    public void ExportAccount_WritesTheJsonAndQrPair()
    {
        Account account = Sample();
        ExportResult result = ExportService.ExportAccount(account, _directory);

        Assert.Equal(1, result.AccountCount);
        Assert.Equal(2, result.Files.Count);

        string json = Assert.Single(result.Files, f => f.EndsWith(".json", StringComparison.Ordinal));
        string png = Assert.Single(result.Files, f => f.EndsWith(".png", StringComparison.Ordinal));

        Assert.True(File.Exists(json));
        Assert.True(File.Exists(png));

        // A real PNG, not an empty placeholder.
        byte[] header = File.ReadAllBytes(png)[..8];
        Assert.Equal<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], header);
    }

    [Fact]
    public void ExportedJson_IsReadableAndCarriesTheOtpAuthUri()
    {
        ExportResult result = ExportService.ExportAccount(Sample(), _directory);
        string json = File.ReadAllText(result.Files.First(f => f.EndsWith(".json", StringComparison.Ordinal)));

        // Plain text by design — the user asked for files they can open.
        Assert.Contains("JBSWY3DPEHPK3PXP", json, StringComparison.Ordinal);
        Assert.Contains("otpauth://totp/GitHub:user%40example.com", json, StringComparison.Ordinal);
        Assert.Contains("\"issuer\": \"GitHub\"", json, StringComparison.Ordinal);

        // The URI must be copyable straight out of the file, not escaped into & soup.
        Assert.Contains("?secret=JBSWY3DPEHPK3PXP&issuer=GitHub&algorithm=SHA1", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0026", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportedSecret_IsTheStandardBase32SetupKey()
    {
        // Base32 is the only encoding written: it is what the site gave you, and what any other
        // authenticator app will take back.
        ExportedAccount exported = ExportedAccount.From(Sample());

        Assert.Equal("JBSWY3DPEHPK3PXP", exported.Secret);
        Assert.Contains("secret=JBSWY3DPEHPK3PXP", exported.OtpAuth, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportAll_WritesACombinedFileAPairPerAccountAndAReadMe()
    {
        List<Account> accounts = [Sample("GitHub", "a@example.com"), Sample("AWS", "root")];

        ExportResult result = ExportService.ExportAll(accounts, _directory);

        Assert.Equal(2, result.AccountCount);
        Assert.True(Directory.Exists(result.Directory));
        Assert.StartsWith("TrayAuth-export-", Path.GetFileName(result.Directory), StringComparison.Ordinal);

        string[] files = Directory.GetFiles(result.Directory).Select(Path.GetFileName).OfType<string>().ToArray();

        Assert.Contains(ExportService.CombinedFileName, files);
        Assert.Contains(ExportService.ReadMeFileName, files);
        Assert.Contains("GitHub - a@example.com.json", files);
        Assert.Contains("GitHub - a@example.com.png", files);
        Assert.Contains("AWS - root.json", files);
        Assert.Contains("AWS - root.png", files);
    }

    [Fact]
    public void ExportThenImport_RestoresTheAccountsUnchanged()
    {
        List<Account> original =
        [
            new Account { Issuer = "GitHub", Label = "a@example.com", Secret = "JBSWY3DPEHPK3PXP" },
            new Account { Issuer = "AWS", Label = "root", Secret = "JBSWY3DPEHPK3PXP", Digits = 8, Period = 60, Algorithm = "SHA512" },
        ];

        ExportResult result = ExportService.ExportAll(original, _directory);
        IReadOnlyList<Account> imported = ExportService.Import(Path.Combine(result.Directory, ExportService.CombinedFileName));

        Assert.Equal(2, imported.Count);

        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].Issuer, imported[i].Issuer);
            Assert.Equal(original[i].Label, imported[i].Label);
            Assert.Equal(original[i].Secret, imported[i].Secret);
            Assert.Equal(original[i].Digits, imported[i].Digits);
            Assert.Equal(original[i].Period, imported[i].Period);
            Assert.Equal(original[i].Algorithm, imported[i].Algorithm);

            // The whole point of the backup: the same codes come out the other side.
            var instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
            Assert.Equal(original[i].Generate(instant).Code, imported[i].Generate(instant).Code);
        }
    }

    [Fact]
    public void Import_AcceptsASingleAccountExport()
    {
        ExportResult result = ExportService.ExportAccount(Sample(), _directory);
        string json = result.Files.First(f => f.EndsWith(".json", StringComparison.Ordinal));

        Account imported = Assert.Single(ExportService.Import(json));
        Assert.Equal("GitHub", imported.Issuer);
        Assert.Equal("user@example.com", imported.Label);
    }

    [Fact]
    public void Import_AcceptsAPlainListOfOtpAuthUris()
    {
        string path = Path.Combine(_directory, "uris.txt");
        File.WriteAllText(
            path,
            "otpauth://totp/GitHub:a%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub\r\n"
            + "otpauth://totp/AWS:root?secret=JBSWY3DPEHPK3PXP&issuer=AWS&digits=8\r\n");

        IReadOnlyList<Account> imported = ExportService.Import(path);

        Assert.Equal(2, imported.Count);
        Assert.Equal("a@example.com", imported[0].Label);
        Assert.Equal(8, imported[1].Digits);
    }

    [Fact]
    public void Import_RejectsFilesThatHoldNoAccounts()
    {
        string path = Path.Combine(_directory, "empty.json");
        File.WriteAllText(path, "{\"format\":\"trayauth-export\",\"accounts\":[]}");

        Assert.Throws<InvalidDataException>(() => ExportService.Import(path));
    }

    [Theory]
    [InlineData("GitHub - user@example.com", "GitHub - user@example.com")]
    [InlineData("Bank / Savings", "Bank Savings")]
    [InlineData("a:b*c?d\"e<f>g|h", "a b c d e f g h")]
    [InlineData("trailing dots...", "trailing dots")]
    [InlineData("   ", "account")]
    [InlineData("CON", "_CON")]
    [InlineData("com1", "_com1")]
    public void SanitizeFileName_ProducesNamesWindowsWillAccept(string input, string expected)
    {
        Assert.Equal(expected, ExportService.SanitizeFileName(input));
    }

    [Fact]
    public void ExportAll_DoesNotOverwriteAccountsThatShareADisplayName()
    {
        // Two entries can legitimately look identical; neither should silently replace the other.
        List<Account> accounts = [Sample("GitHub", "a@example.com"), Sample("GitHub", "a@example.com")];

        ExportResult result = ExportService.ExportAll(accounts, _directory);
        string[] files = Directory.GetFiles(result.Directory, "*.json").Select(Path.GetFileName).OfType<string>().ToArray();

        Assert.Contains("GitHub - a@example.com.json", files);
        Assert.Contains("GitHub - a@example.com (2).json", files);
    }

    [Fact]
    public void ExportAll_TwiceIntoTheSamePlace_KeepsBothFolders()
    {
        var stamp = new DateTimeOffset(2026, 7, 28, 14, 30, 0, TimeSpan.Zero);

        ExportResult first = ExportService.ExportAll([Sample()], _directory, stamp);
        ExportResult second = ExportService.ExportAll([Sample()], _directory, stamp);

        Assert.NotEqual(first.Directory, second.Directory);
        Assert.True(Directory.Exists(first.Directory));
        Assert.True(Directory.Exists(second.Directory));
    }

    [Fact]
    public void ReadMe_WarnsThatTheFilesAreNotEncrypted()
    {
        ExportResult result = ExportService.ExportAll([Sample()], _directory);
        string readMe = File.ReadAllText(Path.Combine(result.Directory, ExportService.ReadMeFileName));

        Assert.Contains("NOT ENCRYPTED", readMe, StringComparison.Ordinal);
    }

    [Fact]
    public void OtpAuthUri_RoundTripsThroughBuildAndParse()
    {
        var account = new Account
        {
            Issuer = "Green Execution",
            Label = "raadhes@gmail.com",
            Secret = "JBSWY3DPEHPK3PXP",
            Digits = 8,
            Period = 45,
            Algorithm = "SHA256",
        };

        string uri = OtpAuthUri.Build(account);
        Assert.True(OtpAuthUri.TryParse(uri, out Account parsed, out string error), error);

        Assert.Equal(account.Issuer, parsed.Issuer);
        Assert.Equal(account.Label, parsed.Label);
        Assert.Equal(account.Secret, parsed.Secret);
        Assert.Equal(account.Digits, parsed.Digits);
        Assert.Equal(account.Period, parsed.Period);
        Assert.Equal(account.Algorithm, parsed.Algorithm);
    }

    [Fact]
    public void OtpAuthUri_RejectsCounterBasedAccounts()
    {
        Assert.False(OtpAuthUri.TryParse("otpauth://hotp/X?secret=JBSWY3DPEHPK3PXP&counter=1", out _, out string error));
        Assert.Contains("totp", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QrPng_EncodesSomethingOfAPlausibleSize()
    {
        byte[] png = ExportService.RenderQrPng("otpauth://totp/Test?secret=JBSWY3DPEHPK3PXP");

        Assert.True(png.Length > 100);
        Assert.Equal("PNG", Encoding.ASCII.GetString(png, 1, 3));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Temp cleanup only.
        }

        GC.SuppressFinalize(this);
    }
}
