using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Clamshield.Services;

/// <summary>
/// Runs a private clamd daemon so the virus database is loaded once instead of on every scan.
/// Scans fall back to plain clamscan while it is still starting up.
/// </summary>
public static class ClamdService
{
    static Process? _proc;
    static volatile bool _ready;
    static int _gen;

    public static bool Ready => _ready;
    public static bool Running => _proc is { HasExited: false };
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

    public static void Start()
    {
        Stop();
        var clamd = ClamAvLocator.Find("clamd");
        var scan = ClamAvLocator.Find("clamdscan");
        if (clamd == null || scan == null) return;
        var gen = ++_gen;
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
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    public static void Stop()
    {
        _gen++; _ready = false;
        try { if (_proc is { HasExited: false }) _proc.Kill(true); } catch { }
        _proc = null;
    }

    public static void Restart() => Start();
}
