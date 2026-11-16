using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using Avalonia.Media;
using Avalonia.Threading;
using Clamshield.Models;
using Clamshield.Services;

namespace Clamshield.ViewModels;

/// <summary>Pause protection, extra tools (link check, password check, startup programs, clean-up) and the HTML report.</summary>
public partial class MainViewModel
{
    // ---------------- pause protection
    DateTime? _pausedUntil;
    DispatcherTimer? _pauseTimer;
    public bool IsPaused => _pausedUntil != null;
    public bool CanPause => S.RealTime && !IsPaused;
    public bool ShowRtDetails => S.RealTime && !IsPaused;
    public string PauseText => _pausedUntil == null ? "" :
        _pausedUntil == DateTime.MaxValue ? Loc.T("Paused until you restart Clamshield") : Loc.F("Paused until {0}", _pausedUntil.Value.ToString("HH:mm"));

    public void PauseProtection(int minutes)
    {
        _pausedUntil = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : DateTime.MaxValue;
        _pauseTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _pauseTimer.Tick -= PauseTick; _pauseTimer.Tick += PauseTick; _pauseTimer.Start();
        ApplyRealTime(); RaisePause(); Refresh();
    }

    void PauseTick(object? s, EventArgs e) { if (_pausedUntil != null && DateTime.Now >= _pausedUntil) ResumeProtection(); }

    public void ResumeProtection()
    {
        _pausedUntil = null; _pauseTimer?.Stop();
        ApplyRealTime(); RaisePause(); Refresh();
    }

    void RaisePause() => Raise(nameof(IsPaused), nameof(CanPause), nameof(ShowRtDetails), nameof(PauseText),
        nameof(RtBadgeText), nameof(RtBadgeColor), nameof(RtBadgeBg), nameof(RealTimeStatus));

    // ---------------- link check (VirusTotal)
    string _linkInput = "", _linkText = "", _linkShown = "";
    bool _linkBusy, _hasLink;
    IBrush _linkBrush = Green;
    public string LinkInput { get => _linkInput; set => Set(ref _linkInput, value); }
    public bool LinkBusy { get => _linkBusy; set => Set(ref _linkBusy, value); }
    public bool HasLinkResult { get => _hasLink; set => Set(ref _hasLink, value); }
    public string LinkResultText { get => _linkText; set => Set(ref _linkText, value); }
    public string LinkShown { get => _linkShown; set => Set(ref _linkShown, value); }
    public IBrush LinkResultBrush { get => _linkBrush; set => Set(ref _linkBrush, value); }

    public async Task CheckLink()
    {
        if (string.IsNullOrWhiteSpace(_linkInput) || LinkBusy) return;
        if (string.IsNullOrWhiteSpace(S.VtKey))
        { Toast?.Invoke(Loc.T("VirusTotal key needed"), Loc.T("Add your free API key in Settings, under Scanning."), true); return; }
        LinkBusy = true; HasLinkResult = false;
        var r = await VirusTotalUrl.LookupAsync(_linkInput, S.VtKey);
        LinkBusy = false;
        LinkShown = _linkInput.Trim();
        LinkResultText = r.Status switch
        {
            VtStatus.Detected => Loc.F("{0} of {1} security engines flag this link.", r.Malicious + r.Suspicious, r.Total),
            VtStatus.Clean => Loc.F("None of the {0} security engines flag this link.", r.Total),
            VtStatus.NotFound => Loc.T("VirusTotal has no record of this link."),
            VtStatus.RateLimited => Loc.T("Too many lookups. Free keys allow 4 per minute, try again shortly."),
            VtStatus.NoKey => Loc.T("Add your free API key in Settings, under Scanning."),
            _ => r.Message,
        };
        LinkResultBrush = r.Status == VtStatus.Detected ? Red : r.Status == VtStatus.Clean ? Green : Amber;
        HasLinkResult = true;
    }

    // ---------------- password check (Have I Been Pwned, k-anonymity)
    string _pwInput = "", _pwText = "";
    bool _pwBusy, _hasPw;
    IBrush _pwBrush = Green;
    public string PwInput { get => _pwInput; set => Set(ref _pwInput, value); }
    public bool PwBusy { get => _pwBusy; set => Set(ref _pwBusy, value); }
    public bool HasPwResult { get => _hasPw; set => Set(ref _hasPw, value); }
    public string PwResultText { get => _pwText; set => Set(ref _pwText, value); }
    public IBrush PwResultBrush { get => _pwBrush; set => Set(ref _pwBrush, value); }

    public async Task CheckPassword()
    {
        if (string.IsNullOrEmpty(_pwInput) || PwBusy) return;
        PwBusy = true; HasPwResult = false;
        var pw = _pwInput;
        PwInput = "";   // never keep the password around longer than needed
        var n = await PasswordBreach.CountAsync(pw);
        PwBusy = false;
        if (n < 0) { PwResultText = Loc.T("Couldn't reach the breach service. Check your connection."); PwResultBrush = Amber; }
        else if (n == 0) { PwResultText = Loc.T("Not found in known data breaches. Length and uniqueness still matter."); PwResultBrush = Green; }
        else { PwResultText = Loc.F("Found {0:N0} times in known data breaches. Don't use it anywhere.", n); PwResultBrush = Red; }
        HasPwResult = true;
    }

    // ---------------- startup programs
    public sealed class StartupRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        readonly StartupEntry _entry; readonly Action<string, string, bool> _toast;
        bool _enabled;
        public StartupRow(StartupEntry e, Action<string, string, bool> toast) { _entry = e; _enabled = e.Enabled; _toast = toast; }
        public string Name => _entry.Name;
        public string Command => _entry.Command;
        public string Source => Loc.T(_entry.Source);
        public bool CanChange => _entry.CanChange;
        public string Note => _entry.CanChange ? Source : Source + "  ·  " + Loc.T("Can't be changed here");
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                var err = StartupItems.SetEnabled(_entry, value);
                if (err != null) { _toast(Loc.T("Couldn't change the startup setting"), err, true); }
                else _enabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            }
        }
    }

    public ObservableCollection<StartupRow> Startup { get; } = new();
    bool _startupBusy;
    public bool StartupBusy { get => _startupBusy; set => Set(ref _startupBusy, value); }
    public bool HasStartup => Startup.Count > 0;

    public async Task LoadStartup()
    {
        StartupBusy = true;
        var list = await Task.Run(StartupItems.List);
        Startup.Clear();
        foreach (var e in list) Startup.Add(new StartupRow(e, (t, m, w) => Toast?.Invoke(t, m, w)));
        StartupBusy = false; Raise(nameof(HasStartup));
    }

    // ---------------- disk clean-up
    public sealed class CleanRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        bool _sel = true;
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required string SizeText { get; init; }
        public required long Bytes { get; init; }
        public bool Selected { get => _sel; set { _sel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); } }
    }

    public ObservableCollection<CleanRow> CleanItems { get; } = new();
    bool _cleanBusy, _cleanAnalyzed;
    string _cleanStatus = "";
    public bool CleanBusy { get => _cleanBusy; set { Set(ref _cleanBusy, value); Raise(nameof(CleanCanRun)); } }
    public bool CleanAnalyzed { get => _cleanAnalyzed; set => Set(ref _cleanAnalyzed, value); }
    public string CleanStatus { get => _cleanStatus; set => Set(ref _cleanStatus, value); }
    public bool HasCleanItems => CleanItems.Count > 0;
    public bool CleanCanRun => HasCleanItems && !CleanBusy;

    static string Size(long b) => b >= 1L << 30 ? $"{b / 1073741824.0:0.0} GB" : b >= 1L << 20 ? $"{b / 1048576.0:0.0} MB" : $"{Math.Max(1, b / 1024)} KB";

    (string name, string desc) CleanText(string id) => id switch
    {
        "temp" => (Loc.T("Temporary files"), Loc.T("Files in the temporary folder untouched for 2 days.")),
        "thumbnails" => (Loc.T("Thumbnail cache"), Loc.T("Small preview images your file manager can rebuild.")),
        "trash" => (Loc.T("Trash"), Loc.T("Files you already deleted.")),
        _ => (id, ""),
    };

    public async Task AnalyzeCleanup()
    {
        CleanBusy = true; CleanStatus = Loc.T("Analyzing…");
        var cats = await CleanupService.AnalyzeAsync(CancellationToken.None);
        CleanItems.Clear();
        foreach (var c in cats)
        {
            var (n, d) = CleanText(c.Id);
            CleanItems.Add(new CleanRow { Id = c.Id, Name = n, Description = d, SizeText = Size(c.Bytes), Bytes = c.Bytes });
        }
        CleanAnalyzed = true; CleanBusy = false; Raise(nameof(HasCleanItems), nameof(CleanCanRun));
        CleanStatus = CleanItems.Count == 0 ? Loc.T("Nothing to clean. Your disk is tidy.") : Loc.F("Total you can free: {0}", Size(CleanItems.Sum(x => x.Bytes)));
    }

    public async Task RunCleanup()
    {
        var ids = CleanItems.Where(c => c.Selected).Select(c => c.Id).ToList();
        if (ids.Count == 0 || CleanBusy) return;
        CleanBusy = true; CleanStatus = Loc.T("Cleaning…");
        var res = await CleanupService.CleanAsync(ids, null, CancellationToken.None);
        CleanBusy = false;
        CleanStatus = Loc.F("Freed {0}.", Size(res.FreedBytes));
        Toast?.Invoke(Loc.T("Disk clean-up"), CleanStatus, res.Failed > 0 && res.Deleted == 0);
        await AnalyzeCleanup();
        CleanStatus = Loc.F("Freed {0}.", Size(res.FreedBytes)) + (CleanItems.Count > 0 ? "  " + Loc.F("Total you can free: {0}", Size(CleanItems.Sum(x => x.Bytes))) : "");
    }

    // ---------------- HTML report
    public string BuildHtmlReport()
    {
        string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Clamshield</title><style>");
        sb.Append("body{font-family:system-ui,Segoe UI,Roboto,sans-serif;background:#f3f5f9;color:#1b2336;margin:0;padding:32px}.w{max-width:880px;margin:auto}");
        sb.Append("h1{margin:0 0 4px}h2{margin:28px 0 10px;font-size:18px}.m{color:#6b7488;font-size:14px}.g{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-top:18px}");
        sb.Append(".c{background:#fff;border:1px solid #e6eaf2;border-radius:14px;padding:16px}.n{font-size:28px;font-weight:700}table{width:100%;border-collapse:collapse;background:#fff;border-radius:14px;overflow:hidden;border:1px solid #e6eaf2}");
        sb.Append("th,td{text-align:left;padding:10px 14px;border-bottom:1px solid #eef1f7;font-size:14px}th{background:#f6f8fc;color:#6b7488;font-weight:600}.b{display:flex;gap:10px;align-items:flex-end;height:130px;background:#fff;border:1px solid #e6eaf2;border-radius:14px;padding:16px}");
        sb.Append(".b div{flex:1;text-align:center;font-size:12px;color:#6b7488}.b i{display:block;background:linear-gradient(#ffa24d,#ff6a00);border-radius:6px;margin:0 auto 6px;width:60%}.ok{color:#0fb872}.bad{color:#e5364b}@media print{body{background:#fff}}");
        sb.Append("</style></head><body><div class=\"w\">");
        sb.Append($"<h1>{E(Loc.T("Protection report"))}</h1><div class=\"m\">Clamshield · {E(Loc.T("Generated"))} {DateTime.Now:yyyy-MM-dd HH:mm} · {E(EngineInfo)}</div>");
        sb.Append("<div class=\"g\">");
        sb.Append($"<div class=\"c\"><div class=\"m\">{E(Loc.T("Security health"))}</div><div class=\"n\">{HealthScore}</div></div>");
        sb.Append($"<div class=\"c\"><div class=\"m\">{E(Loc.T("files checked"))}</div><div class=\"n\">{WeekFiles:N0}</div></div>");
        sb.Append($"<div class=\"c\"><div class=\"m\">{E(Loc.T("threats found"))}</div><div class=\"n bad\">{WeekThreats}</div></div>");
        sb.Append($"<div class=\"c\"><div class=\"m\">{E(Loc.T("In quarantine"))}</div><div class=\"n\">{Quarantined.Count}</div></div></div>");
        sb.Append($"<h2>{E(Loc.T("Last 7 days"))}</h2><div class=\"b\">");
        foreach (var d in Week) sb.Append($"<div><i style=\"height:{d.BarHeight:0}px\"></i>{E(d.Label)}</div>");
        sb.Append($"</div><h2>{E(Loc.T("Recent activity"))}</h2><table><tr><th>{E(Loc.T("Date"))}</th><th>{E(Loc.T("Type"))}</th><th>{E(Loc.T("Result"))}</th><th>{E(Loc.T("Details"))}</th></tr>");
        foreach (var h in History.Take(50))
            sb.Append($"<tr><td>{h.Date:yyyy-MM-dd HH:mm}</td><td>{E(h.TypeText)}</td><td class=\"{(h.Status is "Clean" ? "ok" : h.Status is "Cancelled" ? "" : "bad")}\">{E(h.StatusText)}</td><td>{E(h.Summary)}</td></tr>");
        sb.Append("</table>");
        if (Quarantined.Count > 0)
        {
            sb.Append($"<h2>{E(Loc.T("Quarantine"))}</h2><table><tr><th>{E(Loc.T("Date"))}</th><th>{E(Loc.T("Details"))}</th><th>File</th></tr>");
            foreach (var q in Quarantined) sb.Append($"<tr><td>{q.Date:yyyy-MM-dd HH:mm}</td><td class=\"bad\">{E(q.Virus)}</td><td>{E(q.OriginalPath)}</td></tr>");
            sb.Append("</table>");
        }
        sb.Append("</div></body></html>");
        return sb.ToString();
    }
}
