namespace TrayAuth.Desktop;

/// <summary>
/// Start-on-login, per platform: a launchd LaunchAgent on macOS, a freedesktop autostart
/// entry on Linux. Both are per-user plain files - the equivalent of the Windows Run key,
/// with no elevation and nothing to uninstall centrally.
/// </summary>
public static class Autostart
{
    public static bool IsEnabled() => File.Exists(AppPaths.AutostartFile);

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            string path = AppPaths.AutostartFile;

            if (!enabled)
            {
                if (AppPaths.IsMac && File.Exists(path))
                {
                    // Tell launchd to forget it now; otherwise it stays loaded until logout.
                    ProcessRunner.Capture("launchctl", ["unload", path]);
                }

                File.Delete(path);
                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, AppPaths.IsMac ? BuildLaunchAgent() : BuildDesktopEntry());

            if (AppPaths.IsMac)
            {
                // Load it immediately so the setting takes effect without a logout.
                ProcessRunner.Capture("launchctl", ["load", path]);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildLaunchAgent() =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>{AppPaths.LaunchAgentLabel}</string>
            <key>ProgramArguments</key>
            <array>
                <string>{System.Security.SecurityElement.Escape(AppPaths.ExecutablePath)}</string>
            </array>
            <key>RunAtLoad</key>
            <true/>
            <key>KeepAlive</key>
            <false/>
            <key>ProcessType</key>
            <string>Interactive</string>
        </dict>
        </plist>
        """ + "\n";

    private static string BuildDesktopEntry() =>
        $"""
        [Desktop Entry]
        Type=Application
        Name=TrayAuth
        Comment=Authenticator codes in your system tray
        Exec={AppPaths.ExecutablePath}
        Icon=trayauth
        Terminal=false
        X-GNOME-Autostart-enabled=true
        """ + "\n";
}
