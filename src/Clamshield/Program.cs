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
            .With(new X11PlatformOptions
            {
                WmClass = Clamshield.Services.LinuxIntegration.AppId,
                // Software drawing: the GL path loads Mesa and LLVM, about 60 MB the app does not need.
                // CLAMSHIELD_GL=1 switches the GPU path back on for people who prefer smoother animation to memory.
                RenderingMode = Environment.GetEnvironmentVariable("CLAMSHIELD_GL") == "1"
                    ? new[] { X11RenderingMode.Glx, X11RenderingMode.Software }
                    : new[] { X11RenderingMode.Software },
            })
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
