using TrayAuth.Core;

namespace TrayAuth.UI;

/// <summary>
/// Add or edit an account by typing the setup key.
///
/// The live preview is the point of this dialog: it shows the code the entered key actually
/// produces, so you can check it against the site before saving. Catching a mistyped key here is
/// the difference between a two-second retype and being locked out of an account.
/// </summary>
public sealed class AddAccountDialog : Form
{
    private readonly TextBox _issuer = new();
    private readonly TextBox _label = new();
    private readonly TextBox _secret = new();
    private readonly ComboBox _digits = new();
    private readonly NumericUpDown _period = new();
    private readonly ComboBox _algorithm = new();
    private readonly Label _preview = new();
    private readonly Label _previewCaption = new();
    private readonly Panel _advanced = new();
    private readonly Label _advancedToggle = new();
    private readonly Button _ok = new();
    private readonly System.Windows.Forms.Timer _previewTimer = new();

    private readonly string _existingId;
    private bool _advancedOpen;

    public AddAccountDialog(Account? existing = null)
    {
        _existingId = existing?.Id ?? Guid.NewGuid().ToString("N");

        Text = existing is null ? "Add account" : "Edit account";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ClientSize = new Size(400, 400);

        BuildLayout();

        if (existing is not null)
        {
            _issuer.Text = existing.Issuer;
            _label.Text = existing.Label;
            _secret.Text = existing.Secret;
            _digits.SelectedItem = existing.Digits.ToString();
            _period.Value = Math.Clamp(existing.Period, (int)_period.Minimum, (int)_period.Maximum);
            _algorithm.SelectedItem = Totp.ToName(existing.AlgorithmValue);

            if (existing.Digits != Totp.DefaultDigits
                || existing.Period != Totp.DefaultPeriod
                || existing.AlgorithmValue != OtpAlgorithm.Sha1)
            {
                ToggleAdvanced();
            }
        }

        _previewTimer.Interval = 400;
        _previewTimer.Tick += (_, _) => UpdatePreview();
        _previewTimer.Start();

        UpdatePreview();
    }

    public Account? Result { get; private set; }

    private void BuildLayout()
    {
        int y = 16;

        AddField("Issuer", "The service, e.g. GitHub", _issuer, ref y);
        AddField("Account", "Your username or email at that service", _label, ref y);
        AddField("Setup key", "The base32 key shown next to the QR code", _secret, ref y);

        _secret.CharacterCasing = CharacterCasing.Upper;
        _secret.Font = new Font("Consolas", 9.5f);

        _advancedToggle.Text = "▸  Advanced";
        _advancedToggle.ForeColor = Theme.TextSecondary;
        _advancedToggle.AutoSize = true;
        _advancedToggle.Cursor = Cursors.Hand;
        _advancedToggle.Location = new Point(16, y);
        _advancedToggle.Click += (_, _) => ToggleAdvanced();
        Controls.Add(_advancedToggle);

        y += 26;

        _advanced.Location = new Point(16, y);
        _advanced.Size = new Size(368, 50);
        _advanced.Visible = false;
        BuildAdvanced();
        Controls.Add(_advanced);

        _previewCaption.Text = "Code preview";
        _previewCaption.ForeColor = Theme.TextFaint;
        _previewCaption.Font = Theme.Small;
        _previewCaption.AutoSize = true;
        _previewCaption.Location = new Point(16, ClientSize.Height - 108);
        Controls.Add(_previewCaption);

        _preview.Font = Theme.Code;
        _preview.ForeColor = Theme.TextSecondary;
        _preview.AutoSize = false;
        _preview.Location = new Point(16, ClientSize.Height - 90);
        _preview.Size = new Size(368, 34);
        Controls.Add(_preview);

        _ok.Text = "Save";
        _ok.Size = new Size(90, 30);
        _ok.Location = new Point(ClientSize.Width - 106, ClientSize.Height - 44);
        _ok.FlatStyle = FlatStyle.System;
        _ok.Click += OnSave;
        Controls.Add(_ok);

        var cancel = new Button
        {
            Text = "Cancel",
            Size = new Size(90, 30),
            Location = new Point(ClientSize.Width - 204, ClientSize.Height - 44),
            FlatStyle = FlatStyle.System,
            DialogResult = DialogResult.Cancel,
        };
        Controls.Add(cancel);

        AcceptButton = _ok;
        CancelButton = cancel;
    }

    private void AddField(string caption, string hint, TextBox box, ref int y)
    {
        var label = new Label
        {
            Text = caption,
            ForeColor = Theme.TextSecondary,
            Font = Theme.Small,
            AutoSize = true,
            Location = new Point(16, y),
        };
        Controls.Add(label);

        box.Location = new Point(16, y + 18);
        box.Size = new Size(368, 24);
        box.BorderStyle = BorderStyle.FixedSingle;
        box.BackColor = Theme.Surface;
        box.ForeColor = Theme.Text;
        box.PlaceholderText = hint;
        Controls.Add(box);

        y += 62;
    }

    private void BuildAdvanced()
    {
        _digits.DropDownStyle = ComboBoxStyle.DropDownList;
        _digits.Items.AddRange(["6", "7", "8"]);
        _digits.SelectedIndex = 0;
        _digits.Location = new Point(0, 18);
        _digits.Width = 60;

        _period.Minimum = 1;
        _period.Maximum = 300;
        _period.Value = Totp.DefaultPeriod;
        _period.Location = new Point(126, 18);
        _period.Width = 66;

        _algorithm.DropDownStyle = ComboBoxStyle.DropDownList;
        _algorithm.Items.AddRange(["SHA1", "SHA256", "SHA512"]);
        _algorithm.SelectedIndex = 0;
        _algorithm.Location = new Point(268, 18);
        _algorithm.Width = 100;

        _advanced.Controls.Add(MakeCaption("Digits", 0));
        _advanced.Controls.Add(_digits);
        _advanced.Controls.Add(MakeCaption("Period (s)", 126));
        _advanced.Controls.Add(_period);
        _advanced.Controls.Add(MakeCaption("Algorithm", 268));
        _advanced.Controls.Add(_algorithm);
    }

    private static Label MakeCaption(string text, int x) => new()
    {
        Text = text,
        ForeColor = Theme.TextFaint,
        Font = Theme.Small,
        AutoSize = true,
        Location = new Point(x, 0),
    };

    private void ToggleAdvanced()
    {
        _advancedOpen = !_advancedOpen;
        _advanced.Visible = _advancedOpen;
        _advancedToggle.Text = _advancedOpen ? "▾  Advanced" : "▸  Advanced";
    }

    private Account BuildAccount() => new()
    {
        Id = _existingId,
        Issuer = _issuer.Text,
        Label = _label.Text,
        Secret = _secret.Text,
        Digits = int.TryParse(_digits.SelectedItem?.ToString(), out int digits) ? digits : Totp.DefaultDigits,
        Period = (int)_period.Value,
        Algorithm = _algorithm.SelectedItem?.ToString() ?? "SHA1",
    };

    private void UpdatePreview()
    {
        Account candidate = BuildAccount();

        if (_secret.TextLength == 0)
        {
            _preview.ForeColor = Theme.TextFaint;
            _preview.Font = Theme.Body;
            _preview.Text = "Enter a setup key to see its code.";
            return;
        }

        if (!candidate.TryNormalize(out string error))
        {
            _preview.ForeColor = Theme.Danger;
            _preview.Font = Theme.Body;
            _preview.Text = error;
            return;
        }

        try
        {
            TotpCode code = candidate.Generate();
            _preview.ForeColor = Theme.Accent;
            _preview.Font = Theme.Code;
            _preview.Text = $"{code.Grouped}    {code.SecondsRemaining}s";
        }
        catch (Exception ex)
        {
            _preview.ForeColor = Theme.Danger;
            _preview.Font = Theme.Body;
            _preview.Text = ex.Message;
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        Account candidate = BuildAccount();

        if (!candidate.TryNormalize(out string error))
        {
            MessageBox.Show(this, error, "Check the details", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        Result = candidate;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewTimer.Stop();
            _previewTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
