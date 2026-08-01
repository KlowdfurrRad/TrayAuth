using System.Drawing.Drawing2D;
using TrayAuth.Core;
using TrayAuth.Interop;

namespace TrayAuth.UI;

/// <summary>
/// The flyout that slides out of the taskbar. Borderless, always on top, and absent from both the
/// taskbar and Alt+Tab, so it behaves like a shell surface rather than a window you have to manage.
/// </summary>
public sealed class PanelForm : Form
{
    private const int SlideDurationMs = 160;
    private const int FrameIntervalMs = 15;

    private readonly Vault _vault;
    private readonly ClipboardService _clipboard;

    private readonly Panel _list = new();
    private readonly Label _emptyState = new();
    private readonly System.Windows.Forms.Timer _tickTimer = new();
    private readonly System.Windows.Forms.Timer _slideTimer = new();

    private readonly List<AccountRow> _rows = [];

    private Point _shownLocation;
    private Point _hiddenLocation;
    private bool _slidingIn;
    private DateTime _slideStart;

    /// <summary>
    /// Set while a modal dialog or folder picker is open. Without it, losing activation to our own
    /// dialog would slide the panel away underneath it.
    /// </summary>
    private int _suppressAutoHide;

    public PanelForm(Vault vault, ClipboardService clipboard)
    {
        _vault = vault;
        _clipboard = clipboard;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Theme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        Width = Theme.PanelWidth;
        Height = 260;
        Opacity = 0d;

        BuildChrome();

        _tickTimer.Interval = 200;
        _tickTimer.Tick += (_, _) => RefreshCodes();

        _slideTimer.Interval = FrameIntervalMs;
        _slideTimer.Tick += OnSlideTick;

        // Force handle creation up front: the hotkey and the single-instance broadcast are both
        // delivered to this window, and they must work before it is ever shown.
        _ = Handle;
    }

    /// <summary>Message id used by a second instance to ask this one to show itself.</summary>
    public uint ShowPanelMessage { get; set; }

    public event EventHandler? AddRequested;

    public event EventHandler? MenuRequested;

    public bool IsShown { get; private set; }

    protected override bool ShowWithoutActivation => false;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    private void BuildChrome()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = Theme.HeaderHeight,
            BackColor = Theme.Background,
        };

        var title = new Label
        {
            Text = "Authenticator",
            Font = Theme.Title,
            ForeColor = Theme.Text,
            AutoSize = true,
            Location = new Point(14, 13),
            BackColor = Color.Transparent,
        };

        var menuButton = MakeHeaderButton("⋯", "More options");
        menuButton.Click += (_, _) => MenuRequested?.Invoke(this, EventArgs.Empty);

        var addButton = MakeHeaderButton("+", "Add account");
        addButton.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);

        header.Resize += (_, _) =>
        {
            menuButton.Location = new Point(header.Width - 36, 9);
            addButton.Location = new Point(header.Width - 66, 9);
        };

        header.Controls.Add(title);
        header.Controls.Add(menuButton);
        header.Controls.Add(addButton);

        _list.Dock = DockStyle.Fill;
        _list.AutoScroll = true;
        _list.BackColor = Theme.Background;
        _list.Padding = new Padding(0, 2, 0, 6);

        _emptyState.Text = "No accounts yet.\r\n\r\nAdd one with the + button and paste in the\r\nsetup key the site shows next to its QR code.";
        _emptyState.Font = Theme.Body;
        _emptyState.ForeColor = Theme.TextSecondary;
        _emptyState.TextAlign = ContentAlignment.MiddleCenter;
        _emptyState.Dock = DockStyle.Fill;
        _emptyState.Visible = false;

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = Theme.FooterHeight,
            BackColor = Theme.Background,
        };

        var addLink = new Label
        {
            Text = "+  Add account",
            Font = Theme.Body,
            ForeColor = Theme.Accent,
            AutoSize = true,
            Location = new Point(16, 13),
            Cursor = Cursors.Hand,
            BackColor = Color.Transparent,
        };
        addLink.Click += (_, _) => AddRequested?.Invoke(this, EventArgs.Empty);

        footer.Controls.Add(addLink);

        Controls.Add(_list);
        Controls.Add(_emptyState);
        Controls.Add(footer);
        Controls.Add(header);

        _emptyState.BringToFront();
    }

    private static Label MakeHeaderButton(string glyph, string tooltip)
    {
        var button = new Label
        {
            Text = glyph,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Theme.TextSecondary,
            TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(26, 26),
            Cursor = Cursors.Hand,
            BackColor = Color.Transparent,
        };

        button.MouseEnter += (_, _) => button.ForeColor = Theme.Text;
        button.MouseLeave += (_, _) => button.ForeColor = Theme.TextSecondary;

        var tip = new ToolTip();
        tip.SetToolTip(button, tooltip);

        return button;
    }

    // ---- account list -------------------------------------------------------------------

    public void ReloadAccounts()
    {
        _list.SuspendLayout();

        foreach (AccountRow row in _rows)
        {
            _list.Controls.Remove(row);
            row.Dispose();
        }

        _rows.Clear();

        int y = 0;
        foreach (Account account in _vault.Accounts)
        {
            var row = new AccountRow(account)
            {
                Location = new Point(0, y),
                Width = Theme.PanelWidth - 4,
            };

            row.CopyRequested += (s, _) => CopyFrom((AccountRow)s!);
            row.EditRequested += (s, _) => EditAccount(((AccountRow)s!).Account);
            row.DeleteRequested += (s, _) => DeleteAccount(((AccountRow)s!).Account);
            row.ExportRequested += (s, _) => ExportAccount(((AccountRow)s!).Account);

            _list.Controls.Add(row);
            _rows.Add(row);

            y += Theme.RowHeight;
        }

        _list.ResumeLayout();

        _emptyState.Visible = _rows.Count == 0;
        RefreshCodes();
        ResizeToContent();
    }

    private void ResizeToContent()
    {
        int visibleRows = Math.Clamp(_rows.Count, 1, Theme.MaxVisibleRows);
        int listHeight = _rows.Count == 0 ? 110 : (visibleRows * Theme.RowHeight) + 8;

        Height = Theme.HeaderHeight + listHeight + Theme.FooterHeight;
    }

    private void RefreshCodes()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (AccountRow row in _rows)
        {
            row.Refresh(now);

            if (row.CopiedFlashExpired)
            {
                row.ClearCopiedFlash();
            }
        }
    }

    private void CopyFrom(AccountRow row)
    {
        if (!row.HasValidCode)
        {
            return;
        }

        if (_clipboard.TryCopy(row.RawCode))
        {
            row.FlashCopied();
        }
        else
        {
            MessageBox.Show(
                this,
                "Windows would not let us write to the clipboard just now. Another program may be holding it — try again in a moment.",
                "TrayAuth",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    // ---- account actions ----------------------------------------------------------------

    public void AddAccount()
    {
        using var dialog = new AddAccountDialog();
        if (RunModal(dialog) != DialogResult.OK || dialog.Result is null)
        {
            return;
        }

        _vault.Add(dialog.Result);
        SaveAndReload();
    }

    private void EditAccount(Account account)
    {
        using var dialog = new AddAccountDialog(account);
        if (RunModal(dialog) != DialogResult.OK || dialog.Result is null)
        {
            return;
        }

        _vault.Update(dialog.Result);
        SaveAndReload();
    }

    private void DeleteAccount(Account account)
    {
        DialogResult answer = ShowMessage(
            $"Delete \"{account.FullName}\"?\r\n\r\nIf you have not exported this account, its codes cannot be recovered — you would need to turn two-factor authentication off and on again at the site.",
            "Delete account",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        _vault.Remove(account.Id);
        SaveAndReload();
    }

    private void ExportAccount(Account account)
    {
        string? directory = PickExportFolder($"Choose where to save \"{account.FullName}\".");
        if (directory is null)
        {
            return;
        }

        try
        {
            _suppressAutoHide++;
            ExportResult result = ExportService.ExportAccount(account, directory);

            ShowMessage(
                $"Exported \"{account.FullName}\".\r\n\r\n{result.Directory}\r\n\r\nThe .png is a QR code you can scan with an authenticator app on your phone. Both files hold the secret in the clear — keep them somewhere safe.",
                "Export complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowMessage($"The export failed.\r\n\r\n{ex.Message}", "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _suppressAutoHide--;
        }
    }

    public void ExportAll()
    {
        if (_vault.Accounts.Count == 0)
        {
            ShowMessage("There are no accounts to export yet.", "Export all accounts", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string? parent = PickExportFolder("Choose where to save the export folder.");
        if (parent is null)
        {
            return;
        }

        try
        {
            _suppressAutoHide++;
            ExportResult result = ExportService.ExportAll(_vault.Accounts, parent);

            DialogResult open = ShowMessage(
                $"Exported {result.AccountCount} account(s) to:\r\n\r\n{result.Directory}\r\n\r\nThe folder holds one JSON + QR pair per account, plus {ExportService.CombinedFileName} which restores everything at once. These files are not encrypted.\r\n\r\nOpen the folder now?",
                "Export complete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (open == DialogResult.Yes)
            {
                OpenFolder(result.Directory);
            }
        }
        catch (Exception ex)
        {
            ShowMessage($"The export failed.\r\n\r\n{ex.Message}", "Export all accounts", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _suppressAutoHide--;
        }
    }

    public void ImportAccounts()
    {
        _suppressAutoHide++;

        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Import accounts",
                Filter = "TrayAuth export (*.json;*.txt)|*.json;*.txt|All files (*.*)|*.*",
                InitialDirectory = Directory.Exists(ExportService.DefaultExportRoot)
                    ? ExportService.DefaultExportRoot
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            IReadOnlyList<Account> found = ExportService.Import(dialog.FileName);
            MergeImportedAccounts(found, notes: null);
        }
        catch (Exception ex)
        {
            ShowMessage($"That file could not be imported.\r\n\r\n{ex.Message}", "Import accounts", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _suppressAutoHide--;
        }
    }

    /// <summary>
    /// Import from image files containing QR codes - Google Authenticator transfer QRs
    /// (screenshotted on the phone and copied over) or a site's plain otpauth enrollment QR.
    /// </summary>
    public void ImportFromQrImages()
    {
        _suppressAutoHide++;

        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Import from QR image",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
                Multiselect = true,
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var texts = new List<string>();
            var unreadable = new List<string>();

            foreach (string file in dialog.FileNames)
            {
                try
                {
                    texts.AddRange(QrDecoder.DecodeImageFile(file));
                }
                catch
                {
                    unreadable.Add(Path.GetFileName(file));
                }
            }

            if (unreadable.Count > 0)
            {
                ShowMessage(
                    "These files could not be opened as images:\r\n\r\n" + string.Join("\r\n", unreadable),
                    "Import from QR",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            HandleQrTexts(texts);
        }
        catch (Exception ex)
        {
            ShowMessage($"The QR import failed.\r\n\r\n{ex.Message}", "Import from QR", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _suppressAutoHide--;
        }
    }

    /// <summary>Shared tail of every QR path: decode payloads to accounts, then run the merge flow.</summary>
    public void HandleQrTexts(IReadOnlyList<string> texts)
    {
        if (texts.Count == 0)
        {
            ShowMessage(
                "No QR code could be read.\r\n\r\nMake sure the whole QR is visible and reasonably sharp, then try again.",
                "Import from QR",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        QrImportOutcome outcome = QrImport.CollectAccounts(texts);

        if (outcome.Accounts.Count == 0)
        {
            string detail = outcome.Notes.Count > 0
                ? string.Join("\r\n\r\n", outcome.Notes)
                : "The QR code(s) do not contain authenticator accounts.";

            ShowMessage(detail, "Import from QR", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        MergeImportedAccounts(outcome.Accounts, outcome.Notes);
    }

    /// <summary>
    /// Confirms, resolves conflicts against the vault one by one, saves, and reports - the same
    /// flow whether the accounts came from an export file or a QR.
    /// </summary>
    private void MergeImportedAccounts(IReadOnlyList<Account> found, IReadOnlyList<string>? notes)
    {
        string extra = notes is { Count: > 0 }
            ? "\r\n\r\n" + string.Join("\r\n", notes)
            : string.Empty;

        DialogResult confirm = ShowMessage(
            $"Found {found.Count} account(s).{extra}\r\n\r\nImport them now?",
            "Import accounts",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
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

            DialogResult choice = ShowMessage(
                $"\"{account.FullName}\" is already in your vault.\r\n\r\nReplace it with the imported copy?\r\n\r\nYes — replace    No — keep what I have    Cancel — stop importing",
                "Import conflict",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Cancel)
            {
                break;
            }

            if (choice == DialogResult.Yes)
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

        SaveAndReload();

        ShowMessage(
            $"Import finished.\r\n\r\nAdded: {added}\r\nReplaced: {replaced}\r\nSkipped: {skipped}",
            "Import accounts",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void SaveAndReload()
    {
        try
        {
            _vault.Save();
        }
        catch (Exception ex)
        {
            ShowMessage(
                $"Your change could not be saved to the vault.\r\n\r\n{ex.Message}",
                "TrayAuth",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        ReloadAccounts();
    }

    private string? PickExportFolder(string description)
    {
        _suppressAutoHide++;

        try
        {
            Directory.CreateDirectory(ExportService.DefaultExportRoot);

            using var dialog = new FolderBrowserDialog
            {
                Description = description,
                UseDescriptionForTitle = true,
                SelectedPath = ExportService.DefaultExportRoot,
                ShowNewFolderButton = true,
            };

            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
        }
        catch (Exception ex)
        {
            ShowMessage($"That folder could not be used.\r\n\r\n{ex.Message}", "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        finally
        {
            _suppressAutoHide--;
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Nothing useful to do if Explorer will not open.
        }
    }

    public DialogResult ShowMessage(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        _suppressAutoHide++;
        try
        {
            return MessageBox.Show(this, text, caption, buttons, icon);
        }
        finally
        {
            _suppressAutoHide--;
        }
    }

    private DialogResult RunModal(Form dialog)
    {
        _suppressAutoHide++;
        try
        {
            return dialog.ShowDialog(this);
        }
        finally
        {
            _suppressAutoHide--;
        }
    }

    // ---- show / hide --------------------------------------------------------------------

    public void TogglePanel()
    {
        if (IsShown)
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

        Point anchor = Cursor.Position;
        TaskbarPlacement placement = TaskbarInfo.ForPoint(anchor);

        _shownLocation = TaskbarInfo.ShownLocation(placement, Size, anchor);
        _hiddenLocation = TaskbarInfo.HiddenLocation(placement, Size, _shownLocation);

        Location = IsShown ? Location : _hiddenLocation;
        Opacity = IsShown ? Opacity : 0d;

        IsShown = true;
        Show();
        NativeMethods.SetForegroundWindow(Handle);
        Activate();

        ApplyRoundedCorners();

        _tickTimer.Start();
        StartSlide(inwards: true);
    }

    public void HidePanel()
    {
        if (!IsShown)
        {
            return;
        }

        IsShown = false;
        StartSlide(inwards: false);
    }

    /// <summary>
    /// Hides with no slide and no fade - used before capturing the screen for QR scanning, where
    /// an always-on-top panel would end up in its own photograph.
    /// </summary>
    public void HidePanelImmediate()
    {
        _slideTimer.Stop();
        _tickTimer.Stop();
        IsShown = false;
        Opacity = 0d;
        Hide();
    }

    private void StartSlide(bool inwards)
    {
        _slidingIn = inwards;
        _slideStart = DateTime.UtcNow;
        _slideTimer.Start();
    }

    private void OnSlideTick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.UtcNow - _slideStart).TotalMilliseconds;
        double t = Math.Clamp(elapsed / SlideDurationMs, 0d, 1d);

        // Ease-out cubic: quick off the mark, settling gently — the motion the shell's own
        // flyouts use, and the reason this reads as "slides" rather than "jumps".
        double eased = 1d - Math.Pow(1d - t, 3d);
        double progress = _slidingIn ? eased : 1d - eased;

        Location = new Point(
            (int)Math.Round(_hiddenLocation.X + ((_shownLocation.X - _hiddenLocation.X) * progress)),
            (int)Math.Round(_hiddenLocation.Y + ((_shownLocation.Y - _hiddenLocation.Y) * progress)));

        Opacity = progress;

        if (t < 1d)
        {
            return;
        }

        _slideTimer.Stop();

        if (_slidingIn)
        {
            Location = _shownLocation;
            Opacity = 1d;
        }
        else
        {
            _tickTimer.Stop();
            Hide();
        }
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            int preference = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(
                Handle,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference,
                sizeof(int));
        }
        catch
        {
            // Pre-Windows 11: square corners, no harm done.
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        if (IsShown && _suppressAutoHide == 0)
        {
            HidePanel();
        }

        base.OnDeactivate(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            HidePanel();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // A hairline border keeps the panel legible against a light desktop.
        using var pen = new Pen(Theme.Border);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && (int)m.WParam == HotKey.HotKeyId)
        {
            TogglePanel();
            return;
        }

        // A second copy of the app broadcasts this instead of starting its own tray icon.
        if (ShowPanelMessage != 0 && (uint)m.Msg == ShowPanelMessage)
        {
            if (!IsShown)
            {
                ShowPanel();
            }

            return;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tickTimer.Dispose();
            _slideTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
