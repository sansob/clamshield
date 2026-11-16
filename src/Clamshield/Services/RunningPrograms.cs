using System.Diagnostics;

namespace Clamshield.Services;

public sealed record RunningProgram(int Pid, string Name, string Path, string? Warning);   // Warning: null | "temp" | "deleted"

/// <summary>Lists the programs that are running right now (only the ones the current user may inspect).</summary>
public static class RunningPrograms
{
    static readonly string[] TempMarkers =
    {
        "/tmp/", "/var/tmp/", "/dev/shm/", "/private/tmp/", "/private/var/tmp/", "/downloads/",
        "/appdata/local/temp/", "/users/public/",
    };

    public static List<RunningProgram> List()
    {
        var list = new List<RunningProgram>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (OperatingSystem.IsLinux())
            {
                foreach (var dir in Directory.EnumerateDirectories("/proc"))
                {
                    if (!int.TryParse(System.IO.Path.GetFileName(dir), out var pid)) continue;
                    string? target;
                    try { target = new FileInfo(dir + "/exe").LinkTarget; } catch { continue; }   // other users' processes are not readable
                    if (string.IsNullOrEmpty(target)) continue;                                      // kernel threads
                    const string del = " (deleted)";
                    bool deleted = target.EndsWith(del, StringComparison.Ordinal);
                    var path = deleted ? target[..^del.Length] : target;
                    string comm = "";
                    try { comm = File.ReadAllText(dir + "/comm").Trim(); } catch { }
                    if (!seen.Add(path + (deleted ? "#deleted" : ""))) continue;
                    list.Add(new RunningProgram(pid, comm.Length > 0 ? comm : System.IO.Path.GetFileName(path), path, deleted ? "deleted" : Check(path)));
                }
            }
            else
            {
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        var f = p.MainModule?.FileName;
                        if (string.IsNullOrEmpty(f) || !seen.Add(f)) continue;
                        list.Add(new RunningProgram(p.Id, p.ProcessName, f, Check(f)));
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
        }
        catch { }
        return list;
    }

    static string? Check(string path)
    {
        var n = path.Replace('\\', '/').ToLowerInvariant();
        foreach (var m in TempMarkers) if (n.Contains(m)) return "temp";
        return null;
    }
}
