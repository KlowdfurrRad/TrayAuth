using Microsoft.Win32;

namespace TrayAuth.Core;

/// <summary>
/// Puts the account codes into the desktop's right-click menu, as a cascading "TrayAuth codes"
/// entry with one child per account.
///
/// The menu is a static registry structure (DesktopBackground\Shell), so the labels are only as
/// fresh as the last write - the resident app rewrites them whenever a code rolls over. The
/// click command never trusts the label: it runs "TrayAuth.exe --copy &lt;id&gt;", which computes
/// the code at the moment of the click. A stale label is therefore a cosmetic blemish, never a
/// wrong copy.
///
/// On Windows 11 these entries appear in the classic menu (Show more options / Shift+F10);
/// only packaged COM extensions can join the compact top-level menu.
/// </summary>
public sealed class DesktopContextMenu
{
    public const string DefaultShellBasePath = @"Software\Classes\DesktopBackground\Shell";
    public const string DefaultSettingsKeyPath = @"Software\TrayAuth";

    private const string VerbName = "TrayAuth";
    private const string SettingsValueName = "DesktopMenu";
    private const int MaxItems = 15;

    private readonly string _shellBasePath;
    private readonly string _settingsKeyPath;
    private string? _lastSignature;

    public DesktopContextMenu(string? shellBasePath = null, string? settingsKeyPath = null)
    {
        _shellBasePath = shellBasePath ?? DefaultShellBasePath;
        _settingsKeyPath = settingsKeyPath ?? DefaultSettingsKeyPath;
    }

    private string MenuKeyPath => _shellBasePath + @"\" + VerbName;

    /// <summary>On by default: absence of the setting means enabled.</summary>
    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_settingsKeyPath);
            return key?.GetValue(SettingsValueName) is not int value || value != 0;
        }
        catch
        {
            return true;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(_settingsKeyPath);
            key.SetValue(SettingsValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch
        {
            // The Sync/Remove below still applies the visible effect for this session.
        }

        if (!enabled)
        {
            Remove();
        }

        _lastSignature = null;
    }

    /// <summary>
    /// Rewrites the menu if anything user-visible changed; a cheap string comparison otherwise.
    /// Safe to call every second from a timer.
    /// </summary>
    public void Sync(IReadOnlyList<Account> accounts, string exePath, DateTimeOffset? at = null)
    {
        if (!IsEnabled())
        {
            if (_lastSignature != "disabled")
            {
                Remove();
                _lastSignature = "disabled";
            }

            return;
        }

        DateTimeOffset now = at ?? DateTimeOffset.UtcNow;
        var entries = new List<(string Id, string Label)>();

        foreach (Account account in accounts)
        {
            if (entries.Count >= MaxItems)
            {
                break;
            }

            try
            {
                TotpCode code = account.Generate(now);
                entries.Add((account.Id, $"{Truncate(account.FullName, 40)}   {code.Grouped}"));
            }
            catch
            {
                // A broken entry shows its error in the panel; the desktop menu just skips it.
            }
        }

        string signature = exePath + "|" + string.Join("|", entries.Select(e => e.Id + "=" + e.Label))
            + (accounts.Count > MaxItems ? "|more:" + (accounts.Count - MaxItems) : string.Empty);

        if (signature == _lastSignature)
        {
            return;
        }

        try
        {
            Write(entries, accounts.Count, exePath);
            _lastSignature = signature;
        }
        catch
        {
            // Registry unavailable (policy, corruption): the tray menu still works, so stay quiet
            // and retry on the next change.
        }
    }

    public void Remove()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(MenuKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // Nothing useful to do.
        }

        _lastSignature = null;
    }

    private void Write(List<(string Id, string Label)> entries, int totalAccounts, string exePath)
    {
        using RegistryKey root = Registry.CurrentUser.CreateSubKey(MenuKeyPath);

        root.SetValue("MUIVerb", "TrayAuth codes");
        root.SetValue("Icon", "\"" + exePath + "\"");
        // An empty SubCommands makes Explorer read the children from our shell subkey - the
        // per-user cascading-menu pattern, no admin rights involved.
        root.SetValue("SubCommands", string.Empty);

        root.DeleteSubKeyTree("shell", throwOnMissingSubKey: false);
        using RegistryKey shell = root.CreateSubKey("shell");

        if (entries.Count == 0)
        {
            WriteChild(shell, "01_none", "No accounts yet - open TrayAuth", exePath, $"\"{exePath}\"");
            return;
        }

        int index = 1;
        foreach ((string id, string label) in entries)
        {
            // The numeric prefix pins the order: Explorer sorts children by key name.
            WriteChild(shell, $"{index:D2}_{id}", label, exePath, $"\"{exePath}\" --copy {id}");
            index++;
        }

        if (totalAccounts > entries.Count)
        {
            WriteChild(
                shell,
                $"{index:D2}_more",
                $"... and {totalAccounts - entries.Count} more - open TrayAuth",
                exePath,
                $"\"{exePath}\"");
        }
    }

    private static void WriteChild(RegistryKey shell, string keyName, string label, string exePath, string command)
    {
        using RegistryKey child = shell.CreateSubKey(keyName);
        child.SetValue("MUIVerb", label);
        child.SetValue("Icon", "\"" + exePath + "\"");

        using RegistryKey commandKey = child.CreateSubKey("command");
        commandKey.SetValue(string.Empty, command);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "...";
}
