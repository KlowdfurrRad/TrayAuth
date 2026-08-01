using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ZXing;

namespace TrayAuth.Core;

/// <summary>
/// Finds and decodes QR codes in bitmaps - image files the user picks, or captures of the
/// desktop. ZXing does the finding; this class only feeds it pixels in the layout it expects.
/// </summary>
public static class QrDecoder
{
    public static IReadOnlyList<string> DecodeImageFile(string path)
    {
        using var bitmap = new Bitmap(path);
        return Decode(bitmap);
    }

    /// <summary>Captures every attached screen and returns the text of each QR found on any of them.</summary>
    public static IReadOnlyList<string> DecodeAllScreens()
    {
        var texts = new List<string>();

        foreach (Screen screen in Screen.AllScreens)
        {
            Rectangle bounds = screen.Bounds;

            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            texts.AddRange(Decode(bitmap));
        }

        return texts.Distinct().ToList();
    }

    public static IReadOnlyList<string> Decode(Bitmap bitmap)
    {
        byte[] pixels = CopyPixelsBgr32(bitmap, out int width, out int height);
        var source = new RGBLuminanceSource(pixels, width, height, RGBLuminanceSource.BitmapFormat.BGR32);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options =
            {
                TryHarder = true,
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
            },
        };

        // A screen capture can legitimately hold several QRs (Google Authenticator shows
        // multi-batch transfers side by side), so multi-decode first.
        Result[]? results = reader.DecodeMultiple(source);
        if (results is { Length: > 0 })
        {
            return results
                .Select(r => r.Text)
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct()
                .ToList();
        }

        Result? single = reader.Decode(source);
        return single is null || string.IsNullOrEmpty(single.Text) ? [] : [single.Text];
    }

    /// <summary>
    /// Renders the source to 32bpp and copies out a tightly packed BGR32 buffer. Going through a
    /// fresh bitmap normalises whatever pixel format the file loaded as (palettised PNGs
    /// included), and the row-by-row copy strips stride padding, which ZXing does not expect.
    /// </summary>
    private static byte[] CopyPixelsBgr32(Bitmap bitmap, out int width, out int height)
    {
        width = bitmap.Width;
        height = bitmap.Height;

        using var normalized = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(normalized))
        {
            graphics.DrawImage(bitmap, new Rectangle(0, 0, width, height));
        }

        var rect = new Rectangle(0, 0, width, height);
        BitmapData data = normalized.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);

        try
        {
            int rowBytes = width * 4;
            var buffer = new byte[rowBytes * height];

            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), buffer, y * rowBytes, rowBytes);
            }

            return buffer;
        }
        finally
        {
            normalized.UnlockBits(data);
        }
    }
}
