using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Clamshield.Models;
using Clamshield.Services;

namespace Clamshield.ViewModels;

/// <summary>Appearance, language, system integration, extra protection, tools, health score and statistics.</summary>
public partial class MainViewModel
{
    readonly RansomwareShield _shield = new();
    DateTime _lastCleanNote = DateTime.MinValue, _lastPurge = DateTime.MinValue;

    static string Plural(int n, string one, string many) => n == 1 ? Loc.T(one) : Loc.F(many, n);

    void InitFeatures()
    {
        // keep our records in sync with what is actually registered in the OS
        S.AutoStart = AutoStart.IsEnabled();
        S.ContextMenu = ShellIntegration.IsSupported && ShellIntegration.IsEnabled();
        PushScanOptions();
        foreach (var f in S.WatchFolders) WatchFolders.Add(f);
        foreach (var p in S.Profiles) Profiles.Add(p);
        foreach (var t in TrustedFiles.All()) Trusted.Add(t);
        foreach (var b in Blocklist.All()) Blocked.Add(b);
        foreach (var y in YaraRules.List()) Yara.Add(y);
        Blocklist.Sync();

        _shield.Alert += (summary, samples) => Dispatcher.UIThread.Post(() => OnRansomware(summary, samples));
        _rt.FileClean += path => Dispatcher.UIThread.Post(() => OnFileClean(path));
        WireRealTimeActivity();
        _sched.Tick += (_, _) => PeriodicChores();
        PeriodicChores();
        RebuildStats();
        ComputeHealth();
    }

    // ---------------- appearance & language
    public static void ApplyTheme(int t) =>
        Application.Current!.RequestedThemeVariant = t switch { 1 => ThemeVariant.Light, 2 => ThemeVariant.Dark, _ => ThemeVariant.Default };

    public int ThemeIndex
    {
        get => S.Theme;
        set { if (S.Theme == value || value < 0) return; S.Theme = value; Save(); ApplyTheme(value); Raise(nameof(ThemeIndex)); }
    }

    public int LanguageIndex
    {
        get => S.Language == "id" ? 1 : 0;
        set
        {
            var l = value == 1 ? "id" : "en";
            if (S.Language == l) return;
            S.Language = l; Save(); Loc.I.Language = l;
            RefreshLanguage();
        }
    }

    void RefreshLanguage()
    {
        var h = History.ToList(); History.Clear(); foreach (var x in h) History.Add(x);
        var q = Quarantined.ToList(); Quarantined.Clear(); foreach (var x in q) Quarantined.Add(x);
        if (IsIdle) ScanTitle = Loc.T("Ready to scan");
        ApplyUpdateState(_upState, _upRawHead, _upRawSub);
        Refresh(); RebuildStats(); ComputeHealth();
        Raise(string.Empty);   // re-read every binding
    }

    // ---------------- system integration
    public bool AutoStartEnabled
    {
        get => S.AutoStart;
        set
        {
            if (S.AutoStart == value) return;
            var err = value ? AutoStart.Enable() : AutoStart.Disable();
            if (err != null) { Toast?.Invoke(Loc.T("Couldn't change the startup setting"), err, true); Raise(nameof(AutoStartEnabled)); return; }
            S.AutoStart = value; Save(); Raise(nameof(AutoStartEnabled));
        }
    }

    public bool ContextMenuSupported => ShellIntegration.IsSupported;
    public bool ContextMenuEnabled
    {
        get => S.ContextMenu;
        set
        {
            if (S.ContextMenu == value) return;
            var err = value ? ShellIntegration.Enable() : ShellIntegration.Disable();
            if (err != null) { Toast?.Invoke(Loc.T("Couldn't change the right-click menu"), err, true); Raise(nameof(ContextMenuEnabled)); return; }
            S.ContextMenu = value; Save(); Raise(nameof(ContextMenuEnabled));
        }
    }

    // ---------------- extra protection
    public ObservableCollection<string> WatchFolders { get; } = new();
    public bool HasWatchFolders => WatchFolders.Count > 0;

    IEnumerable<string> RealTimeFolders() =>
        RealTimeService.DefaultFolders()
            .Concat(S.WatchFolders.Where(Directory.Exists))
            .Concat(S.WatchEmail ? EmailFolders.Detect() : Array.Empty<string>())
            .Distinct();

    public void AddWatchFolder(string p)
    {
        if (string.IsNullOrWhiteSpace(p) || WatchFolders.Contains(p)) return;
        WatchFolders.Add(p); S.WatchFolders = WatchFolders.ToList(); Save();
        Raise(nameof(HasWatchFolders), nameof(RealTimeStatus)); ApplyRealTime();
    }

    public void RemoveWatchFolder(string p)
    {
        WatchFolders.Remove(p); S.WatchFolders = WatchFolders.ToList(); Save();
        Raise(nameof(HasWatchFolders), nameof(RealTimeStatus)); ApplyRealTime();
    }

    public bool WatchEmail { get => S.WatchEmail; set { if (S.WatchEmail == value) return; S.WatchEmail = value; Save(); Raise(nameof(WatchEmail), nameof(RealTimeStatus)); ApplyRealTime(); } }
    public bool NotifyClean { get => S.NotifyClean; set { if (S.NotifyClean == value) return; S.NotifyClean = value; Save(); Raise(nameof(NotifyClean)); } }
    public bool PauseOnBattery { get => S.PauseOnBattery; set { if (S.PauseOnBattery == value) return; S.PauseOnBattery = value; Save(); Raise(nameof(PauseOnBattery)); } }
    public bool PauseFullscreen { get => S.PauseFullscreen; set { if (S.PauseFullscreen == value) return; S.PauseFullscreen = value; Save(); Raise(nameof(PauseFullscreen)); } }
    public bool WeeklyReport { get => S.WeeklyReport; set { if (S.WeeklyReport == value) return; S.WeeklyReport = value; Save(); Raise(nameof(WeeklyReport)); } }

    public bool RansomwareEnabled
    {
        get => S.Ransomware;
        set { if (S.Ransomware == value) return; S.Ransomware = value; Save(); ApplyShield(); Raise(nameof(RansomwareEnabled)); ComputeHealth(); }
    }

    void ApplyShield()
    {
        _shield.Stop();
        if (!S.Ransomware) return;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Path.Combine(home, "Pictures") }
            .Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f)).Distinct();
        _shield.Start(folders, threshold: 120);
    }

    void OnRansomware(string summary, string[] samples)
    {
        AddHistory(new ScanRecord { Date = DateTime.Now, Type = "Ransomware shield", Files = samples.Length, Threats = 1, Status = "Alert" });
        Notify(Loc.T("Possible ransomware activity"), summary, true);
    }

    void OnFileClean(string path)
    {
        if (!S.NotifyClean || (DateTime.Now - _lastCleanNote).TotalSeconds < 4) return;
        _lastCleanNote = DateTime.Now;
        Notify(Loc.T("File is safe"), Path.GetFileName(path), false);
    }

    // ---------------- scanning options
    void PushScanOptions()
    {
        ScanOptions.ScanArchives = S.ScanArchives; ScanOptions.MaxFileSizeMb = S.MaxFileSizeMb; ScanOptions.SkipMedia = S.SkipMedia;
        ScanOptions.Strict = S.Strict; ScanOptions.ExtraSignatures = S.ExtraSignatures;
    }
    void ScanOptionsChanged() { Save(); PushScanOptions(); if (S.FastEngine && ClamdService.Ready) Task.Run(ClamdService.Restart); }

    public bool ScanArchives { get => S.ScanArchives; set { if (S.ScanArchives == value) return; S.ScanArchives = value; ScanOptionsChanged(); Raise(nameof(ScanArchives)); } }
    public bool SkipMedia { get => S.SkipMedia; set { if (S.SkipMedia == value) return; S.SkipMedia = value; ScanOptionsChanged(); Raise(nameof(SkipMedia)); } }

    public bool StrictDetection { get => S.Strict; set { if (S.Strict == value) return; S.Strict = value; ScanOptionsChanged(); Raise(nameof(StrictDetection)); } }

    public bool ExtraSignatures
    {
        get => S.ExtraSignatures;
        set
        {
            if (S.ExtraSignatures == value) return;
            S.ExtraSignatures = value; Save(); PushScanOptions(); Raise(nameof(ExtraSignatures));
            if (value)
            {
                Toast?.Invoke(Loc.T("Extra virus signatures"), Loc.T("Updating now. This only takes a moment."), false);
                _ = UpdateDefinitions();
            }
            else
            {
                ClamAvLocator.RemoveExtraSignatures();
                if (S.FastEngine) Task.Run(ClamdService.Restart);
            }
        }
    }

    static readonly int[] SizeChoices = { 25, 50, 100, 250, 500 };
    public int MaxSizeIndex
    {
        get { var i = Array.IndexOf(SizeChoices, S.MaxFileSizeMb); return i < 0 ? 2 : i; }
        set { if (value < 0 || value >= SizeChoices.Length || S.MaxFileSizeMb == SizeChoices[value]) return; S.MaxFileSizeMb = SizeChoices[value]; ScanOptionsChanged(); Raise(nameof(MaxSizeIndex)); }
    }

    static readonly int[] RetentionChoices = { 0, 7, 30, 90 };
    public int RetentionIndex
    {
        get { var i = Array.IndexOf(RetentionChoices, S.RetentionDays); return i < 0 ? 2 : i; }
        set { if (value < 0 || value >= RetentionChoices.Length || S.RetentionDays == RetentionChoices[value]) return; S.RetentionDays = RetentionChoices[value]; Save(); _lastPurge = DateTime.MinValue; PeriodicChores(); Raise(nameof(RetentionIndex)); }
    }

    // ---------------- scan profiles
    public ObservableCollection<ScanProfile> Profiles { get; } = new();
    public bool HasProfiles => Profiles.Count > 0;
    string _profileName = "";
    public string ProfileName { get => _profileName; set => Set(ref _profileName, value); }

    public void AddProfile(IEnumerable<string> paths)
    {
        var list = paths.Where(Directory.Exists).ToList();
        if (list.Count == 0) return;
        var name = string.IsNullOrWhiteSpace(_profileName) ? Path.GetFileName(list[0].TrimEnd('/', '\\')) : _profileName.Trim();
        var p = new ScanProfile { Name = name, Paths = list };
        Profiles.Add(p); S.Profiles = Profiles.ToList(); Save();
        ProfileName = ""; Raise(nameof(HasProfiles));
    }

    public void RemoveProfile(ScanProfile p) { Profiles.Remove(p); S.Profiles = Profiles.ToList(); Save(); Raise(nameof(HasProfiles)); }
    public Task RunProfile(ScanProfile p) => StartScan(p.Name, p.Paths.ToArray(), "Profile");

    // ---------------- trusted files
    public ObservableCollection<TrustedEntry> Trusted { get; } = new();
    public bool HasTrusted => Trusted.Count > 0;

    public void RemoveTrusted(TrustedEntry t)
    {
        TrustedFiles.Remove(t.Sha256); Trusted.Remove(t); Raise(nameof(HasTrusted));
    }

    public string? RestoreAndTrust(QuarantineItem q)
    {
        try { TrustedFiles.Add(TrustedFiles.HashFile(QuarantineService.StoredPath(q)), q.OriginalPath); }
        catch (Exception e) { return e.Message; }
        Trusted.Clear(); foreach (var t in TrustedFiles.All()) Trusted.Add(t);
        Raise(nameof(HasTrusted));
        return Restore(q);
    }

    // ---------------- VirusTotal
    public string VtKey
    {
        get => S.VtKey;
        set { if (S.VtKey == value) return; S.VtKey = value.Trim(); Save(); Raise(nameof(VtKey), nameof(HasVtKey)); }
    }

    public async Task CheckOnline(string path, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(S.VtKey))
        { Toast?.Invoke(Loc.T("VirusTotal key needed"), Loc.T("Add your free API key in Settings, under Scanning."), true); return; }
        Toast?.Invoke(Loc.T("Checking VirusTotal…"), Loc.T("Only the file's fingerprint is sent, not the file."), false);
        VtBusy = true;
        var r = await VirusTotalService.LookupAsync(path, S.VtKey);
        VtBusy = false;
        string msg = r.Status switch
        {
            VtStatus.Detected => Loc.F("{0} of {1} security engines flag this file.", r.Malicious + r.Suspicious, r.Total),
            VtStatus.Clean => Loc.F("None of the {0} security engines flag this file.", r.Total),
            VtStatus.NotFound => Loc.T("VirusTotal has never seen this file."),
            VtStatus.RateLimited => Loc.T("Too many lookups. Free keys allow 4 per minute, try again shortly."),
            VtStatus.NoKey => Loc.T("Add your free API key in Settings, under Scanning."),
            _ => r.Message,
        };
        VtFileName = displayName ?? Path.GetFileName(path); VtResultText = msg;
        VtResultBrush = r.Status == VtStatus.Detected ? Red : r.Status == VtStatus.Clean ? Green : Amber;
        _vtLink = r.Permalink ?? ""; HasVtLink = r.Status is VtStatus.Detected or VtStatus.Clean && _vtLink.Length > 0; HasVtResult = true;
        Toast?.Invoke("VirusTotal", msg, r.Status == VtStatus.Detected);
    }

    // ---------------- tools page (picker -> single tool)
    int _toolView;   // 0 picker, 1 online check, 2 shredder, 3 link, 4 password, 5 startup, 6 clean-up
    public int ToolView
    {
        get => _toolView;
        set
        {
            Set(ref _toolView, value);
            Raise(nameof(IsToolsHome), nameof(IsToolVt), nameof(IsToolShred), nameof(IsToolLink), nameof(IsToolPassword), nameof(IsToolStartup), nameof(IsToolClean), nameof(IsToolBlock), nameof(IsToolEngine));
            if (value == 5) _ = LoadStartup();
            if (value == 8) _ = LoadEngineDetails();
            if (value == 6 && !CleanAnalyzed) _ = AnalyzeCleanup();
        }
    }
    public bool IsToolLink => _toolView == 3;
    public bool IsToolPassword => _toolView == 4;
    public bool IsToolStartup => _toolView == 5;
    public bool IsToolClean => _toolView == 6;
    public bool IsToolBlock => _toolView == 7;
    public bool IsToolEngine => _toolView == 8;
    public bool IsToolsHome => _toolView == 0;
    public bool IsToolVt => _toolView == 1;
    public bool IsToolShred => _toolView == 2;

    public bool HasVtKey => !string.IsNullOrWhiteSpace(S.VtKey);
    bool _vtBusy, _hasVtResult, _hasVtLink;
    string _vtText = "", _vtFile = "", _vtLink = "";
    IBrush _vtBrush = Green;
    public bool VtBusy { get => _vtBusy; set => Set(ref _vtBusy, value); }
    public bool HasVtResult { get => _hasVtResult; set => Set(ref _hasVtResult, value); }
    public bool HasVtLink { get => _hasVtLink; set => Set(ref _hasVtLink, value); }
    public string VtResultText { get => _vtText; set => Set(ref _vtText, value); }
    public string VtFileName { get => _vtFile; set => Set(ref _vtFile, value); }
    public IBrush VtResultBrush { get => _vtBrush; set => Set(ref _vtBrush, value); }

    public void OpenVtLink()
    {
        if (string.IsNullOrEmpty(_vtLink)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_vtLink) { UseShellExecute = true }); } catch { }
    }

    // ---------------- real-time activity (live status + log)
    public ObservableCollection<RtLogEntry> RtLog { get; } = new();
    public bool HasRtLog => RtLog.Count > 0;
    bool _rtBusy; string _rtNow = "", _rtFolder = ""; int _rtBlocked;
    readonly object _logLock = new();
    static string RtLogPath => Path.Combine(AppData.Dir, "logs", "realtime.log");

    public bool RtBusy { get => _rtBusy; set => Set(ref _rtBusy, value); }
    public string RtNowText => string.IsNullOrEmpty(_rtNow) ? Loc.T("Waiting for new files") : _rtNow;
    public string RtNowFolder { get => _rtFolder; set => Set(ref _rtFolder, value); }
    public string RtBadgeText => Loc.T(IsPaused ? "Protection is paused" : S.RealTime ? "Protection is on" : "Protection is off");
    public string RtBadgeColor => IsPaused ? "#FF7800" : S.RealTime ? "#0FB872" : "#E5364B";
    public string RtBadgeBg => IsPaused ? "#1FFF7800" : S.RealTime ? "#1F0FB872" : "#1FE5364B";
    public string RtPendingText => _rt.Pending.ToString();
    public string RtBlockedText => _rtBlocked.ToString();
    public string RtCheckedText => _rtCount.ToString("N0");

    void WireRealTimeActivity()
    {
        _rt.BatchStarted += paths => Dispatcher.UIThread.Post(() =>
        {
            RtBusy = true;
            var name = Path.GetFileName(paths[0]);
            _rtNow = paths.Length == 1 ? name : Loc.F("{0} and {1} more", name, paths.Length - 1);
            RtNowFolder = Path.GetDirectoryName(paths[0]) ?? "";
            Raise(nameof(RtNowText));
        });
        _rt.BatchDone += () => Dispatcher.UIThread.Post(() =>
        {
            RtBusy = false; _rtNow = ""; RtNowFolder = "";
            Raise(nameof(RtNowText), nameof(RtCheckedText), nameof(RtPendingText));
        });
        _rt.FileClean += path => Dispatcher.UIThread.Post(() => AddRtLog("Clean", path, ""));
        _rt.FileSkipped += (path, why) => Dispatcher.UIThread.Post(() => AddRtLog("Skipped", path, why switch
        {
            "too large" => Loc.T("File too large to scan"),
            "busy" => Loc.T("Too many files at once"),
            _ => Loc.T("Couldn't read the file"),
        }));
        var tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        tick.Tick += (_, _) => Raise(nameof(RtPendingText), nameof(RtCheckedText));
        tick.Start();
    }

    void AddRtLog(string result, string path, string detail)
    {
        var e = new RtLogEntry { Name = Path.GetFileName(path), Folder = Path.GetDirectoryName(path) ?? "", Result = result, Detail = detail };
        RtLog.Insert(0, e);
        while (RtLog.Count > 100) RtLog.RemoveAt(RtLog.Count - 1);
        Raise(nameof(HasRtLog));
        var line = $"{e.Time:yyyy-MM-dd HH:mm:ss}\t{result.ToUpperInvariant()}\t{path}\t{detail}";
        _ = Task.Run(() =>
        {
            try
            {
                lock (_logLock)
                {
                    AppData.EnsureDir(Path.GetDirectoryName(RtLogPath)!);
                    if (File.Exists(RtLogPath) && new FileInfo(RtLogPath).Length > 1_000_000) File.Move(RtLogPath, RtLogPath + ".1", true);
                    File.AppendAllText(RtLogPath, line + "\n");
                    AppData.Secure(RtLogPath);
                }
            }
            catch { }
        });
    }

    public void ClearRtLog() { RtLog.Clear(); Raise(nameof(HasRtLog)); }

    public void OpenRtLogFile()
    {
        try
        {
            if (!File.Exists(RtLogPath)) { Directory.CreateDirectory(Path.GetDirectoryName(RtLogPath)!); File.WriteAllText(RtLogPath, ""); }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RtLogPath) { UseShellExecute = true });
        }
        catch { }
    }

    // ---------------- file shredder
    public ObservableCollection<string> ShredQueue { get; } = new();
    bool _shredConfirm, _shredBusy;
    string _shredStatus = "";
    double _shredProg;
    public bool ShredConfirm { get => _shredConfirm; set { Set(ref _shredConfirm, value); Raise(nameof(ShredCanRun)); } }
    public bool ShredBusy { get => _shredBusy; set { Set(ref _shredBusy, value); Raise(nameof(ShredCanRun)); } }
    public string ShredStatus { get => _shredStatus; set => Set(ref _shredStatus, value); }
    public double ShredProgress { get => _shredProg; set => Set(ref _shredProg, value); }
    public bool ShredCanRun => ShredQueue.Count > 0 && _shredConfirm && !_shredBusy;
    public bool HasShredQueue => ShredQueue.Count > 0;

    public void AddShred(IEnumerable<string> paths)
    {
        foreach (var p in paths) if (!ShredQueue.Contains(p) && (File.Exists(p) || Directory.Exists(p))) ShredQueue.Add(p);
        Raise(nameof(ShredCanRun), nameof(HasShredQueue));
    }
    public void ClearShred() { ShredQueue.Clear(); ShredConfirm = false; Raise(nameof(ShredCanRun), nameof(HasShredQueue)); }

    public async Task ShredNow()
    {
        if (!ShredCanRun) return;
        ShredBusy = true; ShredProgress = 0; ShredStatus = Loc.T("Shredding…");
        var items = ShredQueue.ToList();
        var prog = new Progress<(int done, int total, string current)>(t =>
        {
            ShredProgress = t.total == 0 ? 0 : t.done * 100.0 / t.total;
            ShredStatus = Path.GetFileName(t.current);
        });
        var res = await Shredder.ShredAsync(items, prog, CancellationToken.None);
        ShredQueue.Clear(); ShredConfirm = false; ShredBusy = false; ShredProgress = 100;
        ShredStatus = res.Failed == 0
            ? Loc.F("{0} file(s) were permanently erased.", res.Shredded)
            : Loc.F("{0} erased, {1} could not be removed.", res.Shredded, res.Failed);
        Toast?.Invoke(Loc.T("File shredder"), ShredStatus, res.Failed > 0);
        Raise(nameof(ShredCanRun), nameof(HasShredQueue));
    }

    // ---------------- housekeeping & weekly report
    void PeriodicChores()
    {
        CheckPersistence();
        if ((DateTime.Now - _lastDefsCheck).TotalMinutes >= 10) { RefreshDefsBanner(); Refresh(); }
        try
        {
            if ((DateTime.Now - _lastPurge).TotalHours >= 6)
            {
                _lastPurge = DateTime.Now;
                if (S.RetentionDays > 0 && QuarantineService.PurgeOlderThan(S.RetentionDays) > 0) ReloadQuarantine();
            }
            if (S.WeeklyReport && !IsWizard)
            {
                if (S.LastReport == null) { S.LastReport = DateTime.Now; Save(); }
                else if ((DateTime.Now - S.LastReport.Value).TotalDays >= 7) SendWeeklyReport();
            }
        }
        catch { }
    }

    void SendWeeklyReport()
    {
        S.LastReport = DateTime.Now; Save();
        var since = DateTime.Now.AddDays(-7);
        var recent = History.Where(h => h.Date >= since).ToList();
        int scans = recent.Count(h => h.Type != "Real-time" && h.Type != "Ransomware shield");
        int files = recent.Sum(h => h.Files), threats = recent.Sum(h => h.Threats);
        try
        {
            var dir = Path.Combine(AppData.Dir, "reports"); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"week-{DateTime.Now:yyyyMMdd}.txt"), BuildReport());
        }
        catch { }
        Notify(Loc.T("Your weekly report"), Loc.F("This week: {0} scans, {1} files checked, {2} threats found.", scans, files, threats), false);
    }

    // ---------------- statistics (last 7 days)
    public ObservableCollection<DayStat> Week { get; } = new();
    int _weekFiles, _weekThreats;
    public int WeekFiles { get => _weekFiles; set => Set(ref _weekFiles, value); }
    public int WeekThreats { get => _weekThreats; set => Set(ref _weekThreats, value); }

    void RebuildStats()
    {
        var culture = new CultureInfo(Loc.I.IsId ? "id-ID" : "en-US");
        var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(i - 6)).ToList();
        var sums = days.Select(d => (d, files: History.Where(h => h.Date.Date == d).Sum(h => h.Files), thr: History.Where(h => h.Date.Date == d).Sum(h => h.Threats))).ToList();
        var max = Math.Max(1, sums.Max(x => x.files));
        Week.Clear();
        foreach (var x in sums)
            Week.Add(new DayStat(x.d.ToString("ddd", culture), x.files, x.thr, x.files == 0 ? 4 : Math.Max(10, x.files * 100.0 / max), x.thr > 0));
        WeekFiles = sums.Sum(x => x.files); WeekThreats = sums.Sum(x => x.thr);
    }

    // ---------------- security health score
    public ObservableCollection<HealthIssue> Health { get; } = new();
    int _score = 100; IBrush _scoreBrush = Green; string _scoreLabel = "";
    public int HealthScore { get => _score; set { Set(ref _score, value); Raise(nameof(HealthScoreText), nameof(HealthAngle)); } }
    public string HealthScoreText => _score.ToString();
    public double HealthAngle => Math.Min(359.9, Math.Max(0.1, _score * 3.6));
    public IBrush HealthBrush { get => _scoreBrush; set => Set(ref _scoreBrush, value); }
    public string HealthLabel { get => _scoreLabel; set => Set(ref _scoreLabel, value); }
    public bool HasHealthIssues => Health.Count > 0;

    void ComputeHealth()
    {
        int score = 0; var issues = new List<HealthIssue>();
        bool engine = _engineOk || ClamAvLocator.Find("clamscan") != null;
        if (engine) score += 25; else issues.Add(new(Loc.T("Scan engine missing"), Loc.T("Install the scan engine to start scanning."), "engine", Loc.T("Fix")));

        if (!IsOutdated(DefinitionsDate(), DateTime.Now)) score += 25;
        else issues.Add(new(Loc.T("Virus definitions are out of date"), Loc.T("New threats show up daily. Update to stay covered."), "update", Loc.T("Update")));

        if (S.RealTime && !IsPaused) score += 20;
        else if (IsPaused) issues.Add(new(Loc.T("Real-time protection is paused"), Loc.T("It resumes on its own, or you can resume it now."), "resume", Loc.T("Resume")));
        else issues.Add(new(Loc.T("Real-time protection is off"), Loc.T("New files aren't checked automatically."), "realtime", Loc.T("Turn on")));

        var lastScan = History.FirstOrDefault(h => h.Type != "Real-time" && h.Type != "Ransomware shield" && h.Status is "Clean" or "Threats found");
        if (lastScan != null && (DateTime.Now - lastScan.Date).TotalDays <= 7) score += 15;
        else issues.Add(new(Loc.T("No scan in the last 7 days"), Loc.T("A regular scan catches threats that were already on your device."), "scan", Loc.T("Scan now")));

        if (S.ScheduleEnabled) score += 10; else issues.Add(new(Loc.T("Automatic scanning is off"), Loc.T("Let Clamshield scan for you on a schedule."), "schedule", Loc.T("Turn on")));
        if (S.Ransomware) score += 5; else issues.Add(new(Loc.T("Ransomware shield is off"), Loc.T("Get warned if many files are changed at once."), "ransomware", Loc.T("Turn on")));

        Health.Clear(); foreach (var i in issues) Health.Add(i);
        HealthScore = score;
        HealthBrush = score >= 90 ? Green : score >= 70 ? Amber : Red;
        HealthLabel = score >= 90 ? Loc.T("Excellent") : score >= 70 ? Loc.T("Good") : Loc.T("Needs attention");
        Raise(nameof(HasHealthIssues));
    }

    public async void FixHealth(HealthIssue i)
    {
        switch (i.Action)
        {
            case "update": Navigate(AppPage.Update); await UpdateDefinitions(); break;
            case "realtime": RealTimeEnabled = true; break;
            case "resume": ResumeProtection(); break;
            case "scan": await SmartScan(); break;
            case "schedule": ScheduleEnabled = true; break;
            case "ransomware": RansomwareEnabled = true; break;
            case "engine": IsWizard = true; WizardStep = 1; _ = RunEngineStep(); break;
        }
        ComputeHealth();
    }

    // ---------------- scan running programs
    string? _scanNote;

    public async Task ProcessScan()
    {
        if (IsScanning) { Navigate(AppPage.Scan); return; }
        var progs = await Task.Run(RunningPrograms.List);
        var paths = progs.Where(p => p.Warning != "deleted" && File.Exists(p.Path)).Select(p => p.Path).Distinct().ToArray();
        var odd = progs.Where(p => p.Warning != null).ToList();
        _scanNote = odd.Count == 0 ? null :
            Plural(odd.Count, "1 program runs from a temporary folder or a deleted file.", "{0} programs run from a temporary folder or a deleted file.")
            + " (" + string.Join(", ", odd.Take(3).Select(o => o.Name)) + (odd.Count > 3 ? ", …" : "") + ")";
        await StartScan("Running programs", paths, "Process Scan");
    }

    // ---------------- startup watch (persistence)
    DateTime _lastPersist = DateTime.MinValue;
    public bool PersistenceEnabled { get => S.Persistence; set { if (S.Persistence == value) return; S.Persistence = value; Save(); Raise(nameof(PersistenceEnabled)); } }

    void CheckPersistence()
    {
        if (!S.Persistence || IsWizard || (DateTime.Now - _lastPersist).TotalMinutes < 5) return;
        _lastPersist = DateTime.Now;
        _ = Task.Run(() =>
        {
            try { var ch = PersistenceWatch.CheckNow(); if (ch.Count > 0) Dispatcher.UIThread.Post(() => OnPersistence(ch)); }
            catch { }
        });
    }

    void OnPersistence(List<PersistenceChange> changes)
    {
        foreach (var c in changes) AddRtLog("Alert", c.Location, Loc.T(c.Kind) + ": " + Loc.T(c.Detail));
        AddHistory(new ScanRecord { Date = DateTime.Now, Type = "Startup watch", Files = changes.Count, Threats = changes.Count, Status = "Alert" });
        Notify(Loc.T("Startup items changed"),
            Plural(changes.Count, "1 startup item was added or changed. Review it in Tools, under Startup programs.", "{0} startup items were added or changed. Review them in Tools, under Startup programs."), true);
    }

    // ---------------- block list (a ClamAV hash database of your own)
    public ObservableCollection<BlockEntry> Blocked { get; } = new();
    public bool HasBlocked => Blocked.Count > 0;
    public bool NeedsOwnDb => !ClamAvLocator.HasUserDb;

    void ReloadEngineDb() { if (S.FastEngine) Task.Run(ClamdService.Restart); }

    public void BlockFile(string path)
    {
        var err = Blocklist.Add(path);
        if (err != null) { Toast?.Invoke(Loc.T("Couldn't block that file"), err == "missing" ? Loc.T("That file couldn't be read.") : err, true); return; }
        Blocked.Clear(); foreach (var b in Blocklist.All()) Blocked.Add(b);
        Raise(nameof(HasBlocked), nameof(NeedsOwnDb));
        ReloadEngineDb();
        Toast?.Invoke(Loc.T("Block a file"), Loc.F("{0} is now blocked.", Path.GetFileName(path)), false);
    }

    public void Unblock(BlockEntry e)
    {
        Blocklist.Remove(e.Sha256);
        Blocked.Remove(e); Raise(nameof(HasBlocked)); ReloadEngineDb();
    }

    // ---------------- YARA rules
    public ObservableCollection<YaraRule> Yara { get; } = new();
    public bool HasYara => Yara.Count > 0;

    public async Task AddYara(string path)
    {
        var err = await YaraRules.AddAsync(path);
        if (err != null) { Toast?.Invoke(Loc.T("Couldn't use that rule file"), err, true); return; }
        Yara.Clear(); foreach (var y in YaraRules.List()) Yara.Add(y);
        Raise(nameof(HasYara), nameof(NeedsOwnDb)); ReloadEngineDb();
        Toast?.Invoke(Loc.T("YARA rules"), Loc.T("Rule added"), false);
    }

    public void RemoveYara(YaraRule r) { YaraRules.Remove(r); Yara.Remove(r); Raise(nameof(HasYara)); ReloadEngineDb(); }

    // ---------------- engine details
    public sealed record DbRow(string Name, string SigText, string Detail);
    public ObservableCollection<DbRow> EngineDbs { get; } = new();
    bool _engBusy;
    string _engFolder = "", _engOwn = "", _engTotal = "–", _engDaemon = "–", _engMem = "", _engVer = "–";
    public bool EngineBusy { get => _engBusy; set => Set(ref _engBusy, value); }
    public string EngineFolder { get => _engFolder; set => Set(ref _engFolder, value); }
    public string EngineOwnText { get => _engOwn; set => Set(ref _engOwn, value); }
    public string EngineTotalText { get => _engTotal; set => Set(ref _engTotal, value); }
    public string DaemonStateText { get => _engDaemon; set => Set(ref _engDaemon, value); }
    public string DaemonMemText { get => _engMem; set => Set(ref _engMem, value); }
    public string EngineVersionLine { get => _engVer; set => Set(ref _engVer, value); }

    public async Task LoadEngineDetails()
    {
        EngineBusy = true;
        var e = await EngineDetails.LoadAsync();
        EngineDbs.Clear();
        foreach (var d in e.Databases)
            EngineDbs.Add(new DbRow(d.File, d.Signatures.ToString("N0"), string.Join("  ·  ", new[] { d.Version.Length > 0 ? Loc.T("Version") + " " + d.Version : "", d.Built }.Where(x => x.Length > 0))));
        EngineVersionLine = e.Version.Contains('/') ? e.Version.Split('/')[0] : e.Version;
        EngineFolder = e.DbDir;
        EngineOwnText = Loc.T(e.OwnDb ? "Using Clamshield's own database" : "Using the system database");
        EngineTotalText = e.TotalSignatures.ToString("N0");
        DaemonStateText = Loc.T(!e.DaemonRunning ? "Off" : e.DaemonReady ? "Ready" : "Starting");
        DaemonMemText = e.DaemonBytes > 0 ? Size(e.DaemonBytes) : "";
        EngineBusy = false;
        Raise(nameof(NeedsOwnDb));
    }

    public async Task ReloadEngine()
    {
        EngineBusy = true; DaemonStateText = Loc.T("Starting");
        await Task.Run(ClamdService.Restart);
        for (int i = 0; i < 40 && !ClamdService.Ready; i++) await Task.Delay(500);
        await LoadEngineDetails();
    }

    // ---------------- are the virus definitions out of date?
    /// <summary>Screenshots and tests only: pretend the definitions were updated at this time.</summary>
    internal static DateTime? DefsDateOverride;
    static readonly string[] SystemDbDirs = { "/var/lib/clamav", "/usr/local/share/clamav", "/opt/homebrew/var/lib/clamav", "/usr/share/clamav" };
    DateTime _lastDefsCheck = DateTime.MinValue;

    /// <summary>Definitions older than three days (or never fetched) count as out of date; ClamAV publishes new ones daily.</summary>
    public static bool IsOutdated(DateTime? updated, DateTime now) => updated == null || (now - updated.Value).TotalDays > 3;

    /// <summary>When the virus definitions in use were last downloaded: our own database, or the system's when we have none.</summary>
    DateTime? DefinitionsDate()
    {
        if (DefsDateOverride != null) return DefsDateOverride;
        DateTime? best = null;
        void Take(DateTime? t) { if (t != null && (best == null || t > best)) best = t; }
        try { var f = Path.Combine(ClamAvLocator.DbDir, "freshclam.dat"); if (ClamAvLocator.HasUserDb && File.Exists(f)) Take(File.GetLastWriteTime(f)); } catch { }
        if (ClamAvLocator.HasUserDb) Take(S.LastUpdate);
        else
            foreach (var d in SystemDbDirs)
                try { foreach (var n in new[] { "daily.cvd", "daily.cld" }) { var f = Path.Combine(d, n); if (File.Exists(f)) Take(File.GetLastWriteTime(f)); } } catch { }
        return best;
    }

    public bool DefinitionsOutdated => !IsWizard && _engineOk && IsOutdated(DefinitionsDate(), DateTime.Now);
    public bool ShowDefsBanner => DefinitionsOutdated;
    public string DefsBannerTitle
    {
        get
        {
            var d = DefinitionsDate();
            if (d == null) return Loc.T("Your virus definitions haven't been updated yet");
            var days = Math.Max(1, (int)(DateTime.Now - d.Value).TotalDays);
            return Plural(days, "Your virus definitions are 1 day old", "Your virus definitions are {0} days old");
        }
    }

    internal void RefreshForShots() { RefreshDefsBanner(); Refresh(); }
    void RefreshDefsBanner() { _lastDefsCheck = DateTime.Now; Raise(nameof(DefinitionsOutdated), nameof(ShowDefsBanner), nameof(DefsBannerTitle)); }
}
