using Avalonia.Media;

namespace TrayAuth.Linux.UI;

/// <summary>The Windows Theme palette, expressed as Avalonia brushes - both apps look related.</summary>
public static class LinuxTheme
{
    public static readonly Color Background = Color.FromRgb(0x1B, 0x1B, 0x20);
    public static readonly Color Surface = Color.FromRgb(0x25, 0x25, 0x2C);
    public static readonly Color SurfaceHover = Color.FromRgb(0x2F, 0x2F, 0x38);
    public static readonly Color Border = Color.FromRgb(0x35, 0x35, 0x3F);

    public static readonly Color Text = Color.FromRgb(0xEC, 0xEC, 0xF1);
    public static readonly Color TextSecondary = Color.FromRgb(0x92, 0x92, 0xA2);
    public static readonly Color TextFaint = Color.FromRgb(0x6A, 0x6A, 0x7A);

    public static readonly Color Accent = Color.FromRgb(0x3D, 0xD6, 0x8C);
    public static readonly Color AccentDim = Color.FromRgb(0x2A, 0x6B, 0x51);
    public static readonly Color Warning = Color.FromRgb(0xF5, 0xA6, 0x23);
    public static readonly Color Danger = Color.FromRgb(0xE5, 0x64, 0x5F);

    public static readonly IBrush BackgroundBrush = new SolidColorBrush(Background);
    public static readonly IBrush SurfaceBrush = new SolidColorBrush(Surface);
    public static readonly IBrush SurfaceHoverBrush = new SolidColorBrush(SurfaceHover);
    public static readonly IBrush BorderBrush = new SolidColorBrush(Border);
    public static readonly IBrush TextBrush = new SolidColorBrush(Text);
    public static readonly IBrush TextSecondaryBrush = new SolidColorBrush(TextSecondary);
    public static readonly IBrush TextFaintBrush = new SolidColorBrush(TextFaint);
    public static readonly IBrush AccentBrush = new SolidColorBrush(Accent);
    public static readonly IBrush AccentDimBrush = new SolidColorBrush(AccentDim);
    public static readonly IBrush WarningBrush = new SolidColorBrush(Warning);
    public static readonly IBrush DangerBrush = new SolidColorBrush(Danger);

    public const double PanelWidth = 340;
    public const double RowHeight = 62;
}
