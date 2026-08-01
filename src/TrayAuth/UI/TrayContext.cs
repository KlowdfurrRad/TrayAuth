using TrayAuth.Core;
using TrayAuth.Interop;

namespace TrayAuth.UI;

/// <summary>
/// The application itself: a tray icon and the flyout it opens. There is no main window, so the
/// process lives for as long as this context does.
/// </summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Vault _vault = new();
    private readonly ClipboardService _clipboard = new();
    private readonly PanelForm _panel;
    private readonly HotKey _hotKey = new();
    private readonly ContextMenuStrip _menu = new();

    private ToolStripMenuItem _startupItem = null!;

    /// <summary>Accounts shown at the top of the tray menu; more than this and the panel is the tool.</summary>
    private const int MaxMenuCodes = 15;

    private readonly List<ToolStripItem> _codeItems = [];
    private readonly System.Windows.Forms.Timer _menuRefresh = new() { Interval = 1000 };

    private readonly DesktopContextMenu _desktopMenu = new();
    private readonly System.Windows.Forms.Timer _desktopSync = new() { Interval = 1000 };
    private ToolStripMenuItem _desktopMenuItem = null!;

    public TrayContext(uint showPanelMessage)
    {
        _panel = new PanelForm(_vault, _clipboard)
        {
            ShowPanelMessage = showPanelMessage,
        };

        _panel.AddRequested += (_, _) => _panel.AddAccount();
        _panel.MenuRequested += (_, _) => ShowMenuAtCursor();

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            Visible = true,
            Text = "TrayAuth - authenticator codes",
        };

        BuildMenu();
        _notifyIcon.ContextMenuStrip = _menu;
        _notifyIcon.MouseClick += OnTrayClick;

        LoadVault();
        RegisterHotKey();

        // Keeps the desktop right-click menu's labels truthful: recomputes a cheap signature
        // every second and rewrites the registry only when a code actually rolled over or the
        // account list changed.
        _desktopSync.Tick += (_, _) => SyncDesktopMenu();
        _desktopSync.Start();
        SyncDesktopMenu();
    }

    private void SyncDesktopMenu()
    {
        try
        {
            _desktopMenu.Sync(_vault.Accounts, StartupRegistration.CurrentExecutablePath);
        }
        catch
        {
            // Never let a registry hiccup take the tray app down.
        }
    }

    private void BuildMenu()
    {
        _menu.RenderMode = ToolStripRenderMode.System;

        _menu.Items.Add("Show codes", null, (_, _) => _panel.ShowPanel());
        _menu.Items.Add("Add account...", null, (_, _) => ShowThenRun(_panel.AddAccount));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Export all accounts...", null, (_, _) => ShowThenRun(_panel.ExportAll));

        var import = new ToolStripMenuItem("Import");
        import.DropDownItems.Add("From export file...", null, (_, _) => ShowThenRun(_panel.ImportAccounts));
        import.DropDownItems.Add("From QR image...", null, (_, _) => ShowThenRun(_panel.ImportFromQrImages));
        import.DropDownItems.Add("Scan screen for QR code", null, (_, _) => ScanScreenForQr());
        _menu.Items.Add(import);

        _menu.Items.Add(new ToolStripSeparator());

        _startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupRegistration.IsEnabled(),
        };
        _startupItem.Click += OnToggleStartup;
        _menu.Items.Add(_startupItem);

        _desktopMenuItem = new ToolStripMenuItem("Codes in desktop right-click menu")
        {
            CheckOnClick = true,
            Checked = _desktopMenu.IsEnabled(),
        };
        _desktopMenuItem.Click += (_, _) =>
        {
            _desktopMenu.SetEnabled(_desktopMenuItem.Checked);
            SyncDesktopMenu();
        };
        _menu.Items.Add(_desktopMenuItem);

        _menu.Items.Add("About TrayAuth", null, (_, _) => ShowAbout());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _menu.Opening += OnMenuOpening;
        _menu.Closed += (_, _) => _menuRefresh.Stop();
        _menuRefresh.Tick += (_, _) => RefreshMenuCodes();
    }

    /// <summary>
    /// The top of the tray menu lists every account with its live code, so a code can be copied
    /// without opening the panel at all. Items are rebuilt on every open (cheap, and always
    /// current); while the menu stays open a one-second timer keeps the codes truthful across
    /// the 30-second boundary.
    /// </summary>
    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        ClearCodeItems();

        IReadOnlyList<Account> accounts = _vault.Accounts;
        int shown = Math.Min(accounts.Count, MaxMenuCodes);
        int index = 0;

        for (int i = 0; i < shown; i++)
        {
            Account account = accounts[i];

            TotpCode code;
            try
            {
                code = account.Generate();
            }
            catch
            {
                continue; // A broken entry shows its error in the panel; the menu just skips it.
            }

            var item = new ToolStripMenuItem(Truncate(account.FullName, 38))
            {
                ShortcutKeyDisplayString = FormatMenuCode(code),
                Tag = account.Id,
            };
            item.Click += OnMenuCopyClick;

            _codeItems.Add(item);
            _menu.Items.Insert(index++, item);
        }

        if (accounts.Count > shown)
        {
            var overflow = new ToolStripMenuItem($"... and {accounts.Count - shown} more - open the panel")
            {
                Enabled = false,
            };

            _codeItems.Add(overflow);
            _menu.Items.Insert(index++, overflow);
        }

        if (index > 0)
        {
            var separator = new ToolStripSeparator();
            _codeItems.Add(separator);
            _menu.Items.Insert(index, separator);
            _menuRefresh.Start();
        }
    }

    private void RefreshMenuCodes()
    {
        if (!_menu.Visible)
        {
            _menuRefresh.Stop();
            return;
        }

        foreach (ToolStripItem entry in _codeItems)
        {
            if (entry is not ToolStripMenuItem item || item.Tag is not string id)
            {
                continue;
            }

            Account? account = _vault.Find(id);
            if (account is null)
            {
                continue;
            }

            try
            {
                item.ShortcutKeyDisplayString = FormatMenuCode(account.Generate());
            }
            catch
            {
                // Leave the last shown value; the entry is broken and the panel says why.
            }
        }
    }

    private void OnMenuCopyClick(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem item || item.Tag is not string id)
        {
            return;
        }

        Account? account = _vault.Find(id);
        if (account is null)
        {
            return;
        }

        try
        {
            // Recomputed at click time: the menu may have sat open past the code shown when it
            // was built, and stale codes are worse than no feature.
            TotpCode code = account.Generate();
            _clipboard.TryCopy(code.Code);
        }
        catch
        {
            // Nothing sensible to copy.
        }
    }

    private void ClearCodeItems()
    {
        foreach (ToolStripItem item in _codeItems)
        {
            _menu.Items.Remove(item);
            item.Dispose();
        }

        _codeItems.Clear();
    }

    /// <summary>
    /// Hides our always-on-top panel (which would otherwise photobomb its own capture), waits a
    /// beat for it and this menu to leave the screen, then scans every monitor for QR codes.
    /// </summary>
    private void ScanScreenForQr()
    {
        _panel.HidePanelImmediate();

        var delay = new System.Windows.Forms.Timer { Interval = 400 };
        delay.Tick += (_, _) =>
        {
            delay.Stop();
            delay.Dispose();

            IReadOnlyList<string> texts;
            try
            {
                texts = QrDecoder.DecodeAllScreens();
            }
            catch
            {
                texts = [];
            }

            _panel.ShowPanel();
            _panel.HandleQrTexts(texts);
        };

        delay.Start();
    }

    private static string FormatMenuCode(TotpCode code) => $"{code.Grouped}  {code.SecondsRemaining,2}s";

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "...";

    private void LoadVault()
    {
        VaultLoadResult result = _vault.Load();

        if (result.Status == VaultLoadStatus.Recovered)
        {
            // Surfaced rather than swallowed: the accounts are gone from the app's point of view,
            // and the user needs to know their backup is now the only copy.
            _notifyIcon.ShowBalloonTip(
                10_000,
                "TrayAuth could not read its vault",
                $"The existing vault was set aside as {Path.GetFileName(result.QuarantinedPath)} and TrayAuth started empty. Use Import to restore from an export.",
                ToolTipIcon.Warning);
        }

        _panel.ReloadAccounts();
    }

    private void RegisterHotKey()
    {
        _hotKey.Register(
            _panel.Handle,
            HotKeyModifiers.Control | HotKeyModifiers.Alt,
            Keys.A);
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _panel.TogglePanel();
        }
    }

    private void ShowMenuAtCursor() => _menu.Show(Cursor.Position);

    /// <summary>
    /// Menu actions open modal dialogs, which need the panel present as their owner - and the panel
    /// is the natural place for the result to appear anyway.
    /// </summary>
    private void ShowThenRun(Action action)
    {
        if (!_panel.IsShown)
        {
            _panel.ShowPanel();
        }

        action();
    }

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        bool desired = _startupItem.Checked;

        if (StartupRegistration.SetEnabled(desired))
        {
            return;
        }

        _startupItem.Checked = !desired;
        _notifyIcon.ShowBalloonTip(
            5_000,
            "TrayAuth",
            "The start-with-Windows setting could not be changed.",
            ToolTipIcon.Warning);
    }

    private void ShowAbout()
    {
        string hotkey = _hotKey.IsRegistered
            ? _hotKey.Describe()
            : $"{_hotKey.Describe()} (unavailable - another app has it)";

        _panel.ShowMessage(
            $"""
            TrayAuth 1.0

            Authenticator codes in the Windows tray.

            Show panel:   click the tray icon, or {hotkey}
            Copy a code:  click it in the panel, or right-click the tray icon
                          - the clipboard clears itself after 20 seconds
            Vault:        {_vault.FilePath}
                          encrypted with your Windows account (DPAPI)

            A DPAPI vault cannot be read by any other Windows profile, including
            a reinstalled one. Export your accounts so you have a way back.
            """,
            "About TrayAuth",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _desktopSync.Dispose();
            _menuRefresh.Dispose();
            _hotKey.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _panel.Dispose();
            _clipboard.Dispose();
        }

        base.Dispose(disposing);
    }
}
