using System.Text;
using TrayAuth.Core;
using Xunit;

namespace TrayAuth.Tests;

public class Base32Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY======")]
    [InlineData("fo", "MZXQ====")]
    [InlineData("foo", "MZXW6===")]
    [InlineData("foob", "MZXW6YQ=")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI======")]
    public void Decode_MatchesRfc4648Vectors(string plain, string encoded)
    {
        if (plain.Length == 0)
        {
            Assert.False(Base32.TryDecode(encoded, out _));
            return;
        }

        Assert.True(Base32.TryDecode(encoded, out byte[] decoded));
        Assert.Equal(plain, Encoding.ASCII.GetString(decoded));
    }

    [Theory]
    [InlineData("MZXW6YTB")]
    [InlineData("mzxw6ytb")]
    [InlineData("MZXW 6YTB")]
    [InlineData("mzxw-6ytb")]
    [InlineData("MZXW_6YTB")]
    [InlineData("  MZXW6YTB  ")]
    [InlineData("MZXW6YTB========")]
    [InlineData("MZ XW 6Y TB")]
    public void Decode_IgnoresFormattingSitesAddToSetupKeys(string input)
    {
        // Every one of these is how some real service prints the same key.
        Assert.True(Base32.TryDecode(input, out byte[] decoded));
        Assert.Equal("fooba", Encoding.ASCII.GetString(decoded));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("====")]
    [InlineData("MZXW6YT!")]
    [InlineData("01890")]     // 0, 1, 8 and 9 are not in the base32 alphabet
    [InlineData("MZ$W6YTB")]
    public void Decode_RejectsInputThatIsNotBase32(string? input)
    {
        Assert.False(Base32.TryDecode(input, out _));
    }

    [Fact]
    public void Decode_RejectsInputTooShortToYieldAByte()
    {
        // A single character carries five bits — not enough for one byte.
        Assert.False(Base32.TryDecode("M", out _));
    }

    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        byte[] original = [0x00, 0x7F, 0x80, 0xFF, 0x10, 0x3C, 0xA5];

        string encoded = Base32.Encode(original);
        Assert.True(Base32.TryDecode(encoded, out byte[] decoded));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Normalize_StripsSeparatorsAndUpperCases()
    {
        Assert.Equal("JBSWY3DPEHPK3PXP", Base32.Normalize(" jbsw y3dp-ehpk_3pxp== "));
    }

    [Fact]
    public void Decode_HandlesTheWidelyUsedTestSecret()
    {
        Assert.True(Base32.TryDecode("JBSWY3DPEHPK3PXP", out byte[] decoded));
        Assert.Equal("Hello!\xDE\xAD\xBE\xEF", Encoding.Latin1.GetString(decoded));
    }
}
