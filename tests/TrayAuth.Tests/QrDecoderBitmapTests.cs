using TrayAuth.Core;
using Xunit;
using static TrayAuth.Tests.MigrationTestData;

namespace TrayAuth.Tests;

/// <summary>
/// The System.Drawing half of the QR story - Windows-only because the decoder feeds ZXing from
/// GDI+ bitmaps. The Linux app has its own decoder with its own adapter.
/// </summary>
public class QrDecoderBitmapTests
{
    [Fact]
    public void QrRoundTrip_MigrationUriComesBackOutOfItsOwnPng()
    {
        // Encode with QRCoder (the export path), decode with ZXing (the import path). If these
        // two libraries agree, a QR we render is scannable - and the import path is proven
        // end to end without a phone in the loop.
        string uri = MigrationUri([OtpEntry(KnownSecret, "GitHub:raadhes@gmail.com", "GitHub")]);
        byte[] png = ExportService.RenderQrPng(uri);

        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);

        IReadOnlyList<string> texts = QrDecoder.Decode(bitmap);
        Assert.Contains(uri, texts);
    }

    [Fact]
    public void QrPastedIntoALargeImage_IsStillFound()
    {
        // The screen-scan case: a QR somewhere on a big mostly-empty surface.
        string uri = "otpauth://totp/GitHub:a%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitHub";
        byte[] png = ExportService.RenderQrPng(uri);

        using var qrStream = new MemoryStream(png);
        using var qr = new Bitmap(qrStream);
        using var screen = new Bitmap(1200, 800);

        using (var graphics = Graphics.FromImage(screen))
        {
            graphics.Clear(Color.White);
            graphics.DrawImageUnscaled(qr, 700, 320);
        }

        IReadOnlyList<string> texts = QrDecoder.Decode(screen);
        Assert.Contains(uri, texts);
    }

    [Fact]
    public void TwoQrsInOneImage_AreBothFound()
    {
        // Google Authenticator shows batch QRs one after another; a screenshot may hold two.
        string first = MigrationUri([OtpEntry(KnownSecret, "GitHub:a", "GitHub")], batchSize: 2, batchIndex: 0);
        string second = MigrationUri([OtpEntry(KnownSecret, "AWS:root", "AWS")], batchSize: 2, batchIndex: 1);

        using var canvas = new Bitmap(1400, 600);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(Color.White);

            using var qr1Stream = new MemoryStream(ExportService.RenderQrPng(first));
            using var qr1 = new Bitmap(qr1Stream);
            graphics.DrawImageUnscaled(qr1, 80, 100);

            using var qr2Stream = new MemoryStream(ExportService.RenderQrPng(second));
            using var qr2 = new Bitmap(qr2Stream);
            graphics.DrawImageUnscaled(qr2, 800, 100);
        }

        IReadOnlyList<string> texts = QrDecoder.Decode(canvas);

        Assert.Contains(first, texts);
        Assert.Contains(second, texts);
    }

    [Fact]
    public void BlankImage_FindsNothing()
    {
        using var blank = new Bitmap(600, 400);
        using (var graphics = Graphics.FromImage(blank))
        {
            graphics.Clear(Color.White);
        }

        Assert.Empty(QrDecoder.Decode(blank));
    }
}
