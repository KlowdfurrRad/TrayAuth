using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrayAuth.Core;

namespace TrayAuth.Desktop;

/// <summary>
/// The vault for Linux and macOS: the same <see cref="VaultDocument"/> JSON the Windows app
/// writes, sealed with AES-256-GCM instead of DPAPI, with the key held by
/// <see cref="VaultKeyStore"/> in the platform keyring.
/// </summary>
public sealed class LocalVault
{
    private const string MagicText = "TRAYAUTHL";
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes(MagicText);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly List<Account> _accounts = [];
    private readonly VaultKeyStore _keyStore;

    public LocalVault(string? filePath = null, string? keyFilePath = null)
    {
        FilePath = filePath ?? AppPaths.VaultFile;
        _keyStore = new VaultKeyStore(keyFilePath ?? AppPaths.KeyFile);
    }

    public string FilePath { get; }

    public VaultKeyStore KeyStore => _keyStore;

    public bool UsedKeyFileFallback => _keyStore.UsedKeyFileFallback;

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
            byte[] json = Unseal(raw, _keyStore.GetOrCreate());

            VaultDocument? document = JsonSerializer.Deserialize<VaultDocument>(json, SerializerOptions);
            CryptographicOperations.ZeroMemory(json);

            if (document is null)
            {
                throw new InvalidDataException("The vault contained no data.");
            }

            foreach (Account account in document.Accounts)
            {
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
        byte[] sealedBytes = Seal(json, _keyStore.GetOrCreate());
        CryptographicOperations.ZeroMemory(json);

        // Write beside the target and swap, so an interrupted save cannot truncate the vault.
        string temp = FilePath + ".tmp";
        File.WriteAllBytes(temp, sealedBytes);
        FileProtection.HardenFile(temp);
        File.Move(temp, FilePath, overwrite: true);
    }

    public void Add(Account account) => _accounts.Add(account);

    public bool Update(Account account)
    {
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

    // ---- crypto -------------------------------------------------------------------------

    private static byte[] Seal(byte[] plaintext, byte[] key)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] output = new byte[Magic.Length + 1 + NonceSize + TagSize + ciphertext.Length];
        int offset = 0;

        Magic.CopyTo(output, offset);
        offset += Magic.Length;
        output[offset++] = FormatVersion;
        nonce.CopyTo(output, offset);
        offset += NonceSize;
        tag.CopyTo(output, offset);
        offset += TagSize;
        ciphertext.CopyTo(output, offset);

        return output;
    }

    private static byte[] Unseal(byte[] raw, byte[] key)
    {
        int headerSize = Magic.Length + 1 + NonceSize + TagSize;
        if (raw.Length < headerSize)
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

        int offset = Magic.Length + 1;
        byte[] nonce = raw[offset..(offset + NonceSize)];
        offset += NonceSize;
        byte[] tag = raw[offset..(offset + TagSize)];
        offset += TagSize;
        byte[] ciphertext = raw[offset..];

        byte[] plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);

        // Throws if the tag does not verify - tampering and a wrong key look the same here,
        // and both end up quarantined rather than silently producing garbage accounts.
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    /// <summary>Sets an unreadable vault aside instead of deleting it; the bytes may still matter.</summary>
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
