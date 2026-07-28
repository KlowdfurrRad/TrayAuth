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
    }

    private void BuildMenu()
    {
        _menu.RenderMode = ToolStripRenderMode.System;

        _menu.Items.Add("Show codes", null, (_, _) => _panel.ShowPanel());
        _menu.Items.Add("Add account...", null, (_, _) => ShowThenRun(_panel.AddAccount));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Export all accounts...", null, (_, _) => ShowThenRun(_panel.ExportAll));
        _menu.Items.Add("Import...", null, (_, _) => ShowThenRun(_panel.ImportAccounts));
        _menu.Items.Add(new ToolStripSeparator());

        _startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupRegistration.IsEnabled(),
        };
        _startupItem.Click += OnToggleStartup;
        _menu.Items.Add(_startupItem);

        _menu.Items.Add("About TrayAuth", null, (_, _) => ShowAbout());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitApplication());
    }

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
            Copy a code:  click it - the clipboard clears after 20 seconds
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
