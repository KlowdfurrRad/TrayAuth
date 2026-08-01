using System.Globalization;

namespace TrayAuth.Core;

/// <summary>
/// Builds and parses the <c>otpauth://totp/...</c> URIs that every authenticator app understands.
/// This is what the exported QR images encode, so a phone can pick an account up unchanged.
/// </summary>
public static class OtpAuthUri
{
    public const string Scheme = "otpauth";

    public static string Build(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        string issuer = account.Issuer.Trim();
        string label = account.Label.Trim();

        // The path is "Issuer:Account" when both are known — that is what makes the issuer show up
        // as a heading in Google Authenticator rather than as part of the account name.
        string path = string.IsNullOrEmpty(issuer)
            ? Uri.EscapeDataString(label)
            : string.IsNullOrEmpty(label)
                ? Uri.EscapeDataString(issuer)
                : $"{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(label)}";

        var query = new List<string>
        {
            "secret=" + Uri.EscapeDataString(Base32.Normalize(account.Secret)),
        };

        if (!string.IsNullOrEmpty(issuer))
        {
            query.Add("issuer=" + Uri.EscapeDataString(issuer));
        }

        query.Add("algorithm=" + Totp.ToName(account.AlgorithmValue));
        query.Add("digits=" + account.Digits.ToString(CultureInfo.InvariantCulture));
        query.Add("period=" + account.Period.ToString(CultureInfo.InvariantCulture));

        return $"{Scheme}://totp/{path}?{string.Join("&", query)}";
    }

    public static bool TryParse(string? uriText, out Account account, out string error)
    {
        account = new Account();

        if (string.IsNullOrWhiteSpace(uriText))
        {
            error = "The URI is empty.";
            return false;
        }

        if (!Uri.TryCreate(uriText.Trim(), UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            error = "Not an otpauth:// URI.";
            return false;
        }

        if (!string.Equals(uri.Host, "totp", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Only time-based (totp) accounts are supported, not '{uri.Host}'.";
            return false;
        }

        string path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        string issuerFromPath = string.Empty;
        string label = path;

        int separator = path.IndexOf(':');
        if (separator >= 0)
        {
            issuerFromPath = path[..separator].Trim();
            label = path[(separator + 1)..].Trim();
        }

        Dictionary<string, string> query = ParseQuery(uri.Query);

        account.Secret = Get(query, "secret");
        account.Issuer = Get(query, "issuer").Trim() is { Length: > 0 } queryIssuer ? queryIssuer : issuerFromPath;
        account.Label = label;
        account.Algorithm = Totp.ToName(Totp.ParseAlgorithm(Get(query, "algorithm")));

        if (int.TryParse(Get(query, "digits"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int digits))
        {
            account.Digits = digits;
        }

        if (int.TryParse(Get(query, "period"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int period))
        {
            account.Period = period;
        }

        return account.TryNormalize(out error);
    }

    private static string Get(Dictionary<string, string> query, string key) =>
        query.TryGetValue(key, out string? value) ? value : string.Empty;

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            string key = equals < 0 ? pair : pair[..equals];
            string value = equals < 0 ? string.Empty : pair[(equals + 1)..];

            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return result;
    }
}
