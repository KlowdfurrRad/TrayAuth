using System.Text;
using TrayAuth.Core;

namespace TrayAuth.Desktop;

/// <summary>
/// `trayauth --selftest`: proves the core on the machine it is actually running on, with no
/// GUI involved - vault crypto and file modes, RFC 6238 agreement, export/import round trip,
/// corruption quarantine. Designed to be the first command run on a fresh Ubuntu install.
/// </summary>
public static class SelfTest
{
    public static int Run()
    {
        Console.WriteLine("TrayAuth selftest");
        Console.WriteLine("-----------------");
        Console.WriteLine($"OS:       {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        Console.WriteLine($"Arch:     {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} (process) / {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture} (os)");
        Console.WriteLine($"Config:   {AppPaths.ConfigDir}");
        Console.WriteLine($"Autostart:{AppPaths.AutostartFile}");

        string tempDir = Path.Combine(Path.GetTempPath(), "trayauth-selftest-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempDir);

            bool ok = true;
            ok &= Step("RFC 6238 test vectors", CheckRfcVectors);
            ok &= Step("vault seal/unseal round trip", () => CheckVaultRoundTrip(tempDir));
            ok &= Step("vault file permissions (0600)", () => CheckPermissions(tempDir));
            ok &= Step("export -> import round trip", () => CheckExportImport(tempDir));
            ok &= Step("corrupt vault quarantined, not lost", () => CheckQuarantine(tempDir));
            ok &= Step("clipboard tool present", CheckClipboardTool);

            Console.WriteLine();
            Console.WriteLine(ok ? "SELFTEST OK" : "SELFTEST FAILED - see above");
            return ok ? 0 : 1;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Temp cleanup only.
            }
        }
    }

    private static bool Step(string name, Func<string?> check)
    {
        try
        {
            string? detail = check();
            Console.WriteLine($"  [ok]   {name}{(detail is null ? string.Empty : " - " + detail)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] {name}: {ex.Message}");
            return false;
        }
    }

    private static string? CheckRfcVectors()
    {
        byte[] sha1Key = Encoding.ASCII.GetBytes("12345678901234567890");

        Expect("94287082", Totp.Generate(sha1Key, 8, 30, OtpAlgorithm.Sha1, DateTimeOffset.FromUnixTimeSeconds(59)).Code);
        Expect("07081804", Totp.Generate(sha1Key, 8, 30, OtpAlgorithm.Sha1, DateTimeOffset.FromUnixTimeSeconds(1111111109)).Code);
        Expect("65353130", Totp.Generate(sha1Key, 8, 30, OtpAlgorithm.Sha1, DateTimeOffset.FromUnixTimeSeconds(20000000000)).Code);

        return "3 vectors";
    }

    private static string? CheckVaultRoundTrip(string tempDir)
    {
        string vaultPath = Path.Combine(tempDir, "vault.dat");
        string keyPath = Path.Combine(tempDir, "vault.key");

        var writer = new LocalVault(vaultPath, keyPath);
        writer.Load();
        writer.Add(new Account { Issuer = "GitHub", Label = "self@test", Secret = "JBSWY3DPEHPK3PXP" });
        writer.Add(new Account { Issuer = "AWS", Label = "root", Secret = "JBSWY3DPEHPK3PXP", Digits = 8, Algorithm = "SHA256" });
        writer.Save();

        string keyLocation = writer.KeyStore.Describe();

        var reader = new LocalVault(vaultPath, keyPath);
        if (reader.Load().Status != VaultLoadStatus.Loaded)
        {
            throw new InvalidOperationException("reload did not report Loaded");
        }

        if (reader.Accounts.Count != 2)
        {
            throw new InvalidOperationException($"expected 2 accounts, got {reader.Accounts.Count}");
        }

        // The reloaded account must agree with the raw generator at the same instant.
        var at = DateTimeOffset.UtcNow;
        string direct = Totp.Generate("JBSWY3DPEHPK3PXP", at: at).Code;
        Expect(direct, reader.Accounts[0].Generate(at).Code);

        // And the file on disk must not leak the secret in the clear.
        string raw = Encoding.ASCII.GetString(File.ReadAllBytes(vaultPath));
        if (raw.Contains("JBSWY3DPEHPK3PXP", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("secret visible in vault file");
        }

        return "key in " + keyLocation;
    }

    private static string? CheckPermissions(string tempDir)
    {
        if (OperatingSystem.IsWindows())
        {
            return "skipped on Windows";
        }

        string vaultPath = Path.Combine(tempDir, "vault.dat");
        UnixFileMode mode = File.GetUnixFileMode(vaultPath);

        if ((mode & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) != 0)
        {
            throw new InvalidOperationException($"vault readable by group/other: {mode}");
        }

        return mode.ToString();
    }

    private static string? CheckExportImport(string tempDir)
    {
        string vaultPath = Path.Combine(tempDir, "vault.dat");
        string keyPath = Path.Combine(tempDir, "vault.key");

        var vault = new LocalVault(vaultPath, keyPath);
        vault.Load();

        ExportResult exported = ExportService.ExportAll(vault.Accounts, tempDir);
        IReadOnlyList<Account> imported = ExportService.Import(
            Path.Combine(exported.Directory, ExportService.CombinedFileName));

        if (imported.Count != vault.Accounts.Count)
        {
            throw new InvalidOperationException($"exported {vault.Accounts.Count}, imported {imported.Count}");
        }

        var at = DateTimeOffset.UtcNow;
        for (int i = 0; i < imported.Count; i++)
        {
            Expect(vault.Accounts[i].Generate(at).Code, imported[i].Generate(at).Code);
        }

        return $"{imported.Count} accounts, QR PNGs written";
    }

    private static string? CheckQuarantine(string tempDir)
    {
        string vaultPath = Path.Combine(tempDir, "vault.dat");
        string keyPath = Path.Combine(tempDir, "vault.key");

        File.WriteAllText(vaultPath, "this is not a vault");

        var vault = new LocalVault(vaultPath, keyPath);
        VaultLoadResult result = vault.Load();

        if (result.Status != VaultLoadStatus.Recovered)
        {
            throw new InvalidOperationException($"expected Recovered, got {result.Status}");
        }

        if (result.QuarantinedPath is null || !File.Exists(result.QuarantinedPath))
        {
            throw new InvalidOperationException("corrupt vault was not preserved");
        }

        return Path.GetFileName(result.QuarantinedPath);
    }

    private static string? CheckClipboardTool()
    {
        string? tool = ClipboardHelper.ActiveTool;
        if (tool is not null)
        {
            return tool;
        }

        // A warning, not a failure: the panel clipboard still works while focused. pbcopy
        // ships with macOS, so its absence there means something stranger than a missing
        // package.
        return OperatingSystem.IsMacOS()
            ? "NONE FOUND - pbcopy should ship with macOS; tray-menu copying will be limited"
            : "NONE FOUND - install wl-clipboard for tray-menu copying";
    }

    private static void Expect(string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"expected {expected}, got {actual}");
        }
    }
}
