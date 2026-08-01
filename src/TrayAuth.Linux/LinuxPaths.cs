namespace TrayAuth.Linux;

/// <summary>
/// Where TrayAuth keeps its state on Linux. XDG conventions, with a TRAYAUTH_CONFIG_DIR
/// override so the selftest (and any test) can run against a throwaway directory.
/// </summary>
public static class LinuxPaths
{
    public static string ConfigDir
    {
        get
        {
            string? overridden = Environment.GetEnvironmentVariable("TRAYAUTH_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(overridden))
            {
                return overridden;
            }

            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string baseDir = !string.IsNullOrWhiteSpace(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            return Path.Combine(baseDir, "trayauth");
        }
    }

    public static string VaultFile => Path.Combine(ConfigDir, "vault.dat");

    public static string KeyFile => Path.Combine(ConfigDir, "vault.key");

    public static string AutostartFile
    {
        get
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string baseDir = !string.IsNullOrWhiteSpace(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            return Path.Combine(baseDir, "autostart", "trayauth.desktop");
        }
    }
}
