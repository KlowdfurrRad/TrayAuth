using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TrayAuth.Core;

namespace TrayAuth.Linux.UI;

/// <summary>
/// The codes window. On Wayland the compositor decides where windows go - the slide-out
/// anchored to the tray icon that defines the Windows app simply cannot exist here - so this
/// is an honest popup: opens centered, stays on top, hides on focus loss or Escape.
/// </summary>
public sealed class PanelWindow : Window
{
    private readonly LinuxVault _vault;
    private readonly Action _vaultChanged;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly TextBlock _emptyState = new()
    {
        Text = "No accounts yet.\n\nAdd one with the + button and paste in the\nsetup key the site shows next to its QR code.",
        Foreground = LinuxTheme.TextSecondaryBrush,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(0, 30),
    };

    private readonly DispatcherTimer _tick;
    private readonly List<AccountRowControl> _rows = [];

    /// <summary>Non-zero while one of our own dialogs is open, so Deactivated doesn't hide us.</summary>
    private int _suppressAutoHide;

    public PanelWindow(LinuxVault vault, Action vaultChanged)
    {
        _vault = vault;
        _vaultChanged = vaultChanged;

        Title = "TrayAuth";
        Width = LinuxTheme.PanelWidth;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = LinuxTheme.BackgroundBrush;

        BuildChrome();

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) => RefreshCodes();

        Deactivated += (_, _) =>
        {
            if (_suppressAutoHide == 0)
            {
                HidePanel();
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                HidePanel();
            }
        };

        Closing += (_, e) =>
        {
            // The tray owns the app lifetime; closing the panel only hides it.
            e.Cancel = true;
            HidePanel();
        };
    }

    private void BuildChrome()
    {
        var titleText = new TextBlock
        {
            Text = "Authenticator",
            FontWeight = FontWeight.Bold,
            FontSize = 15,
            Foreground = LinuxTheme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var addButton = HeaderButton("+", "Add account");
        addButton.Click += async (_, _) => await AddAccountAsync();

        var header = new DockPanel { Margin = new Thickness(14, 10, 10, 6) };
        DockPanel.SetDock(addButton, Dock.Right);
        header.Children.Add(addButton);
        header.Children.Add(titleText);

        var importFile = FooterButton("Import file");
        importFile.Click += async (_, _) => await ImportFromFileAsync();

        var importQr = FooterButton("Import QR");
        importQr.Click += async (_, _) => await ImportFromQrImagesAsync();

        var export = FooterButton("Export all");
        export.Click += async (_, _) => await ExportAllAsync();

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 6, 12, 12),
            Children = { importFile, importQr, export },
        };

        Content = new StackPanel
        {
            Children =
            {
                header,
                new Border { Height = 1, Background = LinuxTheme.BorderBrush, Margin = new Thickness(10, 0) },
                _emptyState,
                _list,
                footer,
            },
        };
    }

    private static Button HeaderButton(string glyph, string tip)
    {
        var button = new Button
        {
            Content = glyph,
            FontSize = 16,
            Padding = new Thickness(10, 2),
            Background = Brushes.Transparent,
            Foreground = LinuxTheme.TextSecondaryBrush,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Button FooterButton(string text) => new()
    {
        Content = text,
        FontSize = 12,
        Padding = new Thickness(10, 5),
        Background = LinuxTheme.SurfaceBrush,
        Foreground = LinuxTheme.TextBrush,
    };

    // ---- show / hide ----------------------------------------------------------------------

    public void TogglePanel()
    {
        if (IsVisible)
        {
            HidePanel();
        }
        else
        {
            ShowPanel();
        }
    }

    public void ShowPanel()
    {
        ReloadAccounts();
        Show();
        Activate();
        _tick.Start();
    }

    public void HidePanel()
    {
        _tick.Stop();
        Hide();
    }

    // ---- accounts ---------------------------------------------------------------------------

    public void ReloadAccounts()
    {
        _list.Children.Clear();
        _rows.Clear();

        foreach (Account account in _vault.Accounts)
        {
            var row = new AccountRowControl(account, CopyFromRow)
            {
                Width = LinuxTheme.PanelWidth - 8,
            };

            var menu = new ContextMenu();

            var edit = new MenuItem { Header = "Edit..." };
            edit.Click += async (_, _) => await EditAccountAsync(account);

            var export = new MenuItem { Header = "Export..." };
            export.Click += async (_, _) => await ExportOneAsync(account);

            var delete = new MenuItem { Header = "Delete..." };
            delete.Click += async (_, _) => await DeleteAccountAsync(account);

            menu.Items.Add(edit);
            menu.Items.Add(export);
            menu.Items.Add(new Separator());
            menu.Items.Add(delete);
            row.ContextMenu = menu;

            _rows.Add(row);
            _list.Children.Add(row);
        }

        _emptyState.IsVisible = _rows.Count == 0;
        RefreshCodes();
    }

    private void RefreshCodes()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (AccountRowControl row in _rows)
        {
            row.RefreshCode(now);
        }
    }

    private async void CopyFromRow(AccountRowControl row)
    {
        if (!row.HasValidCode)
        {
            return;
        }

        if (ClipboardHelper.CopyWithAutoClear(row.RawCode))
        {
            row.FlashCopied();
        }
        else if (Clipboard is { } clipboard)
        {
            // Last resort: the focused-window clipboard. No auto-clear guarantees off-focus,
            // but the copy itself works.
            await clipboard.SetTextAsync(row.RawCode);
            row.FlashCopied();
            await WarnMissingClipboardToolOnce();
        }
    }

    private bool _warnedClipboard;

    private async Task WarnMissingClipboardToolOnce()
    {
        if (_warnedClipboard)
        {
            return;
        }

        _warnedClipboard = true;
        await RunModal(() => MessageDialog.ShowOk(
            this,
            "Clipboard tool recommended",
            "Copying from the tray menu needs wl-clipboard.\n\nInstall it with:\n  sudo apt install wl-clipboard\n\nUntil then, copying only works from this panel while it has focus."));
    }

    // ---- actions ----------------------------------------------------------------------------

    public async Task AddAccountAsync()
    {
        Account? account = await RunModal(() => new AddAccountWindow().ShowDialog<Account?>(this));
        if (account is null)
        {
            return;
        }

        _vault.Add(account);
        await SaveAndReloadAsync();
    }

    private async Task EditAccountAsync(Account account)
    {
        Account? edited = await RunModal(() => new AddAccountWindow(account).ShowDialog<Account?>(this));
        if (edited is null)
        {
            return;
        }

        _vault.Update(edited);
        await SaveAndReloadAsync();
    }

    private async Task DeleteAccountAsync(Account account)
    {
        MessageResult answer = await RunModal(() => MessageDialog.ShowYesNo(
            this,
            "Delete account",
            $"Delete \"{account.FullName}\"?\n\nIf you have not exported this account, its codes cannot be recovered."));

        if (answer != MessageResult.Yes)
        {
            return;
        }

        _vault.Remove(account.Id);
        await SaveAndReloadAsync();
    }

    private async Task ExportOneAsync(Account account)
    {
        IReadOnlyList<IStorageFolder> folders = await PickFolderAsync($"Choose where to save \"{account.FullName}\"");
        string? directory = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (directory is null)
        {
            return;
        }

        try
        {
            ExportResult result = ExportService.ExportAccount(account, directory);
            await RunModal(() => MessageDialog.ShowOk(
                this,
                "Export complete",
                $"Exported \"{account.FullName}\" to:\n{result.Directory}\n\nThe .png is a QR any authenticator app can scan. Both files hold the secret in the clear - keep them safe."));
        }
        catch (Exception ex)
        {
            await RunModal(() => MessageDialog.ShowOk(this, "Export failed", ex.Message));
        }
    }

    public async Task ExportAllAsync()
    {
        if (_vault.Accounts.Count == 0)
        {
            await RunModal(() => MessageDialog.ShowOk(this, "Export", "There are no accounts to export yet."));
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await PickFolderAsync("Choose where to save the export folder");
        string? parent = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (parent is null)
        {
            return;
        }

        try
        {
            ExportResult result = ExportService.ExportAll(_vault.Accounts, parent);
            await RunModal(() => MessageDialog.ShowOk(
                this,
                "Export complete",
                $"Exported {result.AccountCount} account(s) to:\n{result.Directory}\n\nThe folder holds one JSON + QR pair per account plus {ExportService.CombinedFileName}, which restores everything at once. These files are not encrypted."));
        }
        catch (Exception ex)
        {
            await RunModal(() => MessageDialog.ShowOk(this, "Export failed", ex.Message));
        }
    }

    public async Task ImportFromFileAsync()
    {
        IReadOnlyList<IStorageFile> files = await PickFilesAsync(
            "Import accounts",
            new FilePickerFileType("TrayAuth export") { Patterns = ["*.json", "*.txt"] },
            allowMultiple: false);

        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<Account> found = ExportService.Import(path);
            await MergeImportedAccountsAsync(found, notes: null);
        }
        catch (Exception ex)
        {
            await RunModal(() => MessageDialog.ShowOk(this, "Import failed", ex.Message));
        }
    }

    public async Task ImportFromQrImagesAsync()
    {
        IReadOnlyList<IStorageFile> files = await PickFilesAsync(
            "Import from QR image",
            new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] },
            allowMultiple: true);

        if (files.Count == 0)
        {
            return;
        }

        var texts = new List<string>();
        var unreadable = new List<string>();

        foreach (IStorageFile file in files)
        {
            string? path = file.TryGetLocalPath();
            if (path is null)
            {
                continue;
            }

            try
            {
                texts.AddRange(LinuxQrDecoder.DecodeImageFile(path));
            }
            catch
            {
                unreadable.Add(Path.GetFileName(path));
            }
        }

        if (unreadable.Count > 0)
        {
            await RunModal(() => MessageDialog.ShowOk(
                this, "Import from QR", "Could not open as images:\n" + string.Join("\n", unreadable)));
        }

        if (texts.Count == 0)
        {
            await RunModal(() => MessageDialog.ShowOk(
                this,
                "Import from QR",
                "No QR code could be read.\n\nMake sure the whole QR is visible and reasonably sharp, then try again."));
            return;
        }

        QrImportOutcome outcome = QrImport.CollectAccounts(texts);

        if (outcome.Accounts.Count == 0)
        {
            string detail = outcome.Notes.Count > 0
                ? string.Join("\n\n", outcome.Notes)
                : "The QR code(s) do not contain authenticator accounts.";
            await RunModal(() => MessageDialog.ShowOk(this, "Import from QR", detail));
            return;
        }

        await MergeImportedAccountsAsync(outcome.Accounts, outcome.Notes);
    }

    private async Task MergeImportedAccountsAsync(IReadOnlyList<Account> found, IReadOnlyList<string>? notes)
    {
        string extra = notes is { Count: > 0 } ? "\n\n" + string.Join("\n", notes) : string.Empty;

        MessageResult confirm = await RunModal(() => MessageDialog.ShowYesNo(
            this, "Import accounts", $"Found {found.Count} account(s).{extra}\n\nImport them now?"));

        if (confirm != MessageResult.Yes)
        {
            return;
        }

        int added = 0;
        int replaced = 0;
        int skipped = 0;

        foreach (Account account in found)
        {
            Account? existing = _vault.FindMatch(account);

            if (existing is null)
            {
                _vault.Add(account);
                added++;
                continue;
            }

            MessageResult choice = await RunModal(() => MessageDialog.ShowYesNoCancel(
                this,
                "Import conflict",
                $"\"{account.FullName}\" is already in your vault.\n\nReplace it with the imported copy?"));

            if (choice == MessageResult.Cancel)
            {
                break;
            }

            if (choice == MessageResult.Yes)
            {
                account.Id = existing.Id;
                _vault.Update(account);
                replaced++;
            }
            else
            {
                skipped++;
            }
        }

        await SaveAndReloadAsync();

        await RunModal(() => MessageDialog.ShowOk(
            this, "Import finished", $"Added: {added}\nReplaced: {replaced}\nSkipped: {skipped}"));
    }

    private async Task SaveAndReloadAsync()
    {
        try
        {
            _vault.Save();
        }
        catch (Exception ex)
        {
            await RunModal(() => MessageDialog.ShowOk(
                this, "TrayAuth", $"Your change could not be saved to the vault.\n\n{ex.Message}"));
        }

        ReloadAccounts();
        _vaultChanged();
    }

    // ---- helpers ------------------------------------------------------------------------------

    private async Task<IReadOnlyList<IStorageFile>> PickFilesAsync(string title, FilePickerFileType filter, bool allowMultiple)
    {
        _suppressAutoHide++;
        try
        {
            return await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = allowMultiple,
                FileTypeFilter = [filter, FilePickerFileTypes.All],
            });
        }
        finally
        {
            _suppressAutoHide--;
        }
    }

    private async Task<IReadOnlyList<IStorageFolder>> PickFolderAsync(string title)
    {
        _suppressAutoHide++;
        try
        {
            return await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            });
        }
        finally
        {
            _suppressAutoHide--;
        }
    }

    private async Task<T> RunModal<T>(Func<Task<T>> dialog)
    {
        _suppressAutoHide++;
        try
        {
            return await dialog();
        }
        finally
        {
            _suppressAutoHide--;
        }
    }
}
