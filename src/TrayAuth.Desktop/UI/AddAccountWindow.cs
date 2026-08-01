using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAuth.Core;

namespace TrayAuth.Desktop.UI;

/// <summary>
/// Add or edit an account by typing the setup key. As on Windows, the live preview is the
/// point: seeing the code the key produces, next to the site's, before saving is what stops
/// a mistyped key becoming a lockout.
/// </summary>
public sealed class AddAccountWindow : Window
{
    private readonly TextBox _issuer = new() { Watermark = "The service, e.g. GitHub" };
    private readonly TextBox _label = new() { Watermark = "Your username or email there" };
    private readonly TextBox _secret = new() { Watermark = "The base32 key shown next to the QR code" };
    private readonly ComboBox _digits = new();
    private readonly NumericUpDown _period = new()
    {
        Minimum = 1,
        Maximum = 300,
        Value = Totp.DefaultPeriod,
        Increment = 5,
        FormatString = "0",
    };
    private readonly ComboBox _algorithm = new();
    private readonly TextBlock _preview = new()
    {
        FontFamily = new FontFamily("monospace"),
        FontSize = 22,
        FontWeight = FontWeight.Bold,
        Foreground = AppTheme.TextFaintBrush,
        Text = "Enter a setup key to see its code.",
    };

    private readonly string _existingId;
    private readonly DispatcherTimer _previewTimer;

    public AddAccountWindow(Account? existing = null)
    {
        _existingId = existing?.Id ?? Guid.NewGuid().ToString("N");

        Title = existing is null ? "Add account" : "Edit account";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = AppTheme.BackgroundBrush;
        ShowInTaskbar = false;

        _digits.ItemsSource = new[] { "6", "7", "8" };
        _digits.SelectedIndex = 0;
        _algorithm.ItemsSource = new[] { "SHA1", "SHA256", "SHA512" };
        _algorithm.SelectedIndex = 0;

        if (existing is not null)
        {
            _issuer.Text = existing.Issuer;
            _label.Text = existing.Label;
            _secret.Text = existing.Secret;
            _digits.SelectedItem = existing.Digits.ToString();
            _period.Value = existing.Period;
            _algorithm.SelectedItem = Totp.ToName(existing.AlgorithmValue);
        }

        BuildLayout();

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _previewTimer.Tick += (_, _) => UpdatePreview();
        _previewTimer.Start();
        Closed += (_, _) => _previewTimer.Stop();

        UpdatePreview();
    }

    private void BuildLayout()
    {
        var save = new Button { Content = "Save", MinWidth = 90, IsDefault = true };
        save.Click += (_, _) => OnSave();

        var cancel = new Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var advanced = new Expander
        {
            Header = "Advanced",
            Foreground = AppTheme.TextSecondaryBrush,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Margin = new Thickness(0, 8, 0, 0),
                Children =
                {
                    Labeled("Digits", _digits),
                    Labeled("Period (s)", _period),
                    Labeled("Algorithm", _algorithm),
                },
            },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                Caption("Issuer"), _issuer,
                Caption("Account"), _label,
                Caption("Setup key"), _secret,
                advanced,
                Caption("Code preview"),
                _preview,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(0, 10, 0, 0),
                    Children = { cancel, save },
                },
            },
        };
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        Foreground = AppTheme.TextSecondaryBrush,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private static StackPanel Labeled(string caption, Control control) => new()
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = caption, FontSize = 11, Foreground = AppTheme.TextFaintBrush },
            control,
        },
    };

    private Account BuildAccount() => new()
    {
        Id = _existingId,
        Issuer = _issuer.Text ?? string.Empty,
        Label = _label.Text ?? string.Empty,
        Secret = _secret.Text ?? string.Empty,
        Digits = int.TryParse(_digits.SelectedItem?.ToString(), out int digits) ? digits : Totp.DefaultDigits,
        Period = (int)(_period.Value ?? Totp.DefaultPeriod),
        Algorithm = _algorithm.SelectedItem?.ToString() ?? "SHA1",
    };

    private void UpdatePreview()
    {
        Account candidate = BuildAccount();

        if (string.IsNullOrWhiteSpace(candidate.Secret))
        {
            _preview.Foreground = AppTheme.TextFaintBrush;
            _preview.FontSize = 13;
            _preview.Text = "Enter a setup key to see its code.";
            return;
        }

        if (!candidate.TryNormalize(out string error))
        {
            _preview.Foreground = AppTheme.DangerBrush;
            _preview.FontSize = 13;
            _preview.Text = error;
            return;
        }

        try
        {
            TotpCode code = candidate.Generate();
            _preview.Foreground = AppTheme.AccentBrush;
            _preview.FontSize = 22;
            _preview.Text = $"{code.Grouped}    {code.SecondsRemaining}s";
        }
        catch (Exception ex)
        {
            _preview.Foreground = AppTheme.DangerBrush;
            _preview.FontSize = 13;
            _preview.Text = ex.Message;
        }
    }

    private async void OnSave()
    {
        Account candidate = BuildAccount();

        if (!candidate.TryNormalize(out string error))
        {
            await MessageDialog.ShowOk(this, "Check the details", error);
            return;
        }

        Close(candidate);
    }
}
