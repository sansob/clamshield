using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Clamshield.Services;

/// <summary>Finds the ClamAV binaries on Windows, Linux and macOS.</summary>
public static class ClamAvLocator
{
    static readonly string Ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";

    public static string? Find(string name)
    {
        var file = name + Ext;
        var dirs = new List<string>();
        var env = Environment.GetEnvironmentVariable("CLAMAV_DIR");
        if (!string.IsNullOrEmpty(env)) dirs.Add(env);
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)"), @"C:\ClamAV" })
                if (!string.IsNullOrEmpty(root)) dirs.Add(root.EndsWith("ClamAV") ? root : Path.Combine(root, "ClamAV"));
        }
        else
        {
            dirs.AddRange(new[] { "/usr/bin", "/usr/local/bin", "/opt/homebrew/bin", "/opt/local/bin", "/snap/bin" });
        }

        foreach (var d in dirs.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            try { var p = Path.Combine(d.Trim(), file); if (File.Exists(p)) return p; } catch { }
        }
        return FindInEngineDir(file);
    }

    /// <summary>Folder where Clamshield installs ClamAV itself (Windows / macOS).</summary>
    public static readonly string EngineDir = Path.Combine(AppData.BigDataDir, "engine");
    static readonly Dictionary<string, string> _engineCache = new();

    static string? FindInEngineDir(string file)
    {
        lock (_engineCache)
        {
            if (_engineCache.TryGetValue(file, out var c) && File.Exists(c)) return c;
            if (!Directory.Exists(EngineDir)) return null;
            try
            {
                var hit = Directory.EnumerateFiles(EngineDir, file, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 8, IgnoreInaccessible = true }).FirstOrDefault();
                if (hit != null) _engineCache[file] = hit;
                return hit;
            }
            catch { return null; }
        }
    }

    public static void ForgetCache() { lock (_engineCache) _engineCache.Clear(); }

    /// <summary>App-owned virus database, so updating never needs admin rights.</summary>
    public static readonly string DbDir = Path.Combine(AppData.BigDataDir, "db");
    public static bool HasUserDb => File.Exists(Path.Combine(DbDir, "main.cvd")) || File.Exists(Path.Combine(DbDir, "main.cld"));
    public static string[] DbArgs() => HasUserDb ? new[] { "--database=" + DbDir } : Array.Empty<string>();

    /// <summary>Writes (if needed) and returns a private freshclam.conf pointing at <see cref="DbDir"/>.</summary>
    public static string EnsureFreshclamConfig()
    {
        Directory.CreateDirectory(DbDir);
        var conf = Path.Combine(AppData.Dir, "freshclam.conf");
        var text = $"DatabaseDirectory {DbDir}\nDatabaseMirror database.clamav.net\n";
        if (ScanOptions.ExtraSignatures)
            foreach (var f in ScanOptions.ExtraSignatureFiles) text += $"DatabaseCustomURL {ScanOptions.ExtraSignatureBase}{f}\n";
        File.WriteAllText(conf, text);
        return conf;
    }

    public static void RemoveExtraSignatures()
    {
        foreach (var f in ScanOptions.ExtraSignatureFiles) { try { File.Delete(Path.Combine(DbDir, f)); } catch { } }
    }

    public static string? Version()
    {
        var exe = Find("clamscan");
        if (exe == null) return null;
        try
        {
            var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--version");
            foreach (var a in DbArgs()) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(5000);
            return o;
        }
        catch { return null; }
    }

    public static string InstallHint =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Download ClamAV from clamav.net and install it."
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Install it with: brew install clamav"
        : "Install it with your package manager, e.g. sudo dnf install clamav clamav-update  /  sudo apt install clamav";
}
