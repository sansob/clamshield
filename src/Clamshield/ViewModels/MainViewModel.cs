using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Media;
using Avalonia.Threading;
using Clamshield.Models;
using Clamshield.Services;

namespace Clamshield.ViewModels;

public enum AppPage { Home, Scan, Tools, Update, Settings }

public partial class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(f, v)) return;
        f = v; PropertyChanged?.Invoke(this, new(n));
    }
    void Raise(params string[] names) { foreach (var n in names) PropertyChanged?.Invoke(this, new(n)); }

    static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
    static readonly IBrush Green = B("#0FB872"), Amber = B("#FF7800"), Red = B("#E5364B");
    static IBrush G(string a, string b) => new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(a), 0), new GradientStop(Color.Parse(b), 1) }
    };
    static readonly IBrush GGray = G("#9AA3B5", "#6B7488");
    static readonly IBrush GGreen = G("#22D08A", "#0A9D69"), GAmber = G("#FFA24D", "#FF6A00"), GRed = G("#FF6B6F", "#D12A3F");

    readonly AppSettings S = SettingsService.Load();
    readonly RealTimeService _rt = new();
    readonly DriveWatcher _drives = new();
    readonly Queue<(string root, string name)> _usb = new();
    readonly DispatcherTimer _sched = new() { Interval = TimeSpan.FromSeconds(30) };

    /// <summary>Set by the window; used to decide whether to show native notifications.</summary>
    public bool WindowVisible { get; set; } = true;
    /// <summary>title, message, isWarning</summary>
    public event Action<string, string, bool>? Toast;

    void Notify(string title, string msg, bool warn)
    {
        Toast?.Invoke(title, msg, warn);
        if (S.Notifications && (!WindowVisible || warn)) NotificationService.Show(title, msg);
    }

    // ---- navigation
    AppPage _page;
    public AppPage CurrentPage
    {
        get => _page;
        set { Set(ref _page, value); Raise(nameof(IsHome), nameof(IsScan), nameof(IsTools), nameof(IsUpdate), nameof(IsSettings)); }
    }
    public bool IsHome => _page == AppPage.Home;
    public bool IsScan => _page == AppPage.Scan;
    public bool IsTools => _page == AppPage.Tools;
    public bool IsUpdate => _page == AppPage.Update;
    public bool IsSettings => _page == AppPage.Settings;

    // Settings tabs: 0 General, 1 Protection, 2 Scanning, 3 Quarantine, 4 History, 5 About
    int _tab;
    public int SettingsTab { get => _tab; set { Set(ref _tab, value); Raise(nameof(IsTabGeneral), nameof(IsTabProtection), nameof(IsTabScanning), nameof(IsTabQuarantine), nameof(IsTabHistory), nameof(IsTabAbout)); } }
    public bool IsTabGeneral => _tab == 0;
    public bool IsTabProtection => _tab == 1;
    public bool IsTabScanning => _tab == 2;
    public bool IsTabQuarantine => _tab == 3;
    public bool IsTabHistory => _tab == 4;
    public bool IsTabAbout => _tab == 5;
    public string AppVersionText => Loc.T("Version") + " " + (typeof(MainViewModel).Assembly.GetName().Version is { } ver ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0");
    public void OpenSettings(int tab) { SettingsTab = tab; CurrentPage = AppPage.Settings; }

    // ---- home / status
    string _engine = "Checking…";
    public string EngineInfo { get => _engine; set { Set(ref _engine, value); Raise(nameof(DbVersionText), nameof(EngineVersionText)); } }
    bool _engineOk;
    string _lastScan = "Never";
    public string LastScan { get => _lastScan; set => Set(ref _lastScan, value); }
    public string HeroTitle { get; private set; } = "";
    public string HeroSubtitle { get; private set; } = "";
    public IBrush HeroBrush { get; private set; } = Green;
    public IBrush HeroGradient { get; private set; } = GGreen;
    public string HeroIcon { get; private set; } = Icons.ShieldCheck;

    // ---- scan
    public ObservableCollection<ThreatItem> Threats { get; } = new();
    ScanState _state = new();
    CancellationTokenSource? _cts;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    DateTime _started;

    bool _scanning, _finished;
    public bool IsScanning { get => _scanning; set { Set(ref _scanning, value); Raise(nameof(ShowResultBox), nameof(IsIdle), nameof(IsActive), nameof(ShowClean), nameof(ShowMoveThreats)); } }
    public bool IsFinished { get => _finished; set { Set(ref _finished, value); Raise(nameof(ShowResultBox), nameof(IsIdle), nameof(IsActive), nameof(ShowMoveThreats)); } }
    public bool IsIdle => !_scanning && !_finished;
    /// <summary>The "move to quarantine" action only makes sense when the finished scan actually found something.</summary>
    public bool ShowMoveThreats => _finished && !_scanning && Threats.Count > 0;
    public bool IsActive => _scanning || _finished;
    public bool ShowClean => _scanning && Threats.Count == 0;
    IBrush _scanGrad = GAmber;
    public IBrush ScanGradient { get => _scanGrad; set => Set(ref _scanGrad, value); }
    public double ProgressAngle => Math.Min(359.9, Math.Max(0.1, Progress * 3.6));
    public void ResetScan() { if (IsScanning) return; IsFinished = false; ScanTitle = Loc.T("Ready to scan"); }
    public bool ShowResultBox => _finished && !_scanning;
    public bool HasThreats => Threats.Count > 0;

    string _scanTitle = "Ready to scan";
    public string ScanTitle { get => _scanTitle; set => Set(ref _scanTitle, value); }
    string _cur = "";
    public string CurrentFile { get => _cur; set => Set(ref _cur, value); }
    double _progress;
    public double Progress { get => _progress; set { Set(ref _progress, value); Raise(nameof(ProgressText), nameof(ProgressAngle)); } }
    public string ProgressText => $"{Progress:0}%";
    bool _indet;
    public bool IsIndeterminate { get => _indet; set => Set(ref _indet, value); }
    string _scanned = "0", _total = "0", _infected = "0", _elapsed = "00:00";
    public string ScannedText { get => _scanned; set => Set(ref _scanned, value); }
    public string TotalText { get => _total; set => Set(ref _total, value); }
    public string InfectedText { get => _infected; set => Set(ref _infected, value); }
    public string ElapsedText { get => _elapsed; set => Set(ref _elapsed, value); }
    string _resultText = "", _resultIcon = Icons.ShieldCheck;
    IBrush _resultBrush = Green;
    public string ResultText { get => _resultText; set => Set(ref _resultText, value); }
    public string ResultIcon { get => _resultIcon; set => Set(ref _resultIcon, value); }
    public IBrush ResultBrush { get => _resultBrush; set => Set(ref _resultBrush, value); }

    // ---- quarantine
    public ObservableCollection<QuarantineItem> Quarantined { get; } = new();
    public bool QuarantineEmpty => Quarantined.Count == 0;
    public string QuarantineCountText => Quarantined.Count.ToString();

    // ---- history
    public ObservableCollection<ScanRecord> History { get; } = new();
    public bool HistoryEmpty => History.Count == 0;

    void AddHistory(ScanRecord r)
    {
        History.Insert(0, r);
        while (History.Count > 200) History.RemoveAt(History.Count - 1);
        HistoryService.Save(History);
        Raise(nameof(HistoryEmpty));
        RebuildStats();
    }

    public string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Clamshield scan report");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Engine: {EngineInfo}");
        sb.AppendLine($"In quarantine: {Quarantined.Count}");
        sb.AppendLine(new string('-', 60));
        foreach (var r in History)
            sb.AppendLine($"{r.Date:yyyy-MM-dd HH:mm}  {r.Type,-18} {r.Status,-14} files={r.Files} threats={r.Threats} time={TimeSpan.FromSeconds(r.Seconds):mm\\:ss}");
        if (Quarantined.Count > 0)
        {
            sb.AppendLine().AppendLine("Quarantined files:");
            foreach (var q in Quarantined) sb.AppendLine($"  {q.Date:yyyy-MM-dd HH:mm}  {q.Virus}  {q.OriginalPath}");
        }
        return sb.ToString();
    }

    // ---- update
    bool _updating;
    public bool IsUpdating { get => _updating; set { Set(ref _updating, value); Raise(nameof(UpdateButtonText), nameof(UpdateSpin), nameof(UpdateArc)); } }
    string _updateStatus = "Check for updates to be sure you have the newest definitions.";
    public string UpdateStatus { get => _updateStatus; set => Set(ref _updateStatus, value); }
    double _upProg; bool _upIndet = true;
    public double UpdateProgress { get => _upProg; set { Set(ref _upProg, value); Raise(nameof(UpdatePercentText), nameof(UpdateAngle)); } }
    public bool UpdateIndeterminate { get => _upIndet; set { Set(ref _upIndet, value); Raise(nameof(UpdatePercentText), nameof(UpdateSpin), nameof(UpdateArc)); } }
    public string UpdatePercentText => _upIndet ? "" : $"{_upProg:0}%";
    // ---- friendly update screen
    string _upRawHead = "", _upRawSub = "";
    int _upState = 1;   // 0 never updated, 1 ok, 2 updating, 3 failed
    IBrush _upGrad = GGreen;
    string _upHead = "Protection is ready";
    public IBrush UpdateGradient { get => _upGrad; set => Set(ref _upGrad, value); }
    public string UpdateHeadline { get => _upHead; set => Set(ref _upHead, value); }
    public string UpdateIcon => _upState == 1 ? Icons.ShieldCheck : Icons.ShieldAlert;
    public string UpdateButtonText => Loc.T(IsUpdating ? "Updating…" : "Check for updates");
    public bool UpdateSpin => IsUpdating && UpdateIndeterminate;
    public bool UpdateArc => IsUpdating && !UpdateIndeterminate;
    public double UpdateAngle => Math.Min(359.9, Math.Max(0.1, UpdateProgress * 3.6));
    public string EngineVersionText => EngineInfo.Contains('/') ? EngineInfo.Split('/')[0] : EngineInfo;
    public string DbVersionText => EngineInfo.Split('/') is { Length: > 1 } p ? Loc.T("Version ") + p[1] : "—";
    public string LastUpdateText
    {
        get
        {
            var t = S.LastUpdate;
            try { var f = Path.Combine(ClamAvLocator.DbDir, "freshclam.dat"); if (t == null && File.Exists(f)) t = File.GetLastWriteTime(f); } catch { }
            if (t == null) return Loc.T("Never");
            return t.Value.Date == DateTime.Today ? Loc.T("Today, ") + t.Value.ToString("HH:mm") : t.Value.ToString("dd MMM, HH:mm");
        }
    }

    void ApplyUpdateState(int state, string headline, string subtitle)
    {
        _upState = state; _upRawHead = headline; _upRawSub = subtitle;
        UpdateGradient = state switch { 1 => GGreen, 2 => GAmber, 3 => GRed, _ => GAmber };
        UpdateHeadline = Loc.T(headline); UpdateStatus = Loc.T(subtitle);
        Raise(nameof(UpdateIcon), nameof(LastUpdateText));
    }

    string _log = "";
    public string UpdateLog { get => _log; set => Set(ref _log, value); }

    // ---- settings (each change is saved immediately)
    void Save() => SettingsService.Save(S);

    public bool RealTimeEnabled
    {
        get => S.RealTime;
        set { if (S.RealTime == value) return; S.RealTime = value; Save(); ApplyRealTime(); Raise(nameof(RealTimeEnabled), nameof(RealTimeStatus), nameof(RtBadgeText), nameof(RtBadgeColor), nameof(RtBadgeBg), nameof(CanPause), nameof(ShowRtDetails)); }
    }
    public bool AutoQuarantine { get => S.AutoQuarantine; set { if (S.AutoQuarantine == value) return; S.AutoQuarantine = value; Save(); Raise(nameof(AutoQuarantine)); } }
    public bool NotificationsEnabled { get => S.Notifications; set { if (S.Notifications == value) return; S.Notifications = value; Save(); Raise(nameof(NotificationsEnabled)); } }
    public bool RunInTray { get => S.RunInTray; set { if (S.RunInTray == value) return; S.RunInTray = value; Save(); Raise(nameof(RunInTray)); } }
    public bool UpdateBeforeScheduled { get => S.UpdateBeforeScheduled; set { if (S.UpdateBeforeScheduled == value) return; S.UpdateBeforeScheduled = value; Save(); Raise(nameof(UpdateBeforeScheduled)); } }
    public bool ScheduleEnabled
    {
        get => S.ScheduleEnabled;
        set
        {
            if (S.ScheduleEnabled == value) return;
            S.ScheduleEnabled = value;
            if (value) S.LastScheduledRun = DateTime.Now;   // don't fire immediately
            Save(); Raise(nameof(ScheduleEnabled), nameof(NextRunText));
        }
    }
    public int FrequencyIndex { get => S.Frequency; set { if (S.Frequency == value || value < 0) return; S.Frequency = value; Save(); Raise(nameof(FrequencyIndex), nameof(IsWeekly), nameof(NextRunText)); } }
    public int DayIndex { get => S.Day; set { if (S.Day == value || value < 0) return; S.Day = value; Save(); Raise(nameof(DayIndex), nameof(NextRunText)); } }
    public bool ScanRemovable { get => S.ScanRemovable; set { if (S.ScanRemovable == value) return; S.ScanRemovable = value; Save(); Raise(nameof(ScanRemovable)); } }

    // ---- exclusions
    public ObservableCollection<string> Exclusions { get; } = new();
    public bool HasExclusions => Exclusions.Count > 0;
    public void AddExclusion(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Exclusions.Contains(path)) return;
        Exclusions.Add(path); SaveExclusions();
    }
    public void RemoveExclusion(string path) { Exclusions.Remove(path); SaveExclusions(); }
    void SaveExclusions()
    {
        S.Exclusions = Exclusions.ToList(); Save();
        Services.Exclusions.Set(S.Exclusions);
        Raise(nameof(HasExclusions));
    }

    // ---- drag & drop
    bool _dragOver;
    public bool IsDragOver { get => _dragOver; set => Set(ref _dragOver, value); }
    public Task ScanPaths(IEnumerable<string> paths)
    {
        var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (list.Length == 0) return Task.CompletedTask;
        return StartScan(list.Length == 1 ? "Scan of " + Path.GetFileName(list[0].TrimEnd('/', '\\')) : $"Scan of {list.Length} items", list, "Drag & Drop");
    }

    // ---- removable drives
    void OnDriveArrived(string root, string name)
    {
        if (!S.ScanRemovable || IsPaused) return;
        _usb.Enqueue((root, name));
        Dispatcher.UIThread.Post(() =>
        {
            Notify(Loc.T("Drive connected"), Loc.F("Scanning \"{0}\" for threats…", name), false);
            _ = DrainUsb();
        });
    }

    async Task DrainUsb()
    {
        while (!IsScanning && _usb.Count > 0)
        {
            var (root, name) = _usb.Dequeue();
            if (Directory.Exists(root)) await StartScan("USB Scan · " + name, new[] { root }, "USB Scan", showPage: false);
        }
    }

    public bool IsWeekly => S.Frequency == 1;
    public TimeSpan? ScheduleTime
    {
        get => TimeSpan.TryParse(S.Time, out var t) ? t : new TimeSpan(12, 0, 0);
        set { if (value == null) return; S.Time = value.Value.ToString(@"hh\:mm"); Save(); Raise(nameof(ScheduleTime), nameof(NextRunText)); }
    }
    public string NextRunText => S.ScheduleEnabled ? Loc.T("Next scan: ") + NextRun().ToString("ddd, dd MMM yyyy  HH:mm", new System.Globalization.CultureInfo(Loc.I.IsId ? "id-ID" : "en-US")) : Loc.T("Scheduled scans are turned off.");

    DateTime NextRun()
    {
        var last = S.LastScheduledRun ?? DateTime.Now;
        var t = ScheduleTime ?? TimeSpan.Zero;
        var day = (DayOfWeek)((S.Day + 1) % 7);   // 0=Mon..6=Sun -> DayOfWeek
        for (int i = 0; i < 9; i++)
        {
            var c = last.Date.AddDays(i) + t;
            if (c <= last) continue;
            if (S.Frequency == 1 && c.DayOfWeek != day) continue;
            return c;
        }
        return last.AddDays(1);
    }

    async void CheckSchedule()
    {
        try
        {
            if (!S.ScheduleEnabled || IsScanning || IsUpdating || IsPaused || NextRun() > DateTime.Now) return;
            if (PowerState.ShouldDeferHeavyWork(S.PauseOnBattery, S.PauseFullscreen)) return;   // try again at the next tick
            S.LastScheduledRun = DateTime.Now; Save(); Raise(nameof(NextRunText));
            if (S.UpdateBeforeScheduled) await UpdateDefinitions();
            await StartScan("Scheduled Scan", ScanService.QuickRoots(), "Scheduled", showPage: false, lowPriority: true);
        }
        catch { }
    }

    public bool FastEngine
    {
        get => S.FastEngine;
        set { if (S.FastEngine == value) return; S.FastEngine = value; Save(); if (value) ClamdService.Start(); else ClamdService.Stop(); Raise(nameof(FastEngine)); }
    }

    // ---- first-run wizard
    bool _wizard;
    public bool IsWizard { get => _wizard; set => Set(ref _wizard, value); }
    int _wstep;
    public int WizardStep
    {
        get => _wstep;
        set { Set(ref _wstep, value); Raise(nameof(IsStep0), nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4)); }
    }
    public bool IsStep0 => _wstep == 0;
    public bool IsStep1 => _wstep == 1;
    public bool IsStep2 => _wstep == 2;
    public bool IsStep3 => _wstep == 3;
    public bool IsStep4 => _wstep == 4;
    bool _wBusy, _wOk, _wSkip, _wIndet = true;
    string _wStatus = "", _wDetail = "", _wError = "";
    double _wProg;
    public bool WizardBusy { get => _wBusy; set => Set(ref _wBusy, value); }
    public bool WizardOk { get => _wOk; set => Set(ref _wOk, value); }
    public bool WizardCanSkip { get => _wSkip; set => Set(ref _wSkip, value); }
    public bool WizardIndeterminate { get => _wIndet; set { Set(ref _wIndet, value); Raise(nameof(WizardPercentText)); } }
    public double WizardProgress { get => _wProg; set { Set(ref _wProg, value); Raise(nameof(WizardPercentText)); } }
    public string WizardPercentText => _wIndet ? "" : $"{_wProg:0}%";
    public string WizardStatus { get => _wStatus; set => Set(ref _wStatus, value); }
    public string WizardDetail { get => _wDetail; set => Set(ref _wDetail, value); }
    public string WizardError { get => _wError; set { Set(ref _wError, value); Raise(nameof(HasWizardError)); } }
    public bool HasWizardError => !string.IsNullOrEmpty(_wError);

    // freshclam prints "Time: 3s, ETA: 10s [=====   ]  2.67MiB/22.36MiB" (no percent sign), so compute it.
    static readonly System.Text.RegularExpressions.Regex SizeProgress =
        new(@"(?<a>\d+(?:\.\d+)?)\s*(?<au>[KMG]?i?B)\s*/\s*(?<b>\d+(?:\.\d+)?)\s*(?<bu>[KMG]?i?B)", System.Text.RegularExpressions.RegexOptions.Compiled);
    static double ToBytes(string n, string unit) =>
        double.Parse(n, System.Globalization.CultureInfo.InvariantCulture) * (unit[0] switch { 'K' => 1024.0, 'M' => 1048576.0, 'G' => 1073741824.0, _ => 1.0 });

    /// <summary>Returns true and a percent + readable text when the line is a download-progress line.</summary>
    static bool TryProgress(string line, out double pct, out string text)
    {
        pct = 0; text = "";
        var m = SizeProgress.Match(line);
        if (m.Success)
        {
            double a = ToBytes(m.Groups["a"].Value, m.Groups["au"].Value), b = ToBytes(m.Groups["b"].Value, m.Groups["bu"].Value);
            if (b > 0) { pct = Math.Clamp(a * 100 / b, 0, 100); text = $"Downloaded {a / 1048576:0.0} of {b / 1048576:0.0} MB"; return true; }
        }
        var p = System.Text.RegularExpressions.Regex.Match(line, @"(\d{1,3})%");
        if (p.Success) { pct = Math.Min(100, double.Parse(p.Groups[1].Value)); text = "Downloading…"; return true; }
        return false;
    }

    public void WizardNext()
    {
        if (_wstep >= 4) return;
        WizardStep = _wstep + 1;
        if (_wstep == 1) _ = RunEngineStep();
        else if (_wstep == 2) _ = RunDefinitionsStep();
    }

    public void WizardRetry()
    {
        if (_wstep == 1) _ = RunEngineStep();
        else if (_wstep == 2) _ = RunDefinitionsStep();
    }

    void WizardReset()
    {
        WizardBusy = true; WizardOk = false; WizardCanSkip = false; WizardError = Loc.T("");
        WizardIndeterminate = true; WizardProgress = 0; WizardDetail = Loc.T("");
    }

    async Task RunEngineStep()
    {
        WizardReset();
        WizardStatus = Loc.T("Looking for the ClamAV engine…");
        await Task.Delay(700);
        if (ClamAvLocator.Find("clamscan") == null)
        {
            WizardStatus = Loc.T("Preparing to download the ClamAV engine…");
            var prog = new Progress<(double pct, string msg)>(t =>
            {
                WizardIndeterminate = t.pct < 0;
                if (t.pct >= 0) WizardProgress = t.pct;
                WizardStatus = t.msg;
            });
            var err = await EngineInstaller.InstallAsync(prog, CancellationToken.None);
            if (err != null) { WizardError = err; WizardBusy = false; return; }
        }
        var v = await Task.Run(ClamAvLocator.Version);
        if (v == null) { WizardError = Loc.T("ClamAV is installed but could not be started."); WizardBusy = false; return; }
        _engineOk = true; EngineInfo = v; Refresh();
        WizardStatus = Loc.T("The scan engine is ready"); WizardDetail = v;
        WizardIndeterminate = false; WizardProgress = 100; WizardOk = true; WizardBusy = false;
        await Task.Delay(1100);
        if (_wstep == 1 && IsWizard) WizardNext();
    }

    async Task RunDefinitionsStep()
    {
        WizardReset();
        WizardStatus = Loc.T(ClamAvLocator.HasUserDb ? "Checking for new virus definitions…" : "Downloading the latest virus definitions…");
        WizardDetail = ClamAvLocator.HasUserDb ? "" : Loc.T("First time only, about 120 MB.");
        var res = await Task.Run(() => ScanService.UpdateAsync(l => Dispatcher.UIThread.Post(() =>
        {
            var t = l.Trim();
            if (t.Length == 0) return;
            if (TryProgress(t, out var pct, out var text)) { WizardIndeterminate = false; WizardProgress = pct; WizardDetail = text; }
            else WizardDetail = t.Length > 80 ? t[..80] : t;
        }), CancellationToken.None));
        if (res != "OK")
        {
            WizardError = Loc.T("Couldn't download the virus definitions. Check your internet connection and try again.");
            WizardCanSkip = true; WizardBusy = false; return;
        }
        WizardStatus = Loc.T("Virus definitions are up to date"); WizardDetail = Loc.T("");
        WizardIndeterminate = false; WizardProgress = 100; WizardOk = true; WizardBusy = false;
        EngineInfo = await Task.Run(() => ClamAvLocator.Version()) ?? EngineInfo;
        await Task.Delay(1100);
        if (_wstep == 2 && IsWizard) WizardNext();
    }

    public void WizardFinish()
    {
        S.SetupDone = true; Save();
        IsWizard = false;
        ApplyRealTime();
        if (S.FastEngine) Task.Run(ClamdService.Start);
        Navigate(AppPage.Home);
        Refresh();
    }

    // ---- real-time
    int _rtCount;
    public string RealTimeStatus => IsPaused ? PauseText : S.RealTime
        ? Loc.T("Active · watching ") + string.Join(", ", RealTimeFolders().Select(Path.GetFileName).Distinct())
        : Loc.T("Turned off. New files are not checked automatically.");
    public string RealTimeCount => Loc.F("{0:N0} files checked", _rtCount);

    void ApplyRealTime()
    {
        if (IsPaused) { _rt.Stop(); _shield.Stop(); return; }
        if (S.RealTime) _rt.Start(RealTimeFolders()); else _rt.Stop();
        ApplyShield();
    }

    void OnRealTimeThreat(ThreatItem t)
    {
        AddRtLog("Threat found", t.Path, t.Virus); _rtBlocked++; Raise(nameof(RtBlockedText));
        bool isolated = false;
        if (S.AutoQuarantine && QuarantineService.Add(t) == null) { isolated = true; ReloadQuarantine(); }
        else Threats.Add(t);
        AddHistory(new ScanRecord { Date = DateTime.Now, Type = "Real-time", Files = 1, Threats = 1, Status = isolated ? "Blocked" : "Threat found" });
        Notify(Loc.T(isolated ? "Threat blocked" : "Threat detected"), Loc.F(isolated ? "{0} in {1} was moved to quarantine." : "{0} in {1}.", t.Virus, t.FileName), true);
        Refresh();
    }

    void ReloadQuarantine()
    {
        Quarantined.Clear();
        foreach (var q in QuarantineService.Load()) Quarantined.Add(q);
        Raise(nameof(QuarantineEmpty), nameof(QuarantineCountText));
    }

    public MainViewModel()
    {
        _timer.Tick += (_, _) => Tick();
        Threats.CollectionChanged += (_, _) => Raise(nameof(HasThreats), nameof(ShowClean), nameof(ShowMoveThreats));
        foreach (var q in QuarantineService.Load()) Quarantined.Add(q);
        foreach (var h in HistoryService.Load().OrderByDescending(x => x.Date)) History.Add(h);
        if (History.FirstOrDefault(h => h.Type != "Real-time" && h.Status != "Cancelled" && h.Status != "Failed") is { } lastScan)
            _lastScan = lastScan.Date.ToString("dd MMM, HH:mm");
        foreach (var x in S.Exclusions) Exclusions.Add(x);
        Services.Exclusions.Set(S.Exclusions);
        _drives.DriveArrived += OnDriveArrived;
        _rt.ThreatFound += t => Dispatcher.UIThread.Post(() => OnRealTimeThreat(t));
        _rt.FileChecked += _ => Dispatcher.UIThread.Post(() => { _rtCount++; Raise(nameof(RealTimeCount)); });
        _sched.Tick += (_, _) => CheckSchedule();
        _sched.Start();
        bool engineThere = ClamAvLocator.Find("clamscan") != null;
        if (!S.SetupDone || !engineThere)
        {
            IsWizard = true;
            if (S.SetupDone) { WizardStep = 1; _ = RunEngineStep(); }   // engine vanished: repair it
        }
        else if (S.FastEngine) Task.Run(ClamdService.Start);
        InitFeatures();
        if (!ClamAvLocator.HasUserDb) ApplyUpdateState(0, "Not updated yet", "Update now to download the virus definitions.");
        else ApplyUpdateState(1, "Protection is ready", "Check for updates to be sure you have the newest definitions.");
        ApplyRealTime();
        Refresh();
        Task.Run(() =>
        {
            var v = ClamAvLocator.Version();
            Dispatcher.UIThread.Post(() => { _engineOk = v != null; EngineInfo = v ?? "Not installed"; Refresh(); });
        });
    }

    public void Navigate(AppPage p) => CurrentPage = p;

    void Refresh()
    {
        if (IsPaused)
        { HeroTitle = Loc.T("Protection is paused"); HeroSubtitle = PauseText; HeroBrush = Amber; HeroGradient = GAmber; HeroIcon = Icons.ShieldAlert; }
        else if (!_engineOk && EngineInfo != "Checking…")
        { HeroTitle = Loc.T("Protection engine missing"); HeroSubtitle = ClamAvLocator.InstallHint; HeroBrush = Red; HeroGradient = GRed; HeroIcon = Icons.ShieldAlert; }
        else if (Threats.Count > 0)
        { HeroTitle = Loc.T("Threats need your attention"); HeroSubtitle = Plural(Threats.Count, "1 infected file was found. Open Scan to move it to quarantine.", "{0} infected files were found. Open Scan to move them to quarantine."); HeroBrush = Red; HeroGradient = GRed; HeroIcon = Icons.ShieldAlert; }
        else if (DefinitionsOutdated)
        { HeroTitle = Loc.T("Your protection needs an update"); HeroSubtitle = DefsBannerTitle + "."; HeroBrush = Amber; HeroGradient = GAmber; HeroIcon = Icons.ShieldAlert; }
        else if (LastScan == "Never")
        { HeroTitle = Loc.T("You haven't scanned yet"); HeroSubtitle = Loc.T("Run a Smart Scan to check that your device is clean."); HeroBrush = Amber; HeroGradient = GAmber; HeroIcon = Icons.ShieldAlert; }
        else
        { HeroTitle = Loc.T("You're protected"); HeroSubtitle = Loc.T(S.RealTime ? "Real-time protection is on and your last scan found no threats." : "No threats found. Turn on real-time protection for continuous checks."); HeroBrush = Green; HeroGradient = GGreen; HeroIcon = Icons.ShieldCheck; }
        Raise(nameof(HeroTitle), nameof(HeroSubtitle), nameof(HeroBrush), nameof(HeroGradient), nameof(HeroIcon), nameof(QuarantineEmpty), nameof(QuarantineCountText), nameof(HasThreats));
        ComputeHealth();
    }

    // ---- scanning
    public Task SmartScan() => StartScan("Smart Scan", ScanService.QuickRoots(), "Smart Scan");
    public Task QuickScan() => StartScan("Quick Scan", ScanService.QuickRoots(), "Quick Scan");
    public Task FullScan() => StartScan("Full Scan", ScanService.FullRoots(), "Full Scan");
    public Task CustomScan(string path) => StartScan("Custom Scan", new[] { path }, "Custom Scan");

    async Task StartScan(string title, string[] roots, string kind, bool showPage = true, bool lowPriority = false)
    {
        if (IsScanning) { if (showPage) CurrentPage = AppPage.Scan; return; }
        if (showPage) CurrentPage = AppPage.Scan;
        Threats.Clear();
        _state = new ScanState();
        _cts = new CancellationTokenSource();
        _started = DateTime.Now;
        ScanTitle = Loc.T(title); ScanGradient = GAmber;
        IsFinished = false; IsScanning = true; IsIndeterminate = true; Progress = 0;
        CurrentFile = Loc.T("Preparing…");
        ScannedText = "0"; TotalText = "0"; InfectedText = "0"; ElapsedText = "00:00";
        _timer.Start();
        var ct = _cts.Token;
        try { await Task.Run(() => ScanService.RunAsync(roots, _state, t => Dispatcher.UIThread.Post(() => { Threats.Add(t); Raise(nameof(HasThreats)); }), ct)); }
        catch (OperationCanceledException) { }
        catch (Exception e) { _state.Error = e.Message; }
        _timer.Stop(); Tick();
        IsScanning = false; IsFinished = true; IsIndeterminate = false;
        string status;
        if (_state.Error != null)
        { status = "Failed"; ScanGradient = GRed; ScanTitle = Loc.T("Scan failed"); ResultText = _state.Error; ResultIcon = Icons.ShieldAlert; ResultBrush = Red; }
        else if (ct.IsCancellationRequested)
        { status = "Cancelled"; ScanGradient = GGray; ScanTitle = Loc.T("Scan cancelled"); ResultText = Loc.T("The scan was stopped before it finished."); ResultIcon = Icons.ShieldAlert; ResultBrush = Amber; }
        else
        {
            LastScan = DateTime.Now.ToString("dd MMM, HH:mm"); Progress = 100;
            ScanTitle = Loc.T("Scan complete");
            if (Threats.Count > 0) { status = "Threats found"; ScanGradient = GRed; ResultText = Plural(Threats.Count, "1 threat found.", "{0} threats found."); ResultIcon = Icons.ShieldAlert; ResultBrush = Red; }
            else { status = "Clean"; ScanGradient = GGreen; ResultText = Loc.T("No threats found. Your files are clean."); ResultIcon = Icons.ShieldCheck; ResultBrush = Green; }
        }
        if (_scanNote != null && status is "Clean" or "Threats found") ResultText += "  " + _scanNote;
        _scanNote = null;
        AddHistory(new ScanRecord { Date = _started, Type = kind, Files = _state.Scanned, Threats = _state.Infected, Seconds = (DateTime.Now - _started).TotalSeconds, Status = status });
        if (status is "Clean" or "Threats found" && (kind is "Scheduled" or "USB Scan" || !WindowVisible))
            Notify(status == "Clean" ? Loc.F("{0} finished", Loc.T(kind)) : Loc.T("Threats found"),
                   status == "Clean" ? Loc.T("No threats were found.") : Plural(Threats.Count, "1 infected file needs your attention.", "{0} infected files need your attention."), status != "Clean");
        CurrentFile = Loc.T("");
        Refresh();
        _ = DrainUsb();
    }

    void Tick()
    {
        var s = _state;
        IsIndeterminate = s.Preparing && IsScanning;
        if (!s.Preparing && s.Total > 0) Progress = Math.Min(100, s.Scanned * 100.0 / s.Total);
        if (!string.IsNullOrEmpty(s.Current)) CurrentFile = s.Current;
        ScannedText = s.Scanned.ToString("N0"); TotalText = s.Total.ToString("N0"); InfectedText = s.Infected.ToString();
        var e = DateTime.Now - _started;
        ElapsedText = e.TotalHours >= 1 ? e.ToString(@"h\:mm\:ss") : e.ToString(@"mm\:ss");
    }

    public void CancelScan() => _cts?.Cancel();

    public string? QuarantineAll()
    {
        string? err = null;
        foreach (var t in Threats.ToList())
        {
            var e = QuarantineService.Add(t);
            if (e == null) Threats.Remove(t); else err = e;
        }
        ReloadQuarantine();
        if (Threats.Count == 0) { ResultText = Loc.T("All threats were moved to quarantine."); ResultIcon = Icons.ShieldCheck; ResultBrush = Green; }
        Refresh();
        return err;
    }

    public string? Restore(QuarantineItem q) { var e = QuarantineService.Restore(q); if (e == null) Quarantined.Remove(q); Refresh(); return e; }
    public void DeleteQuarantined(QuarantineItem q) { QuarantineService.Delete(q); Quarantined.Remove(q); Refresh(); }

    public async Task UpdateDefinitions()
    {
        if (IsUpdating) return;
        bool first = !ClamAvLocator.HasUserDb;
        IsUpdating = true; UpdateLog = ""; UpdateIndeterminate = true; UpdateProgress = 0;
        ApplyUpdateState(2, "Updating virus definitions…", first
            ? "Downloading the virus database for the first time. This can take a few minutes."
            : "Checking for new definitions. This only takes a moment.");
        bool downloaded = false;
        var result = await Task.Run(() => ScanService.UpdateAsync(l => Dispatcher.UIThread.Post(() =>
        {
            if (TryProgress(l, out var pct, out _)) { UpdateIndeterminate = false; UpdateProgress = pct; }
            else if (l.Trim().Length > 0) { UpdateLog += l + "\n"; if (l.Contains(" updated (")) downloaded = true; }
        }), CancellationToken.None));
        UpdateProgress = 100; UpdateIndeterminate = false;
        IsUpdating = false;
        if (result == "OK")
        {
            S.LastUpdate = DateTime.Now; Save();
            ApplyUpdateState(1, "You're up to date",
                downloaded ? "The newest definitions are installed." : "You already have the newest definitions.");
        }
        else
        {
            ApplyUpdateState(3, "Couldn't update",
                result.StartsWith("exit:") ? "We couldn't reach the update server. Check your internet connection and try again." : result);
        }
        EngineInfo = await Task.Run(() => ClamAvLocator.Version()) ?? EngineInfo;
        if (result == "OK" && S.FastEngine) _ = Task.Run(ClamdService.Restart);
        Raise(nameof(LastUpdateText));
        RefreshDefsBanner(); Refresh();
    }

    public void Shutdown() { _shield.Dispose(); _rt.Dispose(); _drives.Dispose(); _sched.Stop(); ClamdService.Stop(); }
}

public static class Icons
{
    public const string ShieldCheck = "F0 M12 2 L4 5 V11 C4 16 7.5 20.5 12 22 C16.5 20.5 20 16 20 11 V5 Z M8.2 12.2 L10.8 14.8 L15.8 9.4 L14.6 8.3 L10.8 12.4 L9.4 11 Z";
    public const string ShieldAlert = "F0 M12 2 L4 5 V11 C4 16 7.5 20.5 12 22 C16.5 20.5 20 16 20 11 V5 Z M11 7 H13 V13 H11 Z M11 15 H13 V17 H11 Z";
}
