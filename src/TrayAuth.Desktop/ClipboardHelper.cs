namespace TrayAuth.Desktop;

/// <summary>
/// Clipboard access that works from a background tray app.
///
/// Wayland only lets a focused window take the clipboard, and a tray-menu click focuses the
/// shell rather than us - so on Linux this shells out to wl-copy, which uses the data-control
/// protocol and needs no focus, falling back to the X11 tools. macOS has no such restriction
/// and pbcopy/pbpaste ship with the OS.
///
/// The 20-second auto-clear re-reads first and only clears if the clipboard still holds our
/// code, matching the Windows app's contract: it must never wipe something you copied since.
/// </summary>
public static class ClipboardHelper
{
    private sealed record Tool(
        string Copy,
        string[] CopyArgs,
        string Paste,
        string[] PasteArgs,
        string Clear,
        string[] ClearArgs,
        bool ClearsViaEmptyStdin);

    private static readonly Tool[] MacTools =
    [
        new("pbcopy", [], "pbpaste", [], "pbcopy", [], true),
    ];

    private static readonly Tool[] LinuxTools =
    [
        new("wl-copy", [], "wl-paste", ["-n"], "wl-copy", ["--clear"], false),
        new("xclip", ["-selection", "clipboard"], "xclip", ["-o", "-selection", "clipboard"],
            "xclip", ["-selection", "clipboard"], true),
        new("xsel", ["-ib"], "xsel", ["-ob"], "xsel", ["-bc"], false),
    ];

    private static Tool? _tool;
    private static bool _probed;

    /// <summary>Which tool is in use ("pbcopy", "wl-copy", ...); null if none was found.</summary>
    public static string? ActiveTool
    {
        get
        {
            Probe();
            return _tool?.Copy;
        }
    }

    public static bool TryCopy(string text)
    {
        Probe();
        return _tool is not null && ProcessRunner.Capture(_tool.Copy, _tool.CopyArgs, text) is not null;
    }

    public static string? TryRead()
    {
        Probe();
        return _tool is null ? null : ProcessRunner.Capture(_tool.Paste, _tool.PasteArgs);
    }

    /// <summary>Copies, then clears after <paramref name="clearAfterSeconds"/> if still ours.</summary>
    public static bool CopyWithAutoClear(string text, int clearAfterSeconds = 20)
    {
        if (!TryCopy(text))
        {
            return false;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(clearAfterSeconds)).ConfigureAwait(false);

            try
            {
                string? current = TryRead();
                if (current is not null && current.TrimEnd('\n', '\r') == text)
                {
                    Clear();
                }
            }
            catch
            {
                // Leaving the code on the clipboard is the only fallback.
            }
        });

        return true;
    }

    private static void Clear()
    {
        if (_tool is null)
        {
            return;
        }

        // Some tools have an explicit clear flag; the rest clear by being handed nothing.
        ProcessRunner.Capture(
            _tool.Clear,
            _tool.ClearArgs,
            _tool.ClearsViaEmptyStdin ? string.Empty : null);
    }

    private static void Probe()
    {
        if (_probed)
        {
            return;
        }

        _probed = true;

        foreach (Tool tool in OperatingSystem.IsMacOS() ? MacTools : LinuxTools)
        {
            if (ProcessRunner.Exists(tool.Copy) && ProcessRunner.Exists(tool.Paste))
            {
                _tool = tool;
                return;
            }
        }
    }
}
