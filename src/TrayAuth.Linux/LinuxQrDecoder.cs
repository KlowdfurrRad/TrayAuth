using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;

namespace TrayAuth.Linux;

/// <summary>
/// QR decoding for image files: ImageSharp loads the pixels (no System.Drawing on Linux),
/// ZXing finds the codes - the same reader configuration as the Windows QrDecoder, so the
/// two platforms accept the same images.
/// </summary>
public static class LinuxQrDecoder
{
    public static IReadOnlyList<string> DecodeImageFile(string path)
    {
        using var image = Image.Load<Rgba32>(path);

        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);

        var source = new RGBLuminanceSource(
            pixels,
            image.Width,
            image.Height,
            RGBLuminanceSource.BitmapFormat.RGB32);

        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options =
            {
                TryHarder = true,
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
            },
        };

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
}
