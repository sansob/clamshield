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
    /// <summary>False when the desktop has no tray: closing the window must then quit, or there is no way back.</summary>
    public static bool TrayAvailable;
    public static bool StartInBackground;
    MainWindow? _win;
    MainViewModel? _vm;
    IClassicDesktopStyleApplicationLifetime? _desktop;
    DispatcherTimer? _trim;

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

            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;   // the tray keeps the app alive when the window is gone
            _vm = new MainViewModel();
            // Started by autostart and living in the tray: no window at all until someone opens it.
            if (!(StartInBackground && _vm.RunInTray && !_vm.IsWizard)) ShowWindow();
            else _vm.WindowVisible = false;
            SetupTray(desktop);
            _trim = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
            _trim.Tick += (_, _) => { if (_win == null) TrimMemory(); };
            _trim.Start();

            // other launches (e.g. right-click "Scan with Clamshield") are forwarded to this running instance
            SingleInstance.StartServer(args => Dispatcher.UIThread.Post(() => HandleArgs(args)));
            desktop.Exit += (_, _) => SingleInstance.Stop();
            var first = SingleInstance.ParseScanPaths(desktop.Args ?? Array.Empty<string>());
            if (first.Length > 0) Dispatcher.UIThread.Post(() => HandleArgs(desktop.Args!));
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Shows the window, building it first if the app has been living in the tray without one.</summary>
    void ShowWindow()
    {
        if (_vm == null) return;
        if (_win == null)
        {
            var win = new MainWindow(_vm);
            _win = win;
            if (_desktop?.MainWindow == null) _desktop!.MainWindow = win;
            win.Closed += (_, _) =>
            {
                if (_win == win) _win = null;
                if (_desktop?.MainWindow == win) _desktop.MainWindow = null;
                if (Quitting) _desktop?.Shutdown(); else TrimMemory();
            };
            win.Show();
        }
        else
        {
            _win.Show();
            if (_win.WindowState == WindowState.Minimized) _win.WindowState = WindowState.Normal;
        }
        _win.Activate();
        _vm.WindowVisible = true;
    }

    /// <summary>Hands memory back to the system after the window has gone, so the idle app stays small.</summary>
    static void TrimMemory() => _ = Task.Delay(1500).ContinueWith(_ =>
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    });

    void HandleArgs(string[] args)
    {
        if (_vm == null) return;
        ShowWindow();
        var paths = SingleInstance.ParseScanPaths(args);
        if (paths.Length == 0 || _vm.IsWizard) return;
        _vm.Navigate(AppPage.Scan);
        _ = _vm.ScanPaths(paths);
    }

    void SetupTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Clamshield/Assets/icon.png")));
            var menu = new NativeMenu();
            var open = new NativeMenuItem("Open Clamshield");
            open.Click += (_, _) => Dispatcher.UIThread.Post(ShowWindow);
            var quick = new NativeMenuItem("Quick Scan");
            quick.Click += (_, _) => Dispatcher.UIThread.Post(async () => { ShowWindow(); await _vm!.QuickScan(); });
            var pause = new NativeMenuItem(Loc.T("Pause for 15 minutes"));
            pause.Click += (_, _) => Dispatcher.UIThread.Post(() => _vm!.PauseProtection(15));
            var resume = new NativeMenuItem(Loc.T("Resume protection"));
            resume.Click += (_, _) => Dispatcher.UIThread.Post(() => _vm!.ResumeProtection());
            var quit = new NativeMenuItem("Quit");
            quit.Click += (_, _) => { Quitting = true; _vm!.Shutdown(); desktop.Shutdown(); };
            menu.Add(open); menu.Add(quick); menu.Add(pause); menu.Add(resume); menu.Add(new NativeMenuItemSeparator()); menu.Add(quit);

            var tray = new TrayIcon { Icon = icon, ToolTipText = "Clamshield — protected", Menu = menu };
            tray.Clicked += (_, _) => Dispatcher.UIThread.Post(ShowWindow);
            TrayIcon.SetIcons(this, new TrayIcons { tray });
            TrayAvailable = true;
        }
        catch { /* no tray available on this desktop */ }
    }
}
