using System.Drawing.Drawing2D;

namespace TrayAuth.UI;

/// <summary>Shared palette and fonts. Kept in one place so the panel and dialogs stay consistent.</summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(0x1B, 0x1B, 0x20);
    public static readonly Color Surface = Color.FromArgb(0x25, 0x25, 0x2C);
    public static readonly Color SurfaceHover = Color.FromArgb(0x2F, 0x2F, 0x38);
    public static readonly Color Border = Color.FromArgb(0x35, 0x35, 0x3F);

    public static readonly Color Text = Color.FromArgb(0xEC, 0xEC, 0xF1);
    public static readonly Color TextSecondary = Color.FromArgb(0x92, 0x92, 0xA2);
    public static readonly Color TextFaint = Color.FromArgb(0x6A, 0x6A, 0x7A);

    public static readonly Color Accent = Color.FromArgb(0x3D, 0xD6, 0x8C);
    public static readonly Color AccentDim = Color.FromArgb(0x2A, 0x6B, 0x51);
    public static readonly Color Warning = Color.FromArgb(0xF5, 0xA6, 0x23);
    public static readonly Color Danger = Color.FromArgb(0xE5, 0x64, 0x5F);

    public const int PanelWidth = 340;
    public const int RowHeight = 62;
    public const int HeaderHeight = 44;
    public const int FooterHeight = 42;

    /// <summary>Rows visible before the list starts scrolling.</summary>
    public const int MaxVisibleRows = 6;

    public static Font Title { get; } = new("Segoe UI Semibold", 10f, FontStyle.Bold);

    public static Font Body { get; } = new("Segoe UI", 9f);

    public static Font Small { get; } = new("Segoe UI", 8.25f);

    /// <summary>Monospaced so the digits do not shuffle sideways as the code changes.</summary>
    public static Font Code { get; } = new("Consolas", 18f, FontStyle.Bold);

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);

        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);

        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);

        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}
