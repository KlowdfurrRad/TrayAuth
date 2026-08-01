using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using TrayAuth.Core;
using TrayAuth.Desktop.UI;

namespace TrayAuth.Desktop;

/// <summary>
/// The application: a StatusNotifier tray icon whose menu carries live codes, plus the panel.
/// There is no main window - the tray owns the process lifetime.
/// </summary>
public sealed class App : Application
{
    private readonly LocalVault _vault = new();
    private PanelWindow? _panel;
    private TrayIcon? _tray;
    private NativeMenu? _menu;
    private DispatcherTimer? _menuTimer;
    private string? _menuSignature;

    private readonly List<NativeMenuItem> _codeItems = [];

    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            VaultLoadResult load = _vault.Load();
            _panel = new PanelWindow(_vault, OnVaultChanged);

            BuildTray();

            // Labels in the tray menu only change when a code rolls over or the account list
            // changes; the 1s tick compares a cheap signature and rebuilds only then.
            _menuTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _menuTimer.Tick += (_, _) => SyncMenu();
            _menuTimer.Start();
            SyncMenu();

            if (load.Status == VaultLoadStatus.Recovered)
            {
                _ = ShowRecoveryNoticeAsync(load);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task ShowRecoveryNoticeAsync(VaultLoadResult load)
    {
        if (_panel is null)
        {
            return;
        }

        _panel.ShowPanel();
        await MessageDialog.ShowOk(
            _panel,
            "TrayAuth could not read its vault",
            $"The existing vault was set aside as {Path.GetFileName(load.QuarantinedPath)} and TrayAuth started empty.\n\nUse Import to restore from an export.");
    }

    private void OnVaultChanged() => SyncMenu();

    // ---- tray ---------------------------------------------------------------------------------

    private void BuildTray()
    {
        _menu = new NativeMenu();

        var show = new NativeMenuItem("Show codes");
        show.Click += (_, _) => _panel?.ShowPanel();

        var add = new NativeMenuItem("Add account...");
        add.Click += async (_, _) =>
        {
            _panel?.ShowPanel();
            if (_panel is not null)
            {
                await _panel.AddAccountAsync();
            }
        };

        var importFile = new NativeMenuItem("Import from file...");
        importFile.Click += async (_, _) =>
        {
            _panel?.ShowPanel();
            if (_panel is not null)
            {
                await _panel.ImportFromFileAsync();
            }
        };

        var importQr = new NativeMenuItem("Import from QR image...");
        importQr.Click += async (_, _) =>
        {
            _panel?.ShowPanel();
            if (_panel is not null)
            {
                await _panel.ImportFromQrImagesAsync();
            }
        };

        var export = new NativeMenuItem("Export all accounts...");
        export.Click += async (_, _) =>
        {
            _panel?.ShowPanel();
            if (_panel is not null)
            {
                await _panel.ExportAllAsync();
            }
        };

        var autostart = new NativeMenuItem("Start on login")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = Autostart.IsEnabled(),
        };
        autostart.Click += (_, _) =>
        {
            bool desired = !Autostart.IsEnabled();
            Autostart.SetEnabled(desired);
            autostart.IsChecked = Autostart.IsEnabled();
        };

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) =>
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        };

        _menu.Add(show);
        _menu.Add(add);
        _menu.Add(new NativeMenuItemSeparator());
        _menu.Add(importFile);
        _menu.Add(importQr);
        _menu.Add(export);
        _menu.Add(new NativeMenuItemSeparator());

        // Inside Flatpak the sandbox blocks writing ~/.config/autostart (the proper route is
        // the Background portal - future work), so the toggle would silently lie. Hide it.
        if (!File.Exists("/.flatpak-info"))
        {
            _menu.Add(autostart);
            _menu.Add(new NativeMenuItemSeparator());
        }

        _menu.Add(quit);

        _tray = new TrayIcon
        {
            ToolTipText = "TrayAuth - authenticator codes",
            Icon = LoadIcon(),
            Menu = _menu,
            IsVisible = true,
        };

        _tray.Clicked += (_, _) => _panel?.TogglePanel();

        TrayIcon.SetIcons(this, [_tray]);
    }

    /// <summary>
    /// Rebuilds the code section at the top of the tray menu when anything user-visible
    /// changed. Rebuild-not-mutate keeps the DBus menu exporter's view consistent.
    /// </summary>
    private void SyncMenu()
    {
        if (_menu is null)
        {
            return;
        }

        var entries = new List<(string Id, string Label)>();
        foreach (Account account in _vault.Accounts.Take(15))
        {
            try
            {
                TotpCode code = account.Generate();
                entries.Add((account.Id, $"{Truncate(account.FullName, 34)}   {code.Grouped}  ({code.SecondsRemaining}s)"));
            }
            catch
            {
                // Broken entries surface their error in the panel.
            }
        }

        string signature = string.Join("|", entries.Select(e => e.Id + "=" + e.Label));
        if (signature == _menuSignature)
        {
            return;
        }

        _menuSignature = signature;

        foreach (NativeMenuItem stale in _codeItems)
        {
            _menu.Items.Remove(stale);
        }

        _codeItems.Clear();

        int index = 0;
        foreach ((string id, string label) in entries)
        {
            var item = new NativeMenuItem(label);
            string accountId = id;
            item.Click += (_, _) => CopyAccountCode(accountId);

            _codeItems.Add(item);
            _menu.Items.Insert(index++, item);
        }

        if (entries.Count > 0)
        {
            // NativeMenuItemSeparator cannot be tracked in _codeItems (different type), so the
            // separator after the codes is the one static "Show codes" boundary already present.
        }
    }

    private void CopyAccountCode(string accountId)
    {
        Account? account = _vault.Find(accountId);
        if (account is null)
        {
            return;
        }

        try
        {
            // Recomputed at click time - the label may be up to a second stale, the copy never is.
            TotpCode code = account.Generate();

            if (!ClipboardHelper.CopyWithAutoClear(code.Code) && _panel is not null)
            {
                // No clipboard tool: fall back to the panel path, which can also explain why.
                _panel.ShowPanel();
            }
        }
        catch
        {
            // Nothing sensible to copy.
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "...";

    private static WindowIcon LoadIcon()
    {
        // The build embeds assets/icon.ico when present (generated by tools/MakeIcon).
        using Stream? stream = typeof(App).Assembly.GetManifestResourceStream("TrayAuth.icon.ico");
        if (stream is not null)
        {
            return new WindowIcon(stream);
        }

        // Clean-clone fallback: a plain accent square, so the app still runs.
        var bitmap = new WriteableBitmap(new PixelSize(32, 32), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (ILockedFramebuffer fb = bitmap.Lock())
        {
            unsafe
            {
                var pixels = (uint*)fb.Address;
                for (int i = 0; i < 32 * 32; i++)
                {
                    pixels[i] = 0xFF3DD68C;
                }
            }
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }
}
