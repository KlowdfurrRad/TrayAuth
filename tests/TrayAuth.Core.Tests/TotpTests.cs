using System.Text;
using TrayAuth.Core;
using Xunit;

namespace TrayAuth.Tests;

/// <summary>
/// The published RFC 6238 test vectors. If these pass, the generator agrees with every other
/// authenticator in the world; nothing else in the app is worth checking until they do.
/// </summary>
public class TotpTests
{
    // RFC 6238 Appendix B seeds.
    private static readonly byte[] Sha1Key = Encoding.ASCII.GetBytes("12345678901234567890");
    private static readonly byte[] Sha256Key = Encoding.ASCII.GetBytes("12345678901234567890123456789012");
    private static readonly byte[] Sha512Key = Encoding.ASCII.GetBytes("1234567890123456789012345678901234567890123456789012345678901234");

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Sha1_MatchesRfc6238(long unixTime, string expected)
    {
        TotpCode code = Totp.Generate(Sha1Key, digits: 8, period: 30, OtpAlgorithm.Sha1, DateTimeOffset.FromUnixTimeSeconds(unixTime));
        Assert.Equal(expected, code.Code);
    }

    [Theory]
    [InlineData(59L, "46119246")]
    [InlineData(1111111109L, "68084774")]
    [InlineData(1111111111L, "67062674")]
    [InlineData(1234567890L, "91819424")]
    [InlineData(2000000000L, "90698825")]
    [InlineData(20000000000L, "77737706")]
    public void Sha256_MatchesRfc6238(long unixTime, string expected)
    {
        TotpCode code = Totp.Generate(Sha256Key, digits: 8, period: 30, OtpAlgorithm.Sha256, DateTimeOffset.FromUnixTimeSeconds(unixTime));
        Assert.Equal(expected, code.Code);
    }

    [Theory]
    [InlineData(59L, "90693936")]
    [InlineData(1111111109L, "25091201")]
    [InlineData(1111111111L, "99943326")]
    [InlineData(1234567890L, "93441116")]
    [InlineData(2000000000L, "38618901")]
    [InlineData(20000000000L, "47863826")]
    public void Sha512_MatchesRfc6238(long unixTime, string expected)
    {
        TotpCode code = Totp.Generate(Sha512Key, digits: 8, period: 30, OtpAlgorithm.Sha512, DateTimeOffset.FromUnixTimeSeconds(unixTime));
        Assert.Equal(expected, code.Code);
    }

    [Theory]
    [InlineData(0L, "755224")]
    [InlineData(1L, "287082")]
    [InlineData(2L, "359152")]
    [InlineData(3L, "969429")]
    [InlineData(4L, "338314")]
    [InlineData(5L, "254676")]
    [InlineData(6L, "287922")]
    [InlineData(7L, "162583")]
    [InlineData(8L, "399871")]
    [InlineData(9L, "520489")]
    public void Hotp_MatchesRfc4226(long counter, string expected)
    {
        Assert.Equal(expected, Totp.ComputeHotp(Sha1Key, counter, digits: 6));
    }

    [Fact]
    public void SecondsRemaining_CountsDownWithinTheStep()
    {
        // 30-second steps: at :00 a full period is left, at :29 exactly one second is.
        Assert.Equal(30, Totp.Generate(Sha1Key, at: DateTimeOffset.FromUnixTimeSeconds(1_700_000_010)).SecondsRemaining);
        Assert.Equal(1, Totp.Generate(Sha1Key, at: DateTimeOffset.FromUnixTimeSeconds(1_700_000_039)).SecondsRemaining);
    }

    [Fact]
    public void CodeChangesOnlyAtTheStepBoundary()
    {
        var justBefore = DateTimeOffset.FromUnixTimeSeconds(1_700_000_039);
        var atBoundary = DateTimeOffset.FromUnixTimeSeconds(1_700_000_040);

        string a = Totp.Generate(Sha1Key, at: DateTimeOffset.FromUnixTimeSeconds(1_700_000_010)).Code;
        string b = Totp.Generate(Sha1Key, at: justBefore).Code;
        string c = Totp.Generate(Sha1Key, at: atBoundary).Code;

        Assert.Equal(a, b);
        Assert.NotEqual(b, c);
    }

    [Fact]
    public void Grouping_SplitsSixAndEightDigitCodes()
    {
        Assert.Equal("482 913", TotpCode.Format("482913"));
        Assert.Equal("4829 1374", TotpCode.Format("48291374"));
        Assert.Equal("4829137", TotpCode.Format("4829137"));
    }

    [Fact]
    public void UnsupportedDigitCounts_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Totp.ComputeHotp(Sha1Key, 1, digits: 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Totp.ComputeHotp(Sha1Key, 1, digits: 9));
    }

    [Fact]
    public void AlgorithmNames_RoundTrip()
    {
        Assert.Equal(OtpAlgorithm.Sha1, Totp.ParseAlgorithm("sha1"));
        Assert.Equal(OtpAlgorithm.Sha256, Totp.ParseAlgorithm("SHA-256"));
        Assert.Equal(OtpAlgorithm.Sha512, Totp.ParseAlgorithm("sha512"));

        // Anything unrecognised falls back to SHA1, which is what the otpauth spec defaults to.
        Assert.Equal(OtpAlgorithm.Sha1, Totp.ParseAlgorithm("md5"));
        Assert.Equal(OtpAlgorithm.Sha1, Totp.ParseAlgorithm(null));
    }
}
