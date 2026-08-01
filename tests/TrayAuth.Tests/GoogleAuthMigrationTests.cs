using TrayAuth.Core;
using Xunit;
using static TrayAuth.Tests.MigrationTestData;

namespace TrayAuth.Tests;

public class GoogleAuthMigrationTests
{
    [Fact]
    public void TwoAccounts_ParseWithAllFields()
    {
        string uri = MigrationUri(
        [
            OtpEntry(KnownSecret, "GitHub:raadhes@gmail.com", "GitHub"),
            OtpEntry(KnownSecret, "AWS:root", "AWS", algorithm: 2, digits: 2),
        ]);

        Assert.True(GoogleAuthMigration.TryParse(uri, out MigrationResult result, out string error), error);
        Assert.Equal(2, result.Accounts.Count);

        Account first = result.Accounts[0];
        Assert.Equal("GitHub", first.Issuer);
        Assert.Equal("raadhes@gmail.com", first.Label);   // "Issuer:" prefix stripped from the name
        Assert.Equal("JBSWY3DPEHPK3PXP", first.Secret);   // raw bytes came back out as base32
        Assert.Equal(6, first.Digits);
        Assert.Equal(30, first.Period);
        Assert.Equal("SHA1", first.Algorithm);

        Account second = result.Accounts[1];
        Assert.Equal("SHA256", second.Algorithm);
        Assert.Equal(8, second.Digits);

        // The imported account must actually generate - that is the entire point.
        Assert.Equal(6, first.Generate().Code.Length);
        Assert.Equal(8, second.Generate().Code.Length);
    }

    [Fact]
    public void CounterBasedEntries_AreSkippedAndCounted()
    {
        string uri = MigrationUri(
        [
            OtpEntry(KnownSecret, "GitHub:a", "GitHub"),
            OtpEntry(KnownSecret, "OldBank:b", "OldBank", type: 1),  // HOTP
        ]);

        Assert.True(GoogleAuthMigration.TryParse(uri, out MigrationResult result, out _));
        Assert.Single(result.Accounts);
        Assert.Equal(1, result.SkippedCounterBased);
        Assert.Equal(0, result.SkippedUnsupported);
    }

    [Fact]
    public void Md5Entries_AreSkippedAsUnsupported()
    {
        string uri = MigrationUri([OtpEntry(KnownSecret, "Legacy:x", "Legacy", algorithm: 4)]);

        Assert.True(GoogleAuthMigration.TryParse(uri, out MigrationResult result, out _));
        Assert.Empty(result.Accounts);
        Assert.Equal(1, result.SkippedUnsupported);
    }

    [Fact]
    public void IssuerIsRecoveredFromTheNamePrefix_WhenTheIssuerFieldIsMissing()
    {
        string uri = MigrationUri([OtpEntry(KnownSecret, "Zerodha Kite:ZE1234")]);

        Assert.True(GoogleAuthMigration.TryParse(uri, out MigrationResult result, out _));

        Account account = Assert.Single(result.Accounts);
        Assert.Equal("Zerodha Kite", account.Issuer);
        Assert.Equal("ZE1234", account.Label);
    }

    [Fact]
    public void BatchMarkers_SurfaceForMultiQrTransfers()
    {
        string uri = MigrationUri([OtpEntry(KnownSecret, "GitHub:a", "GitHub")], batchSize: 2, batchIndex: 1);

        Assert.True(GoogleAuthMigration.TryParse(uri, out MigrationResult result, out _));
        Assert.NotNull(result.Batch);
        Assert.Equal(1, result.Batch!.Index);
        Assert.Equal(2, result.Batch.Size);
    }

    [Fact]
    public void SingleQrTransfers_ReportNoBatch()
    {
        string uri = MigrationUri([OtpEntry(KnownSecret, "GitHub:a", "GitHub")]);

        Assert.True(GoogleAuthMigration.TryParse(uri, out MigrationResult result, out _));
        Assert.Null(result.Batch);
    }

    [Fact]
    public void Base64WithPlusAndSlash_SurvivesUrlEncoding()
    {
        // 0xFB runs force '+' and '/' into the base64, exercising the escaping path end to end.
        byte[] awkwardSecret = [.. Enumerable.Repeat((byte)0xFB, 20)];
        string uri = MigrationUri([OtpEntry(awkwardSecret, "Site:user", "Site")]);

        Assert.True(GoogleAuthMigration.TryParse(uri, out MigrationResult result, out string error), error);

        Account account = Assert.Single(result.Accounts);
        Assert.Equal(Base32.Encode(awkwardSecret), account.Secret);
    }

    [Fact]
    public void PlusSignsMangledIntoSpaces_AreRepaired()
    {
        byte[] awkwardSecret = [.. Enumerable.Repeat((byte)0xFB, 20)];
        string uri = MigrationUri([OtpEntry(awkwardSecret, "Site:user", "Site")]);

        // Simulate a copy path that URL-decoded the '+' into a space along the way.
        string mangled = Uri.UnescapeDataString(uri).Replace('+', ' ');

        Assert.True(GoogleAuthMigration.TryParse(mangled, out MigrationResult result, out string error), error);
        Assert.Single(result.Accounts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("otpauth://totp/GitHub:a?secret=JBSWY3DPEHPK3PXP")]
    [InlineData("https://example.com")]
    public void NonMigrationInput_IsRejected(string? input)
    {
        Assert.False(GoogleAuthMigration.TryParse(input, out _, out _));
    }

    [Theory]
    [InlineData("otpauth-migration://offline")]
    [InlineData("otpauth-migration://offline?data=")]
    [InlineData("otpauth-migration://offline?data=%21%21%21")]
    public void MissingOrGarbageData_IsRejectedWithAnExplanation(string input)
    {
        Assert.False(GoogleAuthMigration.TryParse(input, out _, out string error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TruncatedPayload_IsRejectedNotCrashed()
    {
        string good = MigrationUri([OtpEntry(KnownSecret, "GitHub:a", "GitHub")]);

        // Chop bytes off the end of the base64 - a half-scanned QR in practice.
        string data = good["otpauth-migration://offline?data=".Length..];
        byte[] payload = Convert.FromBase64String(Uri.UnescapeDataString(data));
        string truncated = "otpauth-migration://offline?data="
            + Uri.EscapeDataString(Convert.ToBase64String(payload[..(payload.Length - 4)]));

        // Either outcome is acceptable except an exception: parse fails, or parses fewer entries.
        if (GoogleAuthMigration.TryParse(truncated, out MigrationResult result, out _))
        {
            Assert.True(result.Accounts.Count <= 1);
        }
    }
}
