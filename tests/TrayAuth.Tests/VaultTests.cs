using System.Text;
using TrayAuth.Core;
using Xunit;

namespace TrayAuth.Tests;

public class VaultTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public VaultTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "TrayAuthTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "vault.dat");
    }

    private static Account SampleAccount(string issuer = "GitHub", string label = "user@example.com") => new()
    {
        Issuer = issuer,
        Label = label,
        Secret = "JBSWY3DPEHPK3PXP",
    };

    [Fact]
    public void Load_OnAFreshMachine_ReportsNewRatherThanFailing()
    {
        var vault = new Vault(_path);
        VaultLoadResult result = vault.Load();

        Assert.Equal(VaultLoadStatus.New, result.Status);
        Assert.Empty(vault.Accounts);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var saved = new Account
        {
            Issuer = "AWS",
            Label = "root",
            Secret = "JBSWY3DPEHPK3PXP",
            Digits = 8,
            Period = 60,
            Algorithm = "SHA256",
        };

        var writer = new Vault(_path);
        writer.Load();
        writer.Add(saved);
        writer.Save();

        var reader = new Vault(_path);
        Assert.Equal(VaultLoadStatus.Loaded, reader.Load().Status);

        Account loaded = Assert.Single(reader.Accounts);
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal("AWS", loaded.Issuer);
        Assert.Equal("root", loaded.Label);
        Assert.Equal("JBSWY3DPEHPK3PXP", loaded.Secret);
        Assert.Equal(8, loaded.Digits);
        Assert.Equal(60, loaded.Period);
        Assert.Equal("SHA256", loaded.Algorithm);
    }

    [Fact]
    public void SavedFile_DoesNotContainTheSecretInClear()
    {
        var vault = new Vault(_path);
        vault.Load();
        vault.Add(SampleAccount());
        vault.Save();

        byte[] raw = File.ReadAllBytes(_path);
        string asText = Encoding.ASCII.GetString(raw);

        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", asText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GitHub", asText, StringComparison.OrdinalIgnoreCase);

        // The header is the only thing meant to be readable.
        Assert.StartsWith("TRAYAUTH", asText, StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptVault_IsSetAsideRatherThanDeleted()
    {
        File.WriteAllText(_path, "this is not a vault");

        var vault = new Vault(_path);
        VaultLoadResult result = vault.Load();

        Assert.Equal(VaultLoadStatus.Recovered, result.Status);
        Assert.Empty(vault.Accounts);
        Assert.NotNull(result.QuarantinedPath);
        Assert.True(File.Exists(result.QuarantinedPath));
        Assert.False(File.Exists(_path));

        // The original bytes survive, in case the failure turns out to be recoverable.
        Assert.Equal("this is not a vault", File.ReadAllText(result.QuarantinedPath!));
    }

    [Fact]
    public void TamperedVault_FailsToUnsealRatherThanReturningGarbage()
    {
        var vault = new Vault(_path);
        vault.Load();
        vault.Add(SampleAccount());
        vault.Save();

        byte[] raw = File.ReadAllBytes(_path);
        raw[^1] ^= 0xFF;
        File.WriteAllBytes(_path, raw);

        var reader = new Vault(_path);
        Assert.Equal(VaultLoadStatus.Recovered, reader.Load().Status);
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        var vault = new Vault(_path);
        vault.Load();
        vault.Add(SampleAccount());
        vault.Save();
        vault.Save();

        Assert.False(File.Exists(_path + ".tmp"));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void UpdateAndRemove_ActOnTheMatchingEntry()
    {
        var vault = new Vault(_path);
        vault.Load();

        Account first = SampleAccount("GitHub", "a@example.com");
        Account second = SampleAccount("AWS", "b@example.com");
        vault.Add(first);
        vault.Add(second);

        Account edited = first.Clone();
        edited.Label = "changed@example.com";
        Assert.True(vault.Update(edited));
        Assert.Equal("changed@example.com", vault.Find(first.Id)!.Label);

        Assert.True(vault.Remove(second.Id));
        Assert.False(vault.Remove(second.Id));
        Assert.Single(vault.Accounts);
    }

    [Fact]
    public void Load_SkipsEntriesThatCouldNeverGenerateACode()
    {
        var vault = new Vault(_path);
        vault.Load();
        vault.Add(SampleAccount());
        vault.Add(new Account { Issuer = "Broken", Label = "x", Secret = "!!!not base32!!!" });
        vault.Save();

        var reader = new Vault(_path);
        reader.Load();

        // One bad record must not cost the user the rest of the vault.
        Account survivor = Assert.Single(reader.Accounts);
        Assert.Equal("GitHub", survivor.Issuer);
    }

    [Fact]
    public void FindMatch_IgnoresCaseSoImportsDoNotDuplicate()
    {
        var vault = new Vault(_path);
        vault.Load();
        vault.Add(SampleAccount("GitHub", "user@example.com"));

        Assert.NotNull(vault.FindMatch(SampleAccount("github", "USER@example.com")));
        Assert.Null(vault.FindMatch(SampleAccount("GitLab", "user@example.com")));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Temp cleanup only.
        }

        GC.SuppressFinalize(this);
    }
}
