using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Clamshield.Services;
using Clamshield.ViewModels;

namespace Clamshield;

public partial class App : Application
{
    public static bool Quitting;
    public static bool StartInBackground;
    MainWindow? _win;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppData.Harden();
            _ = Task.Run(LinuxIntegration.EnsureDesktopEntry);
            var st = SettingsService.Load();
            Loc.I.Language = st.Language;
            MainViewModel.ApplyTheme(st.Theme);
            StartInBackground = SingleInstance.HasFlag(desktop.Args ?? Array.Empty<string>(), "--background");

            _win = new MainWindow();
            desktop.MainWindow = _win;
            SetupTray(desktop);

            // other launches (e.g. right-click "Scan with Clamshield") are forwarded to this running instance
            SingleInstance.StartServer(args => Dispatcher.UIThread.Post(() => HandleArgs(args)));
            desktop.Exit += (_, _) => SingleInstance.Stop();
            var first = SingleInstance.ParseScanPaths(desktop.Args ?? Array.Empty<string>());
            if (first.Length > 0) Dispatcher.UIThread.Post(() => HandleArgs(desktop.Args!));
        }
        base.OnFrameworkInitializationCompleted();
    }

    void HandleArgs(string[] args)
    {
        if (_win == null) return;
        _win.ShowFromTray();
        var paths = SingleInstance.ParseScanPaths(args);
        if (paths.Length == 0 || _win.Vm.IsWizard) return;
        _win.Vm.Navigate(AppPage.Scan);
        _ = _win.Vm.ScanPaths(paths);
    }

    void SetupTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Clamshield/Assets/icon.png")));
            var menu = new NativeMenu();
            var open = new NativeMenuItem("Open Clamshield");
            open.Click += (_, _) => _win!.ShowFromTray();
            var quick = new NativeMenuItem("Quick Scan");
            quick.Click += async (_, _) => { _win!.ShowFromTray(); await _win.Vm.QuickScan(); };
            var pause = new NativeMenuItem(Loc.T("Pause for 15 minutes"));
            pause.Click += (_, _) => Dispatcher.UIThread.Post(() => _win!.Vm.PauseProtection(15));
            var resume = new NativeMenuItem(Loc.T("Resume protection"));
            resume.Click += (_, _) => Dispatcher.UIThread.Post(() => _win!.Vm.ResumeProtection());
            var quit = new NativeMenuItem("Quit");
            quit.Click += (_, _) => { Quitting = true; _win!.Vm.Shutdown(); desktop.Shutdown(); };
            menu.Add(open); menu.Add(quick); menu.Add(pause); menu.Add(resume); menu.Add(new NativeMenuItemSeparator()); menu.Add(quit);

            var tray = new TrayIcon { Icon = icon, ToolTipText = "Clamshield — protected", Menu = menu };
            tray.Clicked += (_, _) => _win!.ShowFromTray();
            TrayIcon.SetIcons(this, new TrayIcons { tray });
        }
        catch { /* no tray available on this desktop */ }
    }
}
