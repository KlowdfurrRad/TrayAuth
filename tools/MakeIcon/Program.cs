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

    /// <summary>
    /// The Apple icon-suite types we emit, each carrying a PNG. macOS picks whichever size it
    /// needs; these cover the menu bar through to Finder's largest preview.
    /// </summary>
    private static readonly (string Type, int Size)[] IcnsEntries =
    [
        ("icp4", 16),
        ("icp5", 32),
        ("icp6", 64),
        ("ic07", 128),
        ("ic08", 256),
        ("ic09", 512),
        ("ic10", 1024),
    ];

    private static int Main(string[] args)
    {
        string output = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "icon.ico");

        try
        {
            string full = Path.GetFullPath(output);
            string? directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (full.EndsWith(".icns", StringComparison.OrdinalIgnoreCase))
            {
                WriteIcns(full);
                Console.WriteLine($"Wrote {full} ({IcnsEntries.Length} sizes).");
            }
            else
            {
                WriteIcon(full);
                Console.WriteLine($"Wrote {full} ({Sizes.Length} sizes).");
            }

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

    /// <summary>
    /// Writes an Apple .icns. The container is simple - a header, then one length-prefixed
    /// chunk per size - and every modern reader accepts PNG payloads, so this needs no macOS
    /// tooling. All integers are big-endian, unlike .ico.
    /// </summary>
    private static void WriteIcns(string path)
    {
        var chunks = new List<(string Type, byte[] Png)>();

        foreach ((string type, int size) in IcnsEntries)
        {
            using Bitmap bitmap = AppIcon.DrawBitmap(size);
            using var buffer = new MemoryStream();
            bitmap.Save(buffer, ImageFormat.Png);
            chunks.Add((type, buffer.ToArray()));
        }

        // 8-byte file header, then 8 bytes of chunk header per entry.
        int totalLength = 8 + chunks.Sum(c => 8 + c.Png.Length);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write(System.Text.Encoding.ASCII.GetBytes("icns"));
        WriteBigEndian(writer, totalLength);

        foreach ((string type, byte[] png) in chunks)
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes(type));
            WriteBigEndian(writer, 8 + png.Length);
            writer.Write(png);
        }
    }

    private static void WriteBigEndian(BinaryWriter writer, int value)
    {
        writer.Write((byte)((value >> 24) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }
}
