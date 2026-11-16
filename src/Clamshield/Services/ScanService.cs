using System.Diagnostics;
using System.Runtime.InteropServices;
using Clamshield.Models;

namespace Clamshield.Services;

public static class ScanService
{
    static readonly string[] UnixSkip = { "/proc", "/sys", "/dev", "/run", "/snap", "/var/lib/docker", "/mnt", "/media" };

    public static string[] QuickRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[] { "Downloads", "Desktop", "Documents" }
            .Select(d => Path.Combine(home, d)).Append(Path.GetTempPath()).Where(Directory.Exists).ToArray();
    }

    public static string[] FullRoots() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToArray()
            : new[] { "/" };

    static IEnumerable<string> Enumerate(IEnumerable<string> roots, CancellationToken ct)
    {
        var stack = new Stack<string>(roots.Reverse());
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            if (Exclusions.IsExcluded(dir)) continue;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && UnixSkip.Any(s => dir == s || dir.StartsWith(s + "/"))) continue;
            if (File.Exists(dir)) { yield return dir; continue; }
            string[] files = Array.Empty<string>(), subs = Array.Empty<string>();
            try
            {
                var di = new DirectoryInfo(dir);
                if (di.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue; // skip symlinks / junctions
                files = Directory.GetFiles(dir);
                subs = Directory.GetDirectories(dir);
            }
            catch { }
            foreach (var f in files) if (!Exclusions.IsExcluded(f) && !ScanOptions.IsSkipped(f)) yield return f;
            foreach (var s in subs) stack.Push(s);
        }
    }

    public static async Task RunAsync(IEnumerable<string> roots, ScanState state, Action<ThreatItem> onThreat, CancellationToken ct, bool lowPriority = false)
    {
        var exe = ClamAvLocator.Find("clamscan");
        if (exe == null) { state.Error = "ClamAV was not found. " + ClamAvLocator.InstallHint; return; }

        var list = Path.Combine(Path.GetTempPath(), $"clamshield-{Guid.NewGuid():N}.txt");
        try
        {
            // Step 1: collect files so we can show real progress.
            var files = await Task.Run(() =>
            {
                var all = Enumerate(roots, ct).ToList();
                return all;
            }, ct);
            state.Total = files.Count;
            state.Preparing = false;
            if (files.Count == 0) return;
            await File.WriteAllLinesAsync(list, files, ct);

            // Step 2: scan the list. The clamd daemon is used when it is ready (database already in memory);
            // otherwise, or if it fails, plain clamscan.
            var attempts = ClamdService.Ready ? new[] { true, false } : new[] { false };
            foreach (var fast in attempts)
            {
                var program = fast ? (ClamAvLocator.Find("clamdscan") ?? exe) : exe;
                var psi = new ProcessStartInfo(program)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                };
                psi.ArgumentList.Add("--no-summary");
                if (fast) { psi.ArgumentList.Add("--config-file=" + ClamdService.Conf); psi.ArgumentList.Add("--multiscan"); }
                else { foreach (var a in ClamAvLocator.DbArgs()) psi.ArgumentList.Add(a); foreach (var a in ScanOptions.ClamscanArgs()) psi.ArgumentList.Add(a); }
                psi.ArgumentList.Add("--file-list=" + list);

                using var proc = Process.Start(psi)!;
                if (lowPriority) { try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { } }
                using var reg = ct.Register(() => { try { proc.Kill(true); } catch { } });
                var errTask = proc.StandardError.ReadToEndAsync();

                string? line;
                while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
                {
                    var i = line.LastIndexOf(": ", StringComparison.Ordinal);
                    if (i < 0) continue;
                    var path = line[..i];
                    var result = line[(i + 2)..];
                    state.Current = path;
                    Interlocked.Increment(ref state.Scanned);
                    if (result.EndsWith(" FOUND") && !TrustedFiles.IsTrusted(path))
                    {
                        Interlocked.Increment(ref state.Infected);
                        onThreat(new ThreatItem(path, result[..^6]));
                    }
                }
                await proc.WaitForExitAsync(CancellationToken.None);
                var err = await errTask;
                if (ct.IsCancellationRequested) break;
                if (proc.ExitCode == 2 && state.Scanned == 0)
                {
                    if (fast) continue;   // daemon problem: retry with clamscan
                    state.Error = "ClamAV could not start scanning. " + FirstLine(err) + "\nTry updating the virus definitions first.";
                }
                break;
            }

        }
        finally { try { File.Delete(list); } catch { } }
    }

    static string FirstLine(string s) => s.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";

    public static async Task<string> UpdateAsync(Action<string> onLine, CancellationToken ct)
    {
        var exe = ClamAvLocator.Find("freshclam");
        if (exe == null) return "freshclam was not found. " + ClamAvLocator.InstallHint;
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("--show-progress");
        psi.ArgumentList.Add("--config-file=" + ClamAvLocator.EnsureFreshclamConfig());
        psi.ArgumentList.Add("--datadir=" + ClamAvLocator.DbDir);
        using var p = Process.Start(psi)!;
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        var err = Task.Run(async () => { string? l; while ((l = await p.StandardError.ReadLineAsync()) != null) onLine(l); });
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync()) != null) onLine(line);
        await err;
        await p.WaitForExitAsync(CancellationToken.None);
        return p.ExitCode == 0 ? "OK" : "exit:" + p.ExitCode;
    }
}
