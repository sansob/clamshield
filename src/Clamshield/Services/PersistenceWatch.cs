using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Clamshield.Services;

public sealed record PersistenceChange(string Kind, string Location, string Detail);

/// <summary>Detects places malware uses to survive a reboot and reports new or changed entries.</summary>
public static class PersistenceWatch
{
    private const string BaselineFile = "persistence.json";
    private static readonly object Gate = new();

    internal sealed class BaselineData
    {
        public bool Exists { get; set; }
        public Dictionary<string, string> Entries { get; set; } = new();
    }

    private static readonly string[] ShellFiles =
    {
        ".bashrc", ".bash_profile", ".profile", ".zshrc", ".zprofile", ".config/fish/config.fish"
    };

    // ---------- public API ----------

    public static Dictionary<string, string> Snapshot()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsWindows()) SnapWindows(map);
            else if (OperatingSystem.IsMacOS()) SnapMac(map, home);
            else SnapLinux(map, home);
        }
        catch { }
        return map;
    }

    public static List<PersistenceChange> Diff(Dictionary<string, string>? baseline, Dictionary<string, string> current)
    {
        var added = new List<PersistenceChange>();
        var changed = new List<PersistenceChange>();
        var removed = new List<PersistenceChange>();
        if (baseline == null) return added;

        foreach (var (key, val) in current)
        {
            if (IsOwn(key)) continue;
            if (!baseline.TryGetValue(key, out var old))
                added.Add(new("Added", key, Describe(key, true)));
            else if (!string.Equals(old, val, StringComparison.Ordinal))
                changed.Add(new("Changed", key, Describe(key, false)));
        }
        foreach (var key in baseline.Keys)
        {
            if (IsOwn(key)) continue;
            if (!current.ContainsKey(key))
                removed.Add(new("Removed", key, "Startup entry removed"));
        }
        added.AddRange(changed);
        added.AddRange(removed);
        return added;
    }

    public static List<PersistenceChange> CheckNow()
    {
        lock (Gate)
        {
            try
            {
                var snap = Snapshot();
                BaselineData? saved = null;
                try { saved = AppData.Load<BaselineData>(BaselineFile); } catch { }
                var result = saved is { Exists: true }
                    ? Diff(saved.Entries, snap)
                    : new List<PersistenceChange>();
                try { AppData.Save(BaselineFile, new BaselineData { Exists = true, Entries = snap }); } catch { }
                return result;
            }
            catch { return new List<PersistenceChange>(); }
        }
    }

    // ---------- helpers ----------

    private static bool IsOwn(string key)
    {
        string k = key.Replace('\\', '/');
        string leaf = k[(k.LastIndexOf('/') + 1)..];
        if (leaf.Equals("clamshield.desktop", StringComparison.OrdinalIgnoreCase)) return true;
        if (leaf.Equals("com.clamshield.app.plist", StringComparison.OrdinalIgnoreCase)) return true;
        if (k.StartsWith("reg:", StringComparison.Ordinal) &&
            k.EndsWith(":Clamshield", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string Describe(string key, bool added)
    {
        string k = key.Replace('\\', '/');
        if (k.StartsWith("crontab:")) return added ? "New scheduled job (crontab)" : "Your scheduled jobs (crontab) changed";
        if (k.StartsWith("task:")) return added ? "New scheduled task" : "Scheduled task changed";
        if (k.StartsWith("reg:")) return added ? "New startup entry in the registry" : "Registry startup entry changed";
        if (k.EndsWith("authorized_keys"))
            return added ? "A new SSH authorized keys file appeared" : "Your SSH authorized keys changed";
        if (k.EndsWith("ld.so.preload")) return "Library preload list changed";
        if (k.Contains("/systemd/")) return added ? "New background service" : "Background service changed";
        if (k.Contains("LaunchAgents") || k.Contains("LaunchDaemons"))
            return added ? "New launch item" : "Launch item changed";
        if (ShellFiles.Any(s => k.EndsWith("/" + s)) || k.EndsWith("config.fish"))
            return added ? "New shell startup file" : "Shell startup file changed";
        return added ? "New startup entry" : "Startup entry changed";
    }

    private static string HashBytes(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
    private static string HashText(string s) => HashBytes(Encoding.UTF8.GetBytes(s));

    private static void AddFile(Dictionary<string, string> map, string path, string? keyOverride = null)
    {
        try
        {
            if (!File.Exists(path)) return;
            string hash = HashBytes(File.ReadAllBytes(path));
            map[keyOverride ?? path] = hash;
        }
        catch { }
    }

    private static void AddDir(Dictionary<string, string> map, string dir, params string[] patterns)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var pat in patterns)
                foreach (var f in Directory.EnumerateFiles(dir, pat))
                    AddFile(map, f);
        }
        catch { }
    }

    private static void AddAuthorizedKeys(Dictionary<string, string> map, string home)
    {
        string path = Path.Combine(home, ".ssh", "authorized_keys");
        try
        {
            if (!File.Exists(path)) return;
            var text = File.ReadAllText(path);
            map[path] = HashText(text);
        }
        catch { }
    }

    private static void AddCrontab(Dictionary<string, string> map)
    {
        var (ok, output) = Run("crontab", "-l", 2000);
        if (!ok) return;
        if (output.Contains("no crontab", StringComparison.OrdinalIgnoreCase)) return;
        if (string.IsNullOrWhiteSpace(output)) return;
        map["crontab:user"] = HashText(output);
    }

    private static (bool ok, string output) Run(string file, string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return (false, "");
            var outTask = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(true); } catch { }
                return (false, "");
            }
            if (!outTask.Wait(1000)) return (false, "");
            return (p.ExitCode == 0, outTask.Result);
        }
        catch { return (false, ""); }
    }

    // ---------- platforms ----------

    private static void AddShellFiles(Dictionary<string, string> map, string home)
    {
        foreach (var f in ShellFiles)
            AddFile(map, Path.Combine(home, f.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void SnapLinux(Dictionary<string, string> map, string home)
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string cfg = string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".config") : xdg;
        AddDir(map, Path.Combine(cfg, "autostart"), "*");
        AddDir(map, "/etc/xdg/autostart", "*");
        string units = Path.Combine(home, ".config", "systemd", "user");
        AddDir(map, units, "*.service", "*.timer");
        AddCrontab(map);
        AddAuthorizedKeys(map, home);
        AddShellFiles(map, home);
        AddFile(map, "/etc/ld.so.preload");
    }

    private static void SnapMac(Dictionary<string, string> map, string home)
    {
        AddDir(map, Path.Combine(home, "Library", "LaunchAgents"), "*.plist");
        AddDir(map, "/Library/LaunchAgents", "*.plist");
        AddDir(map, "/Library/LaunchDaemons", "*.plist");
        AddCrontab(map);
        AddShellFiles(map, home);
        AddAuthorizedKeys(map, home);
    }

    private static void SnapWindows(Dictionary<string, string> map)
    {
        if (!OperatingSystem.IsWindows()) return;
        string[] keys =
        {
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce",
            @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run",
            @"HKLM\Software\Microsoft\Windows\CurrentVersion\RunOnce",
        };
        foreach (var key in keys)
        {
            try
            {
                var (ok, output) = Run("reg.exe", $"query \"{key}\"", 3000);
                if (!ok) continue;
                foreach (var raw in output.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    if (!line.StartsWith("    ")) continue; // value lines are indented
                    var parts = line.Trim().Split(new[] { "    " }, 3, StringSplitOptions.None);
                    if (parts.Length < 2) continue;
                    string name = parts[0].Trim();
                    string data = parts.Length > 2 ? parts[2].Trim() : "";
                    map[$"reg:{key}:{name}"] = HashText(data);
                }
            }
            catch { }
        }

        AddDir(map, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "*");
        AddDir(map, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "*");

        try
        {
            var (ok, output) = Run("schtasks.exe", "/query /fo csv /nh", 3000);
            if (ok)
            {
                foreach (var raw in output.Split('\n'))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith('"')) continue;
                    int end = line.IndexOf('"', 1);
                    if (end <= 1) continue;
                    string name = line.Substring(1, end - 1);
                    map[$"task:{name}"] = "present";
                }
            }
        }
        catch { }
    }
}
