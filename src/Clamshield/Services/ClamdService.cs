using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Clamshield.Services;

/// <summary>
/// Uses a clamd daemon so the virus database is loaded once instead of on every scan.
/// An already running system clamd is reused when this user can reach it (saves about 1 GB of RAM);
/// otherwise a private one is started. Scans fall back to plain clamscan while it is starting up.
/// </summary>
public static class ClamdService
{
    static Process? _proc;
    static volatile bool _ready;
    static int _gen;

    public static bool Ready => _ready;
    static volatile bool _system;
    public static bool Running => _system || _proc is { HasExited: false };
    /// <summary>True when the scans go to a clamd that belongs to the system, not to one we started.</summary>
    public static bool UsingSystem => _system;
    /// <summary>Extra clamdscan arguments: the system daemon runs as another user, so files are handed over by descriptor.</summary>
    public static string[] ClientArgs => _system ? new[] { "--fdpass" } : Array.Empty<string>();
    public static long MemoryBytes() { try { _proc?.Refresh(); return _proc?.WorkingSet64 ?? 0; } catch { return 0; } }
    public static string Conf => Path.Combine(AppData.Dir, "clamd.conf");

    static void WriteConf()
    {
        Directory.CreateDirectory(AppData.Dir);
        var lines = new List<string> { "Foreground yes", $"MaxThreads {Math.Clamp(Environment.ProcessorCount / 2, 2, 8)}" };
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { lines.Add("TCPSocket 33100"); lines.Add("TCPAddr 127.0.0.1"); }
        else { lines.Add("LocalSocket " + Path.Combine(AppData.Dir, "clamd.sock")); lines.Add("FixStaleSocket yes"); }
        if (ScanOptions.Strict) { lines.Add("DetectPUA yes"); lines.Add("AlertOLE2Macros yes"); lines.Add("AlertEncrypted yes"); }
        lines.Add("ScanArchive " + (ScanOptions.ScanArchives ? "yes" : "no"));
        lines.Add($"MaxFileSize {ScanOptions.MaxFileSizeMb}M");
        if (ClamAvLocator.HasUserDb) lines.Add("DatabaseDirectory " + ClamAvLocator.DbDir);
        File.WriteAllLines(Conf, lines);
    }

    static readonly string[] SystemSockets =
    {
        "/run/clamd.scan/clamd.sock", "/run/clamav/clamd.ctl", "/var/run/clamav/clamd.ctl",
        "/run/clamd/clamd.sock", "/var/run/clamd.scan/clamd.sock", "/tmp/clamd.socket",
    };

    /// <summary>The system daemon uses the system database and default limits, so it only fits when no custom settings are in play.</summary>
    static bool CanShareSystemDaemon =>
        !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !ClamAvLocator.HasUserDb
        && !ScanOptions.Strict && !ScanOptions.ExtraSignatures && ScanOptions.ScanArchives;

    static string? FindSystemDaemon(string clamdscan, int gen)
    {
        if (!CanShareSystemDaemon) return null;
        foreach (var sock in SystemSockets)
        {
            if (gen != _gen) return null;
            try
            {
                Directory.CreateDirectory(AppData.Dir);
                File.WriteAllText(Conf, "LocalSocket " + sock + "\n");
                if (Ping(clamdscan)) return sock;
            }
            catch { }
        }
        return null;
    }

    public static void Start()
    {
        Stop();
        var clamd = ClamAvLocator.Find("clamd");
        var scan = ClamAvLocator.Find("clamdscan");
        if (scan == null) return;
        var gen = ++_gen;
        _ = Task.Run(() =>
        {
            if (FindSystemDaemon(scan, gen) != null)
            {
                if (gen == _gen) { _system = true; _ready = true; }
                return;
            }
            if (gen == _gen && clamd != null) StartPrivate(clamd, scan, gen);
        });
    }

    static void StartPrivate(string clamd, string scan, int gen)
    {
        try
        {
            WriteConf();
            var psi = new ProcessStartInfo(clamd) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("--config-file=" + Conf);
            var p = Process.Start(psi)!;
            _proc = p;
            try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            _ = p.StandardOutput.ReadToEndAsync(); _ = p.StandardError.ReadToEndAsync();   // drain so it never blocks
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => { if (gen == _gen) _ready = false; };
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < 240 && gen == _gen && !p.HasExited; i++)
                {
                    if (Ping(scan)) { if (gen == _gen) _ready = true; return; }
                    await Task.Delay(1000);
                }
            });
        }
        catch { _ready = false; }
    }

    static bool Ping(string clamdscan)
    {
        try
        {
            var psi = new ProcessStartInfo(clamdscan) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("--config-file=" + Conf); psi.ArgumentList.Add("--ping"); psi.ArgumentList.Add("1:1");
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
            if (!p.WaitForExit(5000)) { try { p.Kill(true); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    public static void Stop()
    {
        _gen++; _ready = false; _system = false;
        try { if (_proc is { HasExited: false }) _proc.Kill(true); } catch { }
        _proc = null;
    }

    public static void Restart() => Start();
}
