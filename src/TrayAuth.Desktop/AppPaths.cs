namespace TrayAuth.Desktop;

/// <summary>
/// Where TrayAuth keeps its state, following each platform's own convention: XDG on Linux,
/// Application Support on macOS. TRAYAUTH_CONFIG_DIR overrides both, which is how the
/// selftest runs against a throwaway directory.
/// </summary>
public static class AppPaths
{
    public const string LaunchAgentLabel = "io.github.klowdfurrrad.trayauth";

    public static bool IsMac => OperatingSystem.IsMacOS();

    public static string ConfigDir
    {
        get
        {
            string? overridden = Environment.GetEnvironmentVariable("TRAYAUTH_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return overridden;
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (IsMac)
            {
                return Path.Combine(home, "Library", "Application Support", "TrayAuth");
            }

            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string baseDir = !string.IsNullOrWhiteSpace(xdg) ? xdg : Path.Combine(home, ".config");
            return Path.Combine(baseDir, "trayauth");
        }
    }

    public static string VaultFile => Path.Combine(ConfigDir, "vault.dat");

    public static string KeyFile => Path.Combine(ConfigDir, "vault.key");

    /// <summary>
    /// Autostart entry: a LaunchAgent plist on macOS, a freedesktop .desktop file on Linux.
    /// </summary>
    public static string AutostartFile
    {
        get
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (IsMac)
            {
                return Path.Combine(home, "Library", "LaunchAgents", LaunchAgentLabel + ".plist");
            }

            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string baseDir = !string.IsNullOrWhiteSpace(xdg) ? xdg : Path.Combine(home, ".config");
            return Path.Combine(baseDir, "autostart", "trayauth.desktop");
        }
    }

    /// <summary>
    /// The executable to launch at login. Inside a macOS .app the running process is
    /// TrayAuth.app/Contents/MacOS/trayauth, which is what launchd should start.
    /// </summary>
    public static string ExecutablePath => Environment.ProcessPath ?? "trayauth";
}
