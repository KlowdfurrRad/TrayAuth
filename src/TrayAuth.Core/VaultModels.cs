using System.Text.Json.Serialization;

namespace TrayAuth.Core;

/// <summary>
/// The JSON document every vault implementation seals: DPAPI on Windows, AES-GCM on Linux.
/// One shape, so an export from either platform restores on the other.
/// </summary>
public sealed class VaultDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("accounts")]
    public List<Account> Accounts { get; set; } = [];
}

public enum VaultLoadStatus
{
    /// <summary>No vault file yet - first run.</summary>
    New,

    Loaded,

    /// <summary>The file existed but could not be unsealed or parsed; it was set aside.</summary>
    Recovered,
}

public sealed record VaultLoadResult(VaultLoadStatus Status, string? QuarantinedPath, string? Error);
