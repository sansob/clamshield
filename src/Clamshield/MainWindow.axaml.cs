using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Clamshield.Models;
using Clamshield.Services;
using Clamshield.ViewModels;

namespace Clamshield;

public partial class MainWindow : Window
{
    public MainViewModel Vm { get; } = new();
    WindowNotificationManager? _toasts;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = Vm;
        SizeChanged += (_, e) => ApplyResponsive(e.NewSize.Width);
        if (Environment.GetEnvironmentVariable("CLAMSHIELD_SIZE") is { Length: > 0 } sz && sz.Split('x') is [var w, var h])
        { Width = double.Parse(w); Height = double.Parse(h); WindowStartupLocation = WindowStartupLocation.Manual; }

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, (_, e) => { Vm.IsDragOver = true; e.DragEffects = DragDropEffects.Copy; });
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => Vm.IsDragOver = false);
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            Vm.IsDragOver = false;
            var items = e.DataTransfer.TryGetFiles();
            var paths = items?.Select(i => i.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToList();
            if (paths is { Count: > 0 }) { Vm.Navigate(AppPage.Scan); await Vm.ScanPaths(paths); }
        });

        Opened += (_, _) =>
        {
            if (Environment.GetEnvironmentVariable("CLAMSHIELD_SHOTS") is { Length: > 0 } shotDir) _ = RunShots(shotDir);
            if (App.StartInBackground && Vm.RunInTray && !Vm.IsWizard) { Hide(); Vm.WindowVisible = false; }
            _toasts = new WindowNotificationManager(this) { Position = NotificationPosition.BottomRight, MaxItems = 3 };
            Vm.Toast += (title, msg, warn) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    _toasts?.Show(new Notification(title, msg, warn ? NotificationType.Warning : NotificationType.Success)));
        };
    }

    /// <summary>Wide windows get more tiles per row so cards don't stretch into mostly empty space.</summary>
    void ApplyResponsive(double width)
    {
        var wide = width >= 1300;
        if (this.FindControl<UniformGrid>("ScanTilesGrid") is { } scan) scan.Columns = wide ? 4 : 2;
        if (this.FindControl<UniformGrid>("ToolTilesGrid") is { } tools) tools.Columns = wide ? 4 : 3;
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Vm.WindowVisible = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.Quitting && Vm.RunInTray)
        {
            e.Cancel = true;      // keep protecting in the background
            Hide();
            Vm.WindowVisible = false;
            return;
        }
        Vm.Shutdown();
        base.OnClosing(e);
    }

    void OnWizardNext(object? s, RoutedEventArgs e) => Vm.WizardNext();
    void OnWizardRetry(object? s, RoutedEventArgs e) => Vm.WizardRetry();
    void OnWizardFinish(object? s, RoutedEventArgs e) => Vm.WizardFinish();

    void NavHome(object? s, RoutedEventArgs e) => Vm.Navigate(AppPage.Home);
    void NavScan(object? s, RoutedEventArgs e) => Vm.Navigate(AppPage.Scan);
    void NavUpdate(object? s, RoutedEventArgs e) => Vm.Navigate(AppPage.Update);
    void NavSettings(object? s, RoutedEventArgs e) => Vm.Navigate(AppPage.Settings);
    void OnGoRealTimeSettings(object? s, RoutedEventArgs e) => Vm.OpenSettings(1);
    void OnOpenRtLog(object? s, RoutedEventArgs e) => Vm.OpenRtLogFile();
    void OnClearRtLog(object? s, RoutedEventArgs e) => Vm.ClearRtLog();
    void NavTools(object? s, RoutedEventArgs e) { Vm.ToolView = 0; Vm.Navigate(AppPage.Tools); }
    void OnOpenToolVt(object? s, RoutedEventArgs e) => Vm.ToolView = 1;
    void OnOpenToolShred(object? s, RoutedEventArgs e) => Vm.ToolView = 2;
    void OnOpenToolLink(object? s, RoutedEventArgs e) => Vm.ToolView = 3;
    void OnOpenToolPassword(object? s, RoutedEventArgs e) => Vm.ToolView = 4;
    void OnOpenToolStartup(object? s, RoutedEventArgs e) => Vm.ToolView = 5;
    void OnOpenToolClean(object? s, RoutedEventArgs e) => Vm.ToolView = 6;
    async void OnCheckLink(object? s, RoutedEventArgs e) => await Vm.CheckLink();
    async void OnCheckPassword(object? s, RoutedEventArgs e) => await Vm.CheckPassword();
    async void OnStartupRefresh(object? s, RoutedEventArgs e) => await Vm.LoadStartup();
    async void OnCleanRun(object? s, RoutedEventArgs e) => await Vm.RunCleanup();
    async void OnCleanAgain(object? s, RoutedEventArgs e) => await Vm.AnalyzeCleanup();
    void OnPause15(object? s, RoutedEventArgs e) => Vm.PauseProtection(15);
    void OnPause60(object? s, RoutedEventArgs e) => Vm.PauseProtection(60);
    void OnPauseRestart(object? s, RoutedEventArgs e) => Vm.PauseProtection(0);
    void OnResumeProtection(object? s, RoutedEventArgs e) => Vm.ResumeProtection();
    void OnOpenToolBlock(object? s, RoutedEventArgs e) => Vm.ToolView = 7;
    void OnOpenToolEngine(object? s, RoutedEventArgs e) => Vm.ToolView = 8;
    async void OnEngineRefresh(object? s, RoutedEventArgs e) => await Vm.LoadEngineDetails();
    async void OnEngineReload(object? s, RoutedEventArgs e) => await Vm.ReloadEngine();
    async void OnBlockChoose(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose a file to block", AllowMultiple = true });
        foreach (var f in files) if (f.TryGetLocalPath() is { } p) Vm.BlockFile(p);
    }
    void OnUnblock(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is BlockEntry b) Vm.Unblock(b); }
    async void OnYaraAdd(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a YARA rule file", AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("YARA rules") { Patterns = new[] { "*.yar", "*.yara" } } },
        });
        foreach (var f in files) if (f.TryGetLocalPath() is { } p) await Vm.AddYara(p);
    }
    void OnYaraRemove(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is YaraRule r) Vm.RemoveYara(r); }
    async void OnDefsUpdate(object? s, RoutedEventArgs e) { Vm.Navigate(AppPage.Update); await Vm.UpdateDefinitions(); }
    void OnToolsBack(object? s, RoutedEventArgs e) => Vm.ToolView = 0;
    void OnVtAddKey(object? s, RoutedEventArgs e) => Vm.OpenSettings(2);
    void OnVtOpenLink(object? s, RoutedEventArgs e) => Vm.OpenVtLink();
    void OnTabGeneral(object? s, RoutedEventArgs e) => Vm.SettingsTab = 0;
    void OnTabProtection(object? s, RoutedEventArgs e) => Vm.SettingsTab = 1;
    void OnTabScanning(object? s, RoutedEventArgs e) => Vm.SettingsTab = 2;
    void OnTabQuarantine(object? s, RoutedEventArgs e) => Vm.SettingsTab = 3;
    void OnTabHistory(object? s, RoutedEventArgs e) => Vm.SettingsTab = 4;
    void OnTabAbout(object? s, RoutedEventArgs e) => Vm.SettingsTab = 5;
    void OnOpenQuarantine(object? s, RoutedEventArgs e) => Vm.OpenSettings(3);

    async void OnSmartScan(object? s, RoutedEventArgs e) => await Vm.SmartScan();
    async void OnQuickScan(object? s, RoutedEventArgs e) => await Vm.QuickScan();
    async void OnFullScan(object? s, RoutedEventArgs e) => await Vm.FullScan();
    async void OnProcessScan(object? s, RoutedEventArgs e) => await Vm.ProcessScan();
    void OnNewScan(object? s, RoutedEventArgs e) => Vm.ResetScan();
    void OnCancel(object? s, RoutedEventArgs e) => Vm.CancelScan();
    async void OnUpdate(object? s, RoutedEventArgs e) => await Vm.UpdateDefinitions();

    async void OnCustomScan(object? s, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a folder to scan", AllowMultiple = false });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path != null) await Vm.CustomScan(path);
    }

    async void OnAddExcludeFolder(object? s, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder to exclude", AllowMultiple = true });
        foreach (var f in folders) if (f.TryGetLocalPath() is { } p) Vm.AddExclusion(p);
    }

    async void OnAddExcludeFile(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "File to exclude", AllowMultiple = true });
        foreach (var f in files) if (f.TryGetLocalPath() is { } p) Vm.AddExclusion(p);
    }

    void OnRemoveExclusion(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is string p) Vm.RemoveExclusion(p); }

    async void OnExport(object? s, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export report",
            SuggestedFileName = $"clamshield-report-{DateTime.Now:yyyyMMdd-HHmm}.html",
            DefaultExtension = "html",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Web page (can be printed to PDF)") { Patterns = new[] { "*.html" } },
                new FilePickerFileType("Plain text") { Patterns = new[] { "*.txt" } },
            },
        });
        if (file == null) return;
        var text = file.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? Vm.BuildReport() : Vm.BuildHtmlReport();
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await using var w = new StreamWriter(stream);
        await w.WriteAsync(text);
    }

    // ----- health, profiles, watch folders, trusted files
    void OnFixHealth(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is HealthIssue i) Vm.FixHealth(i); }

    async void OnAddProfile(object? s, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose the folders for this profile", AllowMultiple = true });
        Vm.AddProfile(folders.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>());
    }
    async void OnRunProfile(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is ScanProfile p) await Vm.RunProfile(p); }
    void OnDeleteProfile(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is ScanProfile p) Vm.RemoveProfile(p); }

    async void OnAddWatchFolder(object? s, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder to watch", AllowMultiple = true });
        foreach (var f in folders) if (f.TryGetLocalPath() is { } p) Vm.AddWatchFolder(p);
    }
    void OnRemoveWatchFolder(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is string p) Vm.RemoveWatchFolder(p); }
    void OnRemoveTrusted(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is TrustedEntry t) Vm.RemoveTrusted(t); }

    // ----- online check (VirusTotal) and shredder
    async void OnCheckThreat(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is ThreatItem t) await Vm.CheckOnline(t.Path); }
    async void OnCheckQuarantined(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is QuarantineItem q) await Vm.CheckOnline(QuarantineService.StoredPath(q), q.FileName); }
    void OnRestoreTrust(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is QuarantineItem q) Vm.RestoreAndTrust(q); }

    async void OnCheckFileOnline(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose a file to check", AllowMultiple = false });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } p) await Vm.CheckOnline(p);
    }
    async void OnShredAddFiles(object? s, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Files to erase", AllowMultiple = true });
        Vm.AddShred(files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>());
    }
    async void OnShredAddFolder(object? s, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Folder to erase", AllowMultiple = true });
        Vm.AddShred(folders.Select(f => f.TryGetLocalPath()).Where(p => p != null).Cast<string>());
    }
    void OnShredClear(object? s, RoutedEventArgs e) => Vm.ClearShred();
    async void OnShredNow(object? s, RoutedEventArgs e) => await Vm.ShredNow();

    void OnQuarantineAll(object? s, RoutedEventArgs e) => Vm.QuarantineAll();
    void OnRestore(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is QuarantineItem q) Vm.Restore(q); }
    void OnDelete(object? s, RoutedEventArgs e) { if ((s as Control)?.DataContext is QuarantineItem q) Vm.DeleteQuarantined(q); }
}
