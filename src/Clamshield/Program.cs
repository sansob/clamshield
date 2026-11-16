using Avalonia;
using System;

namespace Clamshield;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--cli")) return Clamshield.Services.Cli.Run(args);
        // Already running? Hand over the request (e.g. "--scan file") and exit.
        if (Environment.GetEnvironmentVariable("CLAMSHIELD_SHOTS") == null && Clamshield.Services.SingleInstance.TrySendToRunning(args)) return 0;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = Clamshield.Services.LinuxIntegration.AppId })
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
