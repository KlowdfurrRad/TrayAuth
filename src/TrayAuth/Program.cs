using TrayAuth.Core;
using TrayAuth.Interop;
using TrayAuth.UI;

namespace TrayAuth;

internal static class Program
{
    /// <summary>Named per-session so one user's instance never blocks another's on shared machines.</summary>
    private const string MutexName = @"Local\TrayAuth.SingleInstance.v1";

    private const string ShowMessageName = "TrayAuth.ShowPanel.4C1F0C9E";

    [STAThread]
    private static void Main(string[] args)
    {
        // Utility mode, invoked by the desktop context-menu entries: do the one job and exit,
        // deliberately bypassing the single-instance machinery so it works whether or not the
        // tray app is running.
        if (args.Length >= 2 && string.Equals(args[0], "--copy", StringComparison.Ordinal))
        {
            RunCopyMode(args[1]);
            return;
        }

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
    /// Copies the current code for one account to the clipboard, honouring the same 20-second
    /// auto-clear contract as in-app copies - this process simply outlives the click by that
    /// long, invisibly, then leaves. The code is computed here and now, never taken from the
    /// menu label that launched us, so a stale label can never cause a stale copy.
    /// </summary>
    private static void RunCopyMode(string accountId)
    {
        try
        {
            var vault = new Core.Vault();
            vault.Load();

            Account? account = vault.Find(accountId);
            if (account is null)
            {
                return;
            }

            string code = account.Generate().Code;

            bool copied = false;
            for (int attempt = 0; attempt < 3 && !copied; attempt++)
            {
                try
                {
                    Clipboard.SetText(code);
                    copied = true;
                }
                catch
                {
                    Thread.Sleep(60);
                }
            }

            if (!copied)
            {
                return;
            }

            Thread.Sleep(20_000);

            try
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == code)
                {
                    Clipboard.Clear();
                }
            }
            catch
            {
                // Another process holds the clipboard; leaving the code is the only option.
            }
        }
        catch
        {
            // Utility mode has no UI to complain in; failing silently beats a stray dialog.
        }
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
