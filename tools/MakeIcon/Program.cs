using System.Drawing.Imaging;
using TrayAuth.UI;

namespace TrayAuth.Tools;

/// <summary>
/// Writes assets/icon.ico from the same drawing code the app falls back to, so the tray icon, the
/// taskbar icon and the exe icon are the same mark.
///
/// The .ico container is assembled by hand: System.Drawing can save a PNG but cannot author a
/// multi-resolution icon, and Windows needs several sizes to render crisply everywhere from the
/// 16px tray to the 256px file view.
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    private static int Main(string[] args)
    {
        string output = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "icon.ico");

        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            WriteIcon(output);
            Console.WriteLine($"Wrote {Path.GetFullPath(output)} ({Sizes.Length} sizes).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to write the icon: {ex.Message}");
            return 1;
        }
    }

    private static void WriteIcon(string path)
    {
        List<byte[]> frames = [];

        foreach (int size in Sizes)
        {
            using Bitmap bitmap = AppIcon.DrawBitmap(size);
            using var buffer = new MemoryStream();
            bitmap.Save(buffer, ImageFormat.Png);
            frames.Add(buffer.ToArray());
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        // ICONDIR
        writer.Write((ushort)0);              // reserved
        writer.Write((ushort)1);              // type: icon
        writer.Write((ushort)frames.Count);

        int offset = 6 + (16 * frames.Count);

        for (int i = 0; i < frames.Count; i++)
        {
            int size = Sizes[i];

            // ICONDIRENTRY. 256px is encoded as 0, which is what the format specifies.
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);            // palette count
            writer.Write((byte)0);            // reserved
            writer.Write((ushort)1);          // colour planes
            writer.Write((ushort)32);         // bits per pixel
            writer.Write(frames[i].Length);
            writer.Write(offset);

            offset += frames[i].Length;
        }

        foreach (byte[] frame in frames)
        {
            writer.Write(frame);
        }
    }
}
