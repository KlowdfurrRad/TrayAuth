using TrayAuth.Interop;
using TrayAuth.UI;

namespace TrayAuth;

internal static class Program
{
    /// <summary>Named per-session so one user's instance never blocks another's on shared machines.</summary>
    private const string MutexName = @"Local\TrayAuth.SingleInstance.v1";

    private const string ShowMessageName = "TrayAuth.ShowPanel.4C1F0C9E";

    [STAThread]
    private static void Main()
    {
        uint showPanelMessage = NativeMethods.RegisterWindowMessage(ShowMessageName);

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // Already running. Ask the live instance to show its panel and get out of the way,
            // so launching the shortcut again summons the codes rather than a second tray icon.
            if (showPanelMessage != 0)
            {
                NativeMethods.PostMessageW(NativeMethods.HWND_BROADCAST, showPanelMessage, IntPtr.Zero, IntPtr.Zero);
            }

            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => ReportFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);

        using var context = new TrayContext(showPanelMessage);
        Application.Run(context);

        GC.KeepAlive(mutex);
    }

    /// <summary>
    /// A tray app has no window to show a stack trace in, so an unhandled exception would otherwise
    /// vanish along with the icon. Say what happened before going.
    /// </summary>
    private static void ReportFatal(Exception? exception)
    {
        try
        {
            MessageBox.Show(
                $"TrayAuth hit an unexpected error and has to close.\r\n\r\n{exception?.Message}\r\n\r\n{exception?.StackTrace}",
                "TrayAuth",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // Nothing left to try.
        }
    }
}
