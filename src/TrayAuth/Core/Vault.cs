using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrayAuth.Core;

/// <summary>
/// Account storage, sealed at rest with Windows DPAPI under the current user account.
///
/// A DPAPI blob is bound to the Windows user profile that wrote it: copying vault.dat to another
/// machine or another account yields nothing. That is the protection we want, and it is also why
/// <see cref="ExportService"/> exists — lose the profile and this file is gone with it.
/// </summary>
public sealed class Vault
{
    private const string MagicText = "TRAYAUTH";
    private const byte FormatVersion = 1;

    // Extra entropy mixed into DPAPI, so another program running as this user cannot unseal the
    // file by simply handing the bytes to CryptUnprotectData.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TrayAuth/vault/v1");
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes(MagicText);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly List<Account> _accounts = [];

    public Vault(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(DefaultDirectory, "vault.dat");
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TrayAuth");

    public string FilePath { get; }

    public IReadOnlyList<Account> Accounts => _accounts;

    public VaultLoadResult Load()
    {
        _accounts.Clear();

        if (!File.Exists(FilePath))
        {
            return new VaultLoadResult(VaultLoadStatus.New, null, null);
        }

        try
        {
            byte[] raw = File.ReadAllBytes(FilePath);
            byte[] json = Unseal(raw);

            VaultDocument? document = JsonSerializer.Deserialize<VaultDocument>(json, SerializerOptions);
            if (document is null)
            {
                throw new InvalidDataException("The vault contained no data.");
            }

            foreach (Account account in document.Accounts)
            {
                // Drop entries that could never generate a code rather than letting a single bad
                // record take the whole vault down.
                if (account.TryNormalize(out _))
                {
                    _accounts.Add(account);
                }
            }

            return new VaultLoadResult(VaultLoadStatus.Loaded, null, null);
        }
        catch (Exception ex)
        {
            string quarantined = Quarantine();
            return new VaultLoadResult(VaultLoadStatus.Recovered, quarantined, ex.Message);
        }
    }

    public void Save()
    {
        string directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        FileProtection.HardenDirectory(directory);

        var document = new VaultDocument { Version = 1, Accounts = _accounts };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        byte[] sealedBytes = Seal(json);
        CryptographicOperations.ZeroMemory(json);

        // Write to a sibling temp file and swap it in, so an interrupted save can never leave a
        // half-written vault behind.
        string temp = FilePath + ".tmp";
        File.WriteAllBytes(temp, sealedBytes);
        File.Move(temp, FilePath, overwrite: true);
    }

    public void Add(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _accounts.Add(account);
    }

    public bool Update(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        int index = _accounts.FindIndex(a => a.Id == account.Id);
        if (index < 0)
        {
            return false;
        }

        _accounts[index] = account;
        return true;
    }

    public bool Remove(string id)
    {
        int index = _accounts.FindIndex(a => a.Id == id);
        if (index < 0)
        {
            return false;
        }

        _accounts.RemoveAt(index);
        return true;
    }

    public Account? Find(string id) => _accounts.Find(a => a.Id == id);

    public Account? FindMatch(Account other) => _accounts.Find(a => a.Matches(other));

    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _accounts.Count || toIndex < 0 || toIndex >= _accounts.Count)
        {
            return;
        }

        Account account = _accounts[fromIndex];
        _accounts.RemoveAt(fromIndex);
        _accounts.Insert(toIndex, account);
    }

    private static byte[] Seal(byte[] plaintext)
    {
        byte[] protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

        byte[] output = new byte[Magic.Length + 1 + protectedBytes.Length];
        Magic.CopyTo(output, 0);
        output[Magic.Length] = FormatVersion;
        protectedBytes.CopyTo(output, Magic.Length + 1);
        return output;
    }

    private static byte[] Unseal(byte[] raw)
    {
        if (raw.Length <= Magic.Length + 1)
        {
            throw new InvalidDataException("The vault file is truncated.");
        }

        for (int i = 0; i < Magic.Length; i++)
        {
            if (raw[i] != Magic[i])
            {
                throw new InvalidDataException("The vault file header is not recognised.");
            }
        }

        if (raw[Magic.Length] != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported vault format version {raw[Magic.Length]}.");
        }

        byte[] payload = new byte[raw.Length - Magic.Length - 1];
        Array.Copy(raw, Magic.Length + 1, payload, 0, payload.Length);

        return ProtectedData.Unprotect(payload, Entropy, DataProtectionScope.CurrentUser);
    }

    /// <summary>
    /// Moves an unreadable vault aside instead of deleting it. If the failure turns out to be
    /// recoverable — a restored profile, say — the bytes are still there.
    /// </summary>
    private string Quarantine()
    {
        string target = FilePath + ".bad";
        int suffix = 1;
        while (File.Exists(target))
        {
            target = $"{FilePath}.bad{suffix++}";
        }

        try
        {
            File.Move(FilePath, target);
            return target;
        }
        catch
        {
            return FilePath;
        }
    }

}
