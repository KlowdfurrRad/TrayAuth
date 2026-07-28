using System.Drawing.Drawing2D;
using System.Reflection;

namespace TrayAuth.UI;

/// <summary>
/// Supplies the tray icon: the embedded .ico when the build has one, otherwise an equivalent drawn
/// at runtime. The fallback means <c>dotnet run</c> works on a fresh clone before the icon has been
/// generated, instead of failing on a missing resource.
/// </summary>
public static class AppIcon
{
    private const string ResourceName = "TrayAuth.icon.ico";

    private static Icon? _cached;

    public static Icon Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is not null)
            {
                _cached = new Icon(stream);
                return _cached;
            }
        }
        catch
        {
            // Fall through to the drawn icon.
        }

        _cached = Draw(32);
        return _cached;
    }

    /// <summary>Draws the mark: a rounded square with a keyhole-style ring and stem.</summary>
    public static Bitmap DrawBitmap(int size)
    {
        var bitmap = new Bitmap(size, size);

        using Graphics g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float scale = size / 32f;
        var body = new Rectangle(1, 1, size - 2, size - 2);

        using (var path = Theme.RoundedRect(body, (int)Math.Round(7 * scale)))
        using (var brush = new LinearGradientBrush(body, Color.FromArgb(0x24, 0x2C, 0x2A), Color.FromArgb(0x14, 0x1A, 0x18), 60f))
        {
            g.FillPath(brush, path);
        }

        using (var ringPen = new Pen(Theme.Accent, Math.Max(1.6f, 3f * scale)))
        {
            float ringSize = 13f * scale;
            float ringLeft = (size - ringSize) / 2f;
            float ringTop = 7f * scale;
            g.DrawEllipse(ringPen, ringLeft, ringTop, ringSize, ringSize);
        }

        using (var stemBrush = new SolidBrush(Theme.Accent))
        {
            float stemWidth = Math.Max(2f, 4f * scale);
            float stemHeight = 9f * scale;
            g.FillRectangle(
                stemBrush,
                (size - stemWidth) / 2f,
                18f * scale,
                stemWidth,
                stemHeight);
        }

        return bitmap;
    }

    private static Icon Draw(int size)
    {
        using Bitmap bitmap = DrawBitmap(size);
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
