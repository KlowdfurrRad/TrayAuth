using Avalonia;

namespace TrayAuth.Linux;

public static class Program
{
    public static int Main(string[] args)
    {
        // Utility modes exit before any GUI machinery spins up, so they work over SSH.
        if (args.Contains("--selftest"))
        {
            return SelfTest.Run();
        }

        if (args.Contains("--version"))
        {
            Console.WriteLine("TrayAuth for Linux " + (typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?"));
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
