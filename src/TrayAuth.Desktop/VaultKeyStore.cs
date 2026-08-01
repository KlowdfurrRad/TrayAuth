using System.Security.Cryptography;

namespace TrayAuth.Desktop;

public enum KeyStorage
{
    /// <summary>macOS Keychain, via the built-in `security` tool.</summary>
    MacKeychain,

    /// <summary>Secret Service (GNOME Keyring / KWallet), via `secret-tool`.</summary>
    SecretService,

    /// <summary>A 0600 file beside the vault - weaker, used when no keyring is reachable.</summary>
    KeyFile,
}

/// <summary>
/// Holds the AES key that seals the vault, in the strongest place each OS offers: the macOS
/// Keychain, the Secret Service keyring on Linux, and - only if neither answers - a 0600 file
/// next to the vault.
///
/// This is the DPAPI equivalent: on Windows the OS binds the key to your login for us, and on
/// these platforms the keyring does the same job through a helper tool.
/// </summary>
public sealed class VaultKeyStore
{
    private const int KeySize = 32;

    // Keychain / Secret Service lookup attributes.
    private const string Account = "trayauth";
    private const string Service = "trayauth-vault-key";

    private readonly string _keyFilePath;
    private byte[]? _key;

    public VaultKeyStore(string keyFilePath)
    {
        _keyFilePath = keyFilePath;
    }

    /// <summary>Where the key actually came from, once <see cref="GetOrCreate"/> has run.</summary>
    public KeyStorage Storage { get; private set; } = KeyStorage.KeyFile;

    public bool UsedKeyFileFallback => Storage == KeyStorage.KeyFile;

    public byte[] GetOrCreate()
    {
        if (_key is not null)
        {
            return _key;
        }

        // 1. An existing key in the platform keyring.
        if (TryReadFromKeyring(out byte[] fromKeyring))
        {
            _key = fromKeyring;
            return _key;
        }

        // 2. An existing key file, from a previous run that had no keyring.
        if (File.Exists(_keyFilePath) && TryParse(File.ReadAllText(_keyFilePath), out byte[] fromFile))
        {
            Storage = KeyStorage.KeyFile;
            _key = fromFile;
            return _key;
        }

        // 3. First run: mint one and put it in the best place that will accept it.
        byte[] fresh = RandomNumberGenerator.GetBytes(KeySize);
        string hex = Convert.ToHexString(fresh);

        if (TryWriteToKeyring(hex) && TryReadFromKeyring(out byte[] verified)
            && CryptographicOperations.FixedTimeEquals(verified, fresh))
        {
            _key = fresh;
            return _key;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_keyFilePath)!);
        File.WriteAllText(_keyFilePath, hex);
        Core.FileProtection.HardenFile(_keyFilePath);

        Storage = KeyStorage.KeyFile;
        _key = fresh;
        return _key;
    }

    /// <summary>Removes the stored key. Used by uninstall paths; the vault becomes unreadable.</summary>
    public void Delete()
    {
        if (OperatingSystem.IsMacOS())
        {
            ProcessRunner.Capture("security", ["delete-generic-password", "-a", Account, "-s", Service]);
        }
        else
        {
            ProcessRunner.Capture("secret-tool", ["clear", "application", Account, "type", "vault-key"]);
        }

        try
        {
            File.Delete(_keyFilePath);
        }
        catch
        {
            // Nothing more to do.
        }
    }

    private bool TryReadFromKeyring(out byte[] key)
    {
        string? stored = OperatingSystem.IsMacOS()
            // -w prints just the password to stdout.
            ? ProcessRunner.Capture("security", ["find-generic-password", "-a", Account, "-s", Service, "-w"])
            : ProcessRunner.Capture("secret-tool", ["lookup", "application", Account, "type", "vault-key"]);

        if (TryParse(stored, out key))
        {
            Storage = OperatingSystem.IsMacOS() ? KeyStorage.MacKeychain : KeyStorage.SecretService;
            return true;
        }

        return false;
    }

    private static bool TryWriteToKeyring(string hex)
    {
        if (OperatingSystem.IsMacOS())
        {
            // -U updates in place if the item already exists, instead of erroring.
            return ProcessRunner.Capture(
                "security",
                ["add-generic-password", "-a", Account, "-s", Service, "-w", hex, "-U"]) is not null;
        }

        // secret-tool reads the secret from stdin, which keeps it off the process command line.
        return ProcessRunner.Capture(
            "secret-tool",
            ["store", "--label=TrayAuth vault key", "application", Account, "type", "vault-key"],
            hex) is not null;
    }

    private static bool TryParse(string? hex, out byte[] key)
    {
        key = [];

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            byte[] parsed = Convert.FromHexString(hex.Trim());
            if (parsed.Length != KeySize)
            {
                return false;
            }

            key = parsed;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public string Describe() => Storage switch
    {
        KeyStorage.MacKeychain => "macOS Keychain",
        KeyStorage.SecretService => "keyring (Secret Service)",
        _ => "key file (no keyring available)",
    };
}
