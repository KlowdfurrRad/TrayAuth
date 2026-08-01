using TrayAuth.Core;
using Xunit;
using static TrayAuth.Tests.MigrationTestData;

namespace TrayAuth.Tests;

public class QrImportTests : IDisposable
{
    private readonly string _directory;

    public QrImportTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "TrayAuthTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void QrRoundTrip_MigrationUriComesBackOutOfItsOwnPng()
    {
        // Encode with QRCoder (the export path), decode with ZXing (the import path). If these
        // two libraries agree, a QR we render is scannable - and the import path is proven
        // end to end without a phone in the loop.
        string uri = MigrationUri([OtpEntry(KnownSecret, "GitHub:raadhes@gmail.com", "GitHub")]);
        byte[] png = ExportService.RenderQrPng(uri);

        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);

        IReadOnlyList<string> texts = QrDecoder.Decode(bitmap);
        Assert.Contains(uri, texts);
    }

    [Fact]
    public void QrPastedIntoALargeImage_IsStillFound()
    {
        // The screen-scan case: a QR somewhere on a big mostly-empty surface.
        string uri = "otpauth://totp/GitHub:a%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub";
        byte[] png = ExportService.RenderQrPng(uri);

        using var qrStream = new MemoryStream(png);
        using var qr = new Bitmap(qrStream);
        using var screen = new Bitmap(1200, 800);

        using (var graphics = Graphics.FromImage(screen))
        {
            graphics.Clear(Color.White);
            graphics.DrawImageUnscaled(qr, 700, 320);
        }

        IReadOnlyList<string> texts = QrDecoder.Decode(screen);
        Assert.Contains(uri, texts);
    }

    [Fact]
    public void TwoQrsInOneImage_AreBothFound()
    {
        // Google Authenticator shows batch QRs one after another; a screenshot may hold two.
        string first = MigrationUri([OtpEntry(KnownSecret, "GitHub:a", "GitHub")], batchSize: 2, batchIndex: 0);
        string second = MigrationUri([OtpEntry(KnownSecret, "AWS:root", "AWS")], batchSize: 2, batchIndex: 1);

        using var canvas = new Bitmap(1400, 600);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(Color.White);

            using var qr1Stream = new MemoryStream(ExportService.RenderQrPng(first));
            using var qr1 = new Bitmap(qr1Stream);
            graphics.DrawImageUnscaled(qr1, 80, 100);

            using var qr2Stream = new MemoryStream(ExportService.RenderQrPng(second));
            using var qr2 = new Bitmap(qr2Stream);
            graphics.DrawImageUnscaled(qr2, 800, 100);
        }

        IReadOnlyList<string> texts = QrDecoder.Decode(canvas);

        Assert.Contains(first, texts);
        Assert.Contains(second, texts);
    }

    [Fact]
    public void BlankImage_FindsNothing()
    {
        using var blank = new Bitmap(600, 400);
        using (var graphics = Graphics.FromImage(blank))
        {
            graphics.Clear(Color.White);
        }

        Assert.Empty(QrDecoder.Decode(blank));
    }

    [Fact]
    public void CollectAccounts_MixesMigrationAndPlainOtpauthPayloads()
    {
        string migration = MigrationUri([OtpEntry(KnownSecret, "GitHub:a", "GitHub")]);
        string plain = "otpauth://totp/AWS:root?secret=JBSWY3DPEHPK3PXP&issuer=AWS&digits=8";

        QrImportOutcome outcome = QrImport.CollectAccounts([migration, plain, "https://not-an-otp.example"]);

        Assert.Equal(2, outcome.Accounts.Count);
        Assert.Equal("GitHub", outcome.Accounts[0].Issuer);
        Assert.Equal("AWS", outcome.Accounts[1].Issuer);
        Assert.Equal(8, outcome.Accounts[1].Digits);
    }

    [Fact]
    public void CollectAccounts_FlagsAnIncompleteBatch()
    {
        string qr1of2 = MigrationUri([OtpEntry(KnownSecret, "GitHub:a", "GitHub")], batchSize: 2, batchIndex: 0);

        QrImportOutcome outcome = QrImport.CollectAccounts([qr1of2]);

        Assert.Single(outcome.Accounts);
        Assert.Contains(outcome.Notes, n => n.Contains("QR 1 of 2", StringComparison.Ordinal));
    }

    [Fact]
    public void CollectAccounts_StaysQuietWhenAllBatchesWereScanned()
    {
        string qr1 = MigrationUri([OtpEntry(KnownSecret, "GitHub:a", "GitHub")], batchSize: 2, batchIndex: 0);
        string qr2 = MigrationUri([OtpEntry(KnownSecret, "AWS:root", "AWS")], batchSize: 2, batchIndex: 1);

        QrImportOutcome outcome = QrImport.CollectAccounts([qr1, qr2]);

        Assert.Equal(2, outcome.Accounts.Count);
        Assert.DoesNotContain(outcome.Notes, n => n.Contains("remaining accounts", StringComparison.Ordinal));
    }

    [Fact]
    public void CollectAccounts_DedupesTheSameQrScannedTwice()
    {
        string uri = MigrationUri([OtpEntry(KnownSecret, "GitHub:a", "GitHub")]);

        QrImportOutcome outcome = QrImport.CollectAccounts([uri, uri]);

        Assert.Single(outcome.Accounts);
    }

    [Fact]
    public void CollectAccounts_ExplainsWhenNothingWasAnAuthenticatorQr()
    {
        QrImportOutcome outcome = QrImport.CollectAccounts(["https://example.com", "WIFI:S:home;;"]);

        Assert.Empty(outcome.Accounts);
        Assert.Contains(outcome.Notes, n => n.Contains("do not contain authenticator accounts", StringComparison.Ordinal));
    }

    [Fact]
    public void CollectAccounts_ReportsSkippedHotpEntries()
    {
        string uri = MigrationUri(
        [
            OtpEntry(KnownSecret, "GitHub:a", "GitHub"),
            OtpEntry(KnownSecret, "OldBank:b", "OldBank", type: 1),
        ]);

        QrImportOutcome outcome = QrImport.CollectAccounts([uri]);

        Assert.Single(outcome.Accounts);
        Assert.Contains(outcome.Notes, n => n.Contains("HOTP", StringComparison.Ordinal));
    }

    [Fact]
    public void ExportServiceImport_ReadsAPastedMigrationUriFromATextFile()
    {
        string uri = MigrationUri(
        [
            OtpEntry(KnownSecret, "GitHub:a@example.com", "GitHub"),
            OtpEntry(KnownSecret, "AWS:root", "AWS", algorithm: 3, digits: 2),
        ]);

        string path = Path.Combine(_directory, "migration.txt");
        File.WriteAllText(path, uri + "\r\n");

        IReadOnlyList<Account> imported = ExportService.Import(path);

        Assert.Equal(2, imported.Count);
        Assert.Equal("SHA512", imported[1].Algorithm);
        Assert.Equal(8, imported[1].Digits);
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
