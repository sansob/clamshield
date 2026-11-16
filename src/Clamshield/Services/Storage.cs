using System.Text.Json;
using Clamshield.Models;

namespace Clamshield.Services;

public static class AppData
{
    public static readonly string Dir = Path.Combine(ConfigRoot(), "Clamshield");

    // GetFolderPath returns "" on Linux/macOS when the folder does not exist yet; never fall back to the working directory
    static string ConfigRoot()
    {
        var p = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrEmpty(p)) return p;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        return Path.Combine(string.IsNullOrEmpty(home) ? Path.GetTempPath() : home, ".config");
    }

    /// <summary>
    /// Where the large, re-downloadable data lives (virus database, downloaded engine). On Windows that is the local,
    /// non-roaming profile so a company profile does not sync hundreds of megabytes; elsewhere it is the same folder as <see cref="Dir"/>.
    /// </summary>
    public static readonly string BigDataDir = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "Clamshield")
        : Dir;

    const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    const UnixFileMode PrivateDir = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Makes a file or folder readable by its owner only (Linux and macOS; Windows profiles are per-user already).</summary>
    public static void Secure(string path, bool isDir = false)
    {
        if (OperatingSystem.IsWindows()) return;
        try { File.SetUnixFileMode(path, isDir ? PrivateDir : PrivateFile); } catch { }
    }

    /// <summary>Creates a folder that only the owner can enter.</summary>
    public static void EnsureDir(string dir)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dir);
            else if (!Directory.Exists(dir)) Directory.CreateDirectory(dir, PrivateDir);
        }
        catch { }
    }

    /// <summary>Locks down Clamshield's data folder (settings, history, quarantine, logs) for the current user.</summary>
    public static void Harden()
    {
        try
        {
            EnsureDir(Dir); Secure(Dir, true);
            foreach (var d in new[] { "quarantine", "logs", "reports" })
            { var p = Path.Combine(Dir, d); if (Directory.Exists(p)) Secure(p, true); }
            foreach (var f in Directory.EnumerateFiles(Dir, "*.json")) Secure(f);
            var q = Path.Combine(Dir, "quarantine");
            if (Directory.Exists(q)) foreach (var f in Directory.EnumerateFiles(q)) Secure(f);
        }
        catch { }
    }
    public static T Load<T>(string file) where T : new()
    {
        try { var p = Path.Combine(Dir, file); return File.Exists(p) ? JsonSerializer.Deserialize(File.ReadAllText(p), AppJson.Info<T>()) ?? new T() : new T(); }
        catch { return new T(); }
    }
    public static void Save<T>(string file, T value)
    {
        try
        {
            EnsureDir(Dir);
            var path = Path.Combine(Dir, file);
            File.WriteAllText(path, JsonSerializer.Serialize(value, AppJson.Info<T>()));
            Secure(path);
        }
        catch { }
    }
}

public static class SettingsService
{
    public static AppSettings Load() => AppData.Load<AppSettings>("settings.json");
    public static void Save(AppSettings s) => AppData.Save("settings.json", s);
}

public static class HistoryService
{
    public static List<ScanRecord> Load() => AppData.Load<List<ScanRecord>>("history.json");
    public static void Save(IEnumerable<ScanRecord> items) => AppData.Save("history.json", items.Take(200).ToList());
}
