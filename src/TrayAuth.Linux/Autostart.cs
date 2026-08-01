namespace TrayAuth.Linux;

/// <summary>
/// Start-on-login via a freedesktop autostart entry - the Linux equivalent of the Windows
/// Run key: per-user, no elevation, plain file.
/// </summary>
public static class Autostart
{
    public static bool IsEnabled() => File.Exists(LinuxPaths.AutostartFile);

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                File.Delete(LinuxPaths.AutostartFile);
                return true;
            }

            string exe = Environment.ProcessPath ?? "trayauth";

            Directory.CreateDirectory(Path.GetDirectoryName(LinuxPaths.AutostartFile)!);
            File.WriteAllText(
                LinuxPaths.AutostartFile,
                $"""
                [Desktop Entry]
                Type=Application
                Name=TrayAuth
                Comment=Authenticator codes in your system tray
                Exec={exe}
                Icon=trayauth
                Terminal=false
                X-GNOME-Autostart-enabled=true
                """ + "\n");

            return true;
        }
        catch
        {
            return false;
        }
    }
}
