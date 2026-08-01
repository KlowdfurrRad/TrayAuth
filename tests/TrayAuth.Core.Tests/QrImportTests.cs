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
