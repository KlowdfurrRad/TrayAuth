using System.Text.Json.Serialization;

namespace TrayAuth.Core;

/// <summary>A single authenticator entry. This is also the JSON shape written to the vault.</summary>
public sealed class Account
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("issuer")]
    public string Issuer { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("secret")]
    public string Secret { get; set; } = string.Empty;

    [JsonPropertyName("digits")]
    public int Digits { get; set; } = Totp.DefaultDigits;

    [JsonPropertyName("period")]
    public int Period { get; set; } = Totp.DefaultPeriod;

    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "SHA1";

    [JsonIgnore]
    public OtpAlgorithm AlgorithmValue => Totp.ParseAlgorithm(Algorithm);

    /// <summary>Primary line in the UI: the issuer if we have one, otherwise the label.</summary>
    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Issuer) ? Label : Issuer;

    /// <summary>Secondary line: the account label, suppressed when it would just repeat the title.</summary>
    [JsonIgnore]
    public string DisplaySubtitle =>
        string.IsNullOrWhiteSpace(Issuer) || string.Equals(Issuer, Label, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : Label;

    /// <summary>Name used for exported files and for matching on import.</summary>
    [JsonIgnore]
    public string FullName => string.IsNullOrWhiteSpace(DisplaySubtitle)
        ? DisplayTitle
        : $"{DisplayTitle} - {DisplaySubtitle}";

    public TotpCode Generate(DateTimeOffset? at = null) =>
        Totp.Generate(Secret, Digits, Period, AlgorithmValue, at);

    public Account Clone() => new()
    {
        Id = Id,
        Issuer = Issuer,
        Label = Label,
        Secret = Secret,
        Digits = Digits,
        Period = Period,
        Algorithm = Algorithm,
    };

    /// <summary>
    /// Normalizes user input in place and reports why it is unusable, if it is. Callers show
    /// <paramref name="error"/> directly, so the messages are written for a person, not a log.
    /// </summary>
    public bool TryNormalize(out string error)
    {
        Issuer = (Issuer ?? string.Empty).Trim();
        Label = (Label ?? string.Empty).Trim();
        Algorithm = Totp.ToName(Totp.ParseAlgorithm(Algorithm));
        Secret = Base32.Normalize(Secret);

        if (string.IsNullOrWhiteSpace(Issuer) && string.IsNullOrWhiteSpace(Label))
        {
            error = "Enter an issuer or an account name so you can tell this entry apart.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Secret))
        {
            error = "Enter the setup key from the site.";
            return false;
        }

        if (!Base32.TryDecode(Secret, out byte[] key) || key.Length == 0)
        {
            error = "That setup key isn't valid base32. It should contain only A-Z and 2-7.";
            return false;
        }

        if (Digits is < 6 or > 8)
        {
            error = "Digits must be 6, 7 or 8.";
            return false;
        }

        if (Period is < 1 or > 300)
        {
            error = "The period must be between 1 and 300 seconds.";
            return false;
        }

        if (string.IsNullOrEmpty(Id))
        {
            Id = Guid.NewGuid().ToString("N");
        }

        error = string.Empty;
        return true;
    }

    /// <summary>True when two entries describe the same account, used to detect import conflicts.</summary>
    public bool Matches(Account other) =>
        string.Equals(Issuer, other.Issuer, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Label, other.Label, StringComparison.OrdinalIgnoreCase);
}
