using System.Diagnostics;

namespace TrayAuth.Desktop;

/// <summary>
/// Runs a helper program and captures its output. The platform integrations here - keyring,
/// clipboard, launchd - are all reached through small OS command-line tools, so this is the
/// one place that deals with process plumbing, timeouts and missing binaries.
/// </summary>
internal static class ProcessRunner
{
    /// <summary>Runs the tool; returns stdout on success, null if it failed or is absent.</summary>
    public static string? Capture(string fileName, string[] args, string? stdin = null, int timeoutMs = 5000)
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

            using Process? process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            if (stdin is not null)
            {
                process.StandardInput.Write(stdin);
                process.StandardInput.Close();
            }

            // Read before waiting: a tool that fills the pipe buffer would otherwise deadlock
            // against our own WaitForExit.
            string output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Already gone.
                }

                return null;
            }

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            // Tool not installed, or no session available. Callers treat null as "unavailable".
            return null;
        }
    }

    /// <summary>True if the program is present on PATH.</summary>
    public static bool Exists(string program)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return false;
        }

        char separator = OperatingSystem.IsWindows() ? ';' : ':';

        foreach (string dir in path.Split(separator, StringSplitOptions.RemoveEmptyEntries))
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
}
