using Microsoft.Win32;

namespace TrayAuth.Core;

/// <summary>
/// Start-with-Windows, via the per-user Run key. Per-user means no elevation and no scheduled task,
/// and it is the same mechanism the installer writes — so toggling it here and reinstalling agree.
/// </summary>
public static class StartupRegistration
{
    public const string ValueName = "TrayAuth";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// ProcessPath is the right answer for a single-file app, where the assembly has no location
    /// on disk of its own.
    /// </summary>
    public static string CurrentExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TrayAuth.exe");

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{CurrentExecutablePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
