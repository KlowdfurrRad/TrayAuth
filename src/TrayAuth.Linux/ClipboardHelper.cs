using System.Diagnostics;

namespace TrayAuth.Linux;

/// <summary>
/// Clipboard access that works from a background tray app on Wayland.
///
/// Wayland only lets a focused window take the clipboard, and a tray-menu click gives focus to
/// the shell, not to us - so this shells out to wl-copy (which uses the data-control protocol,
/// no focus needed), falling back to the X11 tools under Xorg. The 20-second auto-clear
/// re-reads first and only clears if the clipboard still holds our code, the same contract as
/// the Windows app.
/// </summary>
public static class ClipboardHelper
{
    private sealed record Tool(string Copy, string[] CopyArgs, string Paste, string[] PasteArgs, string Clear, string[] ClearArgs);

    private static readonly Tool[] Tools =
    [
        new("wl-copy", [], "wl-paste", ["-n"], "wl-copy", ["--clear"]),
        new("xclip", ["-selection", "clipboard"], "xclip", ["-o", "-selection", "clipboard"], "xclip", ["-selection", "clipboard"]),
        new("xsel", ["-ib"], "xsel", ["-ob"], "xsel", ["-bc"]),
    ];

    private static Tool? _tool;
    private static bool _probed;

    /// <summary>Which tool is in use, for diagnostics ("wl-copy", "xclip", ...); null if none found.</summary>
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
        if (_tool is null)
        {
            return false;
        }

        return Run(_tool.Copy, _tool.CopyArgs, text) is not null;
    }

    public static string? TryRead()
    {
        Probe();
        if (_tool is null)
        {
            return null;
        }

        return Run(_tool.Paste, _tool.PasteArgs, null);
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
                if (current is not null && current.TrimEnd('\n') == text)
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

        if (_tool.Clear == "wl-copy")
        {
            Run(_tool.Clear, _tool.ClearArgs, null);
        }
        else
        {
            // The X11 tools clear by copying an empty selection.
            Run(_tool.Clear, _tool.ClearArgs, string.Empty);
        }
    }

    private static void Probe()
    {
        if (_probed)
        {
            return;
        }

        _probed = true;

        foreach (Tool tool in Tools)
        {
            if (Exists(tool.Copy) && Exists(tool.Paste))
            {
                _tool = tool;
                return;
            }
        }
    }

    private static bool Exists(string program)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return false;
        }

        foreach (string dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(dir, program)))
                {
                    return true;
                }
            }
            catch
            {
                // Malformed PATH entry.
            }
        }

        return false;
    }

    private static string? Run(string fileName, string[] args, string? stdin)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null,
                UseShellExecute = false,
            };

            foreach (string arg in args)
            {
                info.ArgumentList.Add(arg);
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }

            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { }
                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
