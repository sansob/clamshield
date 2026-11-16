using System.Diagnostics;

namespace Clamshield.Services;

public sealed record DbInfo(string File, long Signatures, string Version, string Built);
public sealed record EngineSnapshot(string Version, string DbDir, bool OwnDb, List<DbInfo> Databases, long TotalSignatures,
                                    bool DaemonRunning, bool DaemonReady, long DaemonBytes);

/// <summary>Reads what the scan engine has loaded: databases and their signature counts, and the daemon's state.</summary>
public static class EngineDetails
{
    static readonly string[] SystemDirs = { "/var/lib/clamav", "/usr/local/share/clamav", "/opt/homebrew/var/lib/clamav", "/usr/share/clamav" };

    public static async Task<EngineSnapshot> LoadAsync()
    {
        var version = await Task.Run(() => ClamAvLocator.Version()) ?? "";
        bool own = ClamAvLocator.HasUserDb;
        var dir = own ? ClamAvLocator.DbDir : SystemDirs.FirstOrDefault(d => Directory.Exists(d) && Directory.EnumerateFiles(d, "*.c?d").Any()) ?? ClamAvLocator.DbDir;
        var list = new List<DbInfo>();
        if (Directory.Exists(dir))
        {
            var files = Directory.EnumerateFiles(dir).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".cvd" or ".cld" or ".hsb" or ".hdb" or ".ndb" or ".ldb" or ".yar" or ".yara").OrderBy(f => f).ToList();
            list = (await Task.WhenAll(files.Select(f => Task.Run(() => Describe(f))))).ToList();
        }
        bool running = ClamdService.Running;
        return new EngineSnapshot(version, dir, own, list, list.Sum(d => d.Signatures), running, ClamdService.Ready, running ? ClamdService.MemoryBytes() : 0);
    }

    static DbInfo Describe(string file)
    {
        var name = Path.GetFileName(file); var ext = Path.GetExtension(file).ToLowerInvariant();
        try
        {
            if (ext is ".cvd" or ".cld")
            {
                var sig = ClamAvLocator.Find("sigtool");
                if (sig != null)
                {
                    var psi = new ProcessStartInfo(sig) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                    psi.ArgumentList.Add("--info=" + file);
                    using var p = Process.Start(psi)!;
                    var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(8000);
                    string Get(string key) => o.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(key + ":"))?[(key.Length + 1)..].Trim() ?? "";
                    long.TryParse(Get("Signatures"), out var n);
                    return new DbInfo(name, n, Get("Version"), Get("Build time"));
                }
                return new DbInfo(name, 0, "", "");
            }
            if (ext is ".yar" or ".yara")
                return new DbInfo(name, File.ReadLines(file).Count(l => l.TrimStart().StartsWith("rule ")), "", "");
            return new DbInfo(name, File.ReadLines(file).Count(l => l.Length > 0 && !l.StartsWith('#')), "", "");
        }
        catch { return new DbInfo(name, 0, "", ""); }
    }
}
