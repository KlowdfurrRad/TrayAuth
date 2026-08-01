using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrayAuth.Core;

namespace TrayAuth.Linux;

/// <summary>
/// The Linux vault: the same <see cref="VaultDocument"/> JSON as Windows, sealed with
/// AES-256-GCM instead of DPAPI.
///
/// The key lives in the GNOME keyring when possible (via the libsecret CLI, secret-tool),
/// which gives the same trust model as DPAPI: unlocked by your login, unreadable to other
/// accounts. Without secret-tool it falls back to a 0600 key file next to the vault - honest
/// but weaker, so <see cref="UsedKeyFileFallback"/> lets the UI say so once.
/// </summary>
public sealed class LinuxVault
{
    private const string MagicText = "TRAYAUTHL";
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes(MagicText);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly List<Account> _accounts = [];
    private byte[]? _key;

    public LinuxVault(string? filePath = null, string? keyFilePath = null)
    {
        FilePath = filePath ?? LinuxPaths.VaultFile;
        KeyFilePath = keyFilePath ?? LinuxPaths.KeyFile;
    }

    public string FilePath { get; }

    public string KeyFilePath { get; }

    public bool UsedKeyFileFallback { get; private set; }

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
            byte[] json = Unseal(raw, GetOrCreateKey());

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
        byte[] sealedBytes = Seal(json, GetOrCreateKey());
        CryptographicOperations.ZeroMemory(json);

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
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    // ---- key management -----------------------------------------------------------------

    private byte[] GetOrCreateKey()
    {
        if (_key is not null)
        {
            return _key;
        }

        // 1. The keyring, via secret-tool. Attributes identify the item; the label is cosmetic.
        string? stored = RunCapture("secret-tool", ["lookup", "application", "trayauth", "type", "vault-key"], null);
        if (TryParseKey(stored, out byte[] fromKeyring))
        {
            _key = fromKeyring;
            return _key;
        }

        // 2. The key file, if a previous run fell back to it.
        if (File.Exists(KeyFilePath)
            && TryParseKey(File.ReadAllText(KeyFilePath), out byte[] fromFile))
        {
            UsedKeyFileFallback = true;
            _key = fromFile;
            return _key;
        }

        // 3. First run: mint a key and store it in the keyring if we can, else the file.
        byte[] fresh = RandomNumberGenerator.GetBytes(KeySize);
        string hex = Convert.ToHexString(fresh);

        RunCapture(
            "secret-tool",
            ["store", "--label=TrayAuth vault key", "application", "trayauth", "type", "vault-key"],
            hex);

        string? verify = RunCapture("secret-tool", ["lookup", "application", "trayauth", "type", "vault-key"], null);
        if (TryParseKey(verify, out byte[] verified) && verified.AsSpan().SequenceEqual(fresh))
        {
            _key = fresh;
            return _key;
        }

        // Keyring unavailable (no secret-tool, no daemon, locked): key file, 0600.
        Directory.CreateDirectory(Path.GetDirectoryName(KeyFilePath)!);
        File.WriteAllText(KeyFilePath, hex);
        FileProtection.HardenFile(KeyFilePath);
        UsedKeyFileFallback = true;
        _key = fresh;
        return _key;
    }

    private static bool TryParseKey(string? hex, out byte[] key)
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

    private static string? RunCapture(string fileName, string[] args, string? stdin)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null,
                UseShellExecute = false,
            };

            foreach (string arg in args)
            {
                info.ArgumentList.Add(arg);
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }

            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { }
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            // Tool not installed, or no session bus. Callers treat null as "unavailable".
            return null;
        }
    }

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
