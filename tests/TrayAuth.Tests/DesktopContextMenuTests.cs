using Microsoft.Win32;
using TrayAuth.Core;
using Xunit;

namespace TrayAuth.Tests;

/// <summary>
/// Exercises the registry writer against a sandboxed path under HKCU\Software\TrayAuthTests,
/// never the real DesktopBackground\Shell location or the real settings key.
/// </summary>
public class DesktopContextMenuTests : IDisposable
{
    private const string ExePath = @"C:\fake path\TrayAuth.exe";

    private readonly string _sandboxRoot;
    private readonly string _shellBase;
    private readonly string _settingsPath;
    private readonly DesktopContextMenu _menu;

    public DesktopContextMenuTests()
    {
        _sandboxRoot = @"Software\TrayAuthTests\" + Guid.NewGuid().ToString("N");
        _shellBase = _sandboxRoot + @"\Shell";
        _settingsPath = _sandboxRoot + @"\Settings";
        _menu = new DesktopContextMenu(_shellBase, _settingsPath);
    }

    private static Account Sample(string issuer = "GitHub", string label = "user@example.com") => new()
    {
        Issuer = issuer,
        Label = label,
        Secret = "JBSWY3DPEHPK3PXP",
    };

    private RegistryKey? OpenMenuKey() =>
        Registry.CurrentUser.OpenSubKey(_shellBase + @"\TrayAuth");

    [Fact]
    public void Sync_WritesTheCascadingMenuStructure()
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010);
        _menu.Sync([Sample()], ExePath, at);

        using RegistryKey? root = OpenMenuKey();
        Assert.NotNull(root);

        Assert.Equal("TrayAuth codes", root!.GetValue("MUIVerb"));
        Assert.Equal(string.Empty, root.GetValue("SubCommands"));
        Assert.Contains(ExePath, (string)root.GetValue("Icon")!, StringComparison.Ordinal);

        using RegistryKey? shell = root.OpenSubKey("shell");
        Assert.NotNull(shell);

        string child = Assert.Single(shell!.GetSubKeyNames());
        Assert.StartsWith("01_", child, StringComparison.Ordinal);

        using RegistryKey childKey = shell.OpenSubKey(child)!;
        string label = (string)childKey.GetValue("MUIVerb")!;

        string expectedCode = Totp.Generate("JBSWY3DPEHPK3PXP", at: at).Grouped;
        Assert.Contains("GitHub - user@example.com", label, StringComparison.Ordinal);
        Assert.Contains(expectedCode, label, StringComparison.Ordinal);

        using RegistryKey command = childKey.OpenSubKey("command")!;
        string commandLine = (string)command.GetValue(string.Empty)!;
        Assert.StartsWith($"\"{ExePath}\" --copy ", commandLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_CommandCarriesTheAccountId()
    {
        Account account = Sample();
        _menu.Sync([account], ExePath, DateTimeOffset.UtcNow);

        using RegistryKey? root = OpenMenuKey();
        using RegistryKey shell = root!.OpenSubKey("shell")!;
        using RegistryKey child = shell.OpenSubKey(shell.GetSubKeyNames()[0])!;
        using RegistryKey command = child.OpenSubKey("command")!;

        Assert.Equal($"\"{ExePath}\" --copy {account.Id}", (string)command.GetValue(string.Empty)!);
    }

    [Fact]
    public void Sync_OrdersChildrenByVaultOrder()
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010);
        _menu.Sync([Sample("Zebra", "z"), Sample("Alpha", "a")], ExePath, at);

        using RegistryKey? root = OpenMenuKey();
        using RegistryKey shell = root!.OpenSubKey("shell")!;

        string[] names = [.. shell.GetSubKeyNames().Order()];
        Assert.Equal(2, names.Length);

        using RegistryKey first = shell.OpenSubKey(names[0])!;
        using RegistryKey second = shell.OpenSubKey(names[1])!;

        // Vault order wins, not alphabetical order of the issuer.
        Assert.Contains("Zebra", (string)first.GetValue("MUIVerb")!, StringComparison.Ordinal);
        Assert.Contains("Alpha", (string)second.GetValue("MUIVerb")!, StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_ReplacesStaleChildrenOnAccountRemoval()
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010);
        _menu.Sync([Sample("GitHub", "a"), Sample("AWS", "b")], ExePath, at);
        _menu.Sync([Sample("GitHub", "a")], ExePath, at.AddSeconds(30));

        using RegistryKey? root = OpenMenuKey();
        using RegistryKey shell = root!.OpenSubKey("shell")!;

        string name = Assert.Single(shell.GetSubKeyNames());
        using RegistryKey child = shell.OpenSubKey(name)!;
        Assert.Contains("GitHub", (string)child.GetValue("MUIVerb")!, StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_WithNoAccounts_WritesAnOpenTrayAuthPlaceholder()
    {
        _menu.Sync([], ExePath, DateTimeOffset.UtcNow);

        using RegistryKey? root = OpenMenuKey();
        using RegistryKey shell = root!.OpenSubKey("shell")!;
        using RegistryKey child = shell.OpenSubKey(Assert.Single(shell.GetSubKeyNames()))!;

        Assert.Contains("No accounts yet", (string)child.GetValue("MUIVerb")!, StringComparison.Ordinal);

        using RegistryKey command = child.OpenSubKey("command")!;
        Assert.Equal($"\"{ExePath}\"", (string)command.GetValue(string.Empty)!);
    }

    [Fact]
    public void Sync_CapsTheListAndAddsAnOverflowHint()
    {
        var accounts = new List<Account>();
        for (int i = 0; i < 20; i++)
        {
            accounts.Add(Sample("Issuer" + i, "user" + i));
        }

        _menu.Sync(accounts, ExePath, DateTimeOffset.UtcNow);

        using RegistryKey? root = OpenMenuKey();
        using RegistryKey shell = root!.OpenSubKey("shell")!;

        string[] names = shell.GetSubKeyNames();
        Assert.Equal(16, names.Length); // 15 accounts + the "... and N more" hint

        string overflowName = Assert.Single(names, n => n.EndsWith("_more", StringComparison.Ordinal));
        using RegistryKey overflow = shell.OpenSubKey(overflowName)!;
        Assert.Contains("5 more", (string)overflow.GetValue("MUIVerb")!, StringComparison.Ordinal);
    }

    [Fact]
    public void SetEnabledFalse_RemovesTheMenuAndSyncKeepsItGone()
    {
        _menu.Sync([Sample()], ExePath, DateTimeOffset.UtcNow);
        Assert.NotNull(OpenMenuKey());

        _menu.SetEnabled(false);
        Assert.Null(OpenMenuKey());

        _menu.Sync([Sample()], ExePath, DateTimeOffset.UtcNow);
        Assert.Null(OpenMenuKey());

        Assert.False(_menu.IsEnabled());
    }

    [Fact]
    public void SetEnabledTrue_RestoresOnNextSync()
    {
        _menu.SetEnabled(false);
        _menu.Sync([Sample()], ExePath, DateTimeOffset.UtcNow);
        Assert.Null(OpenMenuKey());

        _menu.SetEnabled(true);
        _menu.Sync([Sample()], ExePath, DateTimeOffset.UtcNow);
        Assert.NotNull(OpenMenuKey());
    }

    [Fact]
    public void IsEnabled_DefaultsToTrueWhenNothingIsStored()
    {
        Assert.True(_menu.IsEnabled());
    }

    [Fact]
    public void Sync_RefreshesLabelsWhenTheCodeRollsOver()
    {
        var before = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010);
        var after = DateTimeOffset.FromUnixTimeSeconds(1_700_000_040); // next 30s window

        _menu.Sync([Sample()], ExePath, before);
        using (RegistryKey? root = OpenMenuKey())
        using (RegistryKey shell = root!.OpenSubKey("shell")!)
        using (RegistryKey child = shell.OpenSubKey(shell.GetSubKeyNames()[0])!)
        {
            Assert.Contains(
                Totp.Generate("JBSWY3DPEHPK3PXP", at: before).Grouped,
                (string)child.GetValue("MUIVerb")!,
                StringComparison.Ordinal);
        }

        _menu.Sync([Sample()], ExePath, after);
        using (RegistryKey? root = OpenMenuKey())
        using (RegistryKey shell = root!.OpenSubKey("shell")!)
        using (RegistryKey child = shell.OpenSubKey(shell.GetSubKeyNames()[0])!)
        {
            Assert.Contains(
                Totp.Generate("JBSWY3DPEHPK3PXP", at: after).Grouped,
                (string)child.GetValue("MUIVerb")!,
                StringComparison.Ordinal);
        }
    }

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_sandboxRoot, throwOnMissingSubKey: false);
        }
        catch
        {
            // Test cleanup only.
        }

        GC.SuppressFinalize(this);
    }
}
