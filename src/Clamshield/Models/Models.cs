namespace Clamshield.Models;

public record ThreatItem(string Path, string Virus)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public class QuarantineItem
{
    public string Id { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string Virus { get; set; } = "";
    public DateTime Date { get; set; }
    public int Mode { get; set; }   // original Unix permissions, restored with the file
    public string FileName => System.IO.Path.GetFileName(OriginalPath);
    public string DateText => Date.ToString("dd MMM yyyy, HH:mm");
}

public class ScanState
{
    public volatile bool Preparing = true;
    public volatile string Current = "";
    public volatile string? Error;
    public int Total;
    public int Scanned;
    public int Infected;
}

public class ScanRecord
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = "";
    public int Files { get; set; }
    public int Threats { get; set; }
    public double Seconds { get; set; }
    public string Status { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore] public string DateText => Date.ToString("dd MMM yyyy, HH:mm");
    [System.Text.Json.Serialization.JsonIgnore] public string TypeText => Clamshield.Services.Loc.T(Type);
    [System.Text.Json.Serialization.JsonIgnore] public string StatusText => Clamshield.Services.Loc.T(Status);
    [System.Text.Json.Serialization.JsonIgnore] public string Summary =>
        (Files == 1 ? Clamshield.Services.Loc.T("1 file") : Clamshield.Services.Loc.F("{0:N0} files", Files)) + "  ·  " +
        (Threats == 1 ? Clamshield.Services.Loc.T("1 threat") : Clamshield.Services.Loc.F("{0} threats", Threats)) + "  ·  " +
        TimeSpan.FromSeconds(Seconds).ToString(@"mm\:ss");
    [System.Text.Json.Serialization.JsonIgnore] public string StatusColor =>
        Status is "Clean" ? "#0FB872" : Status is "Cancelled" ? "#6B7488" : Status is "Alert" ? "#FF7800" : "#E5364B";
    [System.Text.Json.Serialization.JsonIgnore] public string BadgeColor =>
        Status is "Clean" ? "#1F0FB872" : Status is "Cancelled" ? "#1F8F9AB0" : Status is "Alert" ? "#1FFF7800" : "#1FE5364B";
}

public class AppSettings
{
    public bool RealTime { get; set; } = true;
    public bool AutoQuarantine { get; set; } = true;
    public bool ScheduleEnabled { get; set; }
    public int Frequency { get; set; }          // 0 = daily, 1 = weekly
    public int Day { get; set; }                // 0 = Monday ... 6 = Sunday
    public string Time { get; set; } = "12:00";
    public bool UpdateBeforeScheduled { get; set; } = true;
    public bool RunInTray { get; set; } = !OperatingSystem.IsLinux(); // GNOME has no tray by default
    public bool Notifications { get; set; } = true;
    public DateTime? LastScheduledRun { get; set; }
    public bool ScanRemovable { get; set; } = true;
    public bool SetupDone { get; set; }
    public bool FastEngine { get; set; } = true;
    public DateTime? LastUpdate { get; set; }

    // appearance & system
    public int Theme { get; set; }                 // 0 system, 1 light, 2 dark
    public string Language { get; set; } = "en";   // en | id
    public bool AutoStart { get; set; }
    public bool ContextMenu { get; set; }

    // protection
    public List<string> WatchFolders { get; set; } = new();
    public bool WatchEmail { get; set; }
    public bool NotifyClean { get; set; }
    public bool Ransomware { get; set; } = true;
    public bool PauseOnBattery { get; set; } = true;
    public bool PauseFullscreen { get; set; } = true;

    // scanning
    public bool ScanArchives { get; set; } = true;
    public int MaxFileSizeMb { get; set; } = 100;
    public bool SkipMedia { get; set; }
    public int RetentionDays { get; set; } = 30;   // 0 = keep forever
    public string VtKey { get; set; } = "";
    public List<ScanProfile> Profiles { get; set; } = new();
    public bool WeeklyReport { get; set; } = true;
    public DateTime? LastReport { get; set; }
    public bool Strict { get; set; }
    public bool ExtraSignatures { get; set; }
    public bool Persistence { get; set; } = true;
    public List<string> Exclusions { get; set; } = new();
}

public class ScanProfile
{
    public string Name { get; set; } = "";
    public List<string> Paths { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public string Summary =>
        Paths.Count == 1 ? Paths[0] : $"{Paths.Count} folders";
}

public record DayStat(string Label, int Files, int Threats, double BarHeight, bool HasThreat);

public record HealthIssue(string Title, string Hint, string Action, string ActionText);

public class RtLogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;
    public string Name { get; init; } = "";
    public string Folder { get; init; } = "";
    public string Result { get; init; } = "";    // Clean | Threat found | Skipped
    public string Detail { get; init; } = "";
    public string TimeText => Time.ToString("HH:mm:ss");
    public string ResultText => Clamshield.Services.Loc.T(Result);
    public string Color => Result == "Clean" ? "#0FB872" : Result == "Skipped" ? "#8F9AB0" : Result == "Alert" ? "#FF7800" : "#E5364B";
    public string Badge => Result == "Clean" ? "#1F0FB872" : Result == "Skipped" ? "#1F8F9AB0" : Result == "Alert" ? "#1FFF7800" : "#1FE5364B";
    public string Sub => string.IsNullOrEmpty(Detail) ? Folder : Detail + "  ·  " + Folder;
}
