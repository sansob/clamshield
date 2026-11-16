using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Clamshield.Models;

namespace Clamshield.Services;

/// <summary>
/// Watches folders and scans new or changed files with ClamAV, designed to stay light on the system:
/// one worker, files grouped into small batches, low process priority and a pause between batches.
/// </summary>
public sealed class RealTimeService : IDisposable
{
    static readonly string[] SkipExt = { ".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload", ".swp" };
    const int MaxQueue = 2000, MaxBatch = 64;

    readonly List<FileSystemWatcher> _watchers = new();
    readonly ConcurrentDictionary<string, byte> _queued = new();
    readonly ConcurrentDictionary<string, (long len, DateTime time)> _recent = new();
    Channel<string>? _queue;
    CancellationTokenSource? _cts;

    public event Action<ThreatItem>? ThreatFound;
    public event Action<string>? FileChecked;          // every file that was scanned
    public event Action<string>? FileClean;            // scanned and clean
    public event Action<string, string>? FileSkipped;  // path, reason
    public event Action<string[]>? BatchStarted;       // files about to be scanned
    public event Action? BatchDone;

    public bool IsRunning => _cts != null;
    public int Pending => _queued.Count;

    public static string[] DefaultFolders() =>
        new[] { "Downloads", "Desktop" }
            .Select(d => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), d))
            .Where(Directory.Exists).ToArray();

    public void Start(IEnumerable<string> folders)
    {
        Stop();
        _cts = new CancellationTokenSource();
        _queue = Channel.CreateUnbounded<string>();
        foreach (var f in folders)
        {
            try
            {
                var w = new FileSystemWatcher(f)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                w.Created += (_, e) => Enqueue(e.FullPath);
                w.Changed += (_, e) => Enqueue(e.FullPath);
                w.Renamed += (_, e) => Enqueue(e.FullPath);
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch { }
        }
        var ct = _cts.Token; var q = _queue;
        _ = Task.Run(() => Worker(q, ct));
    }

    public void Stop()
    {
        foreach (var w in _watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
        _watchers.Clear();
        _cts?.Cancel(); _cts = null;
        _queue?.Writer.TryComplete(); _queue = null;
        _queued.Clear();
    }

    void Enqueue(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (SkipExt.Contains(ext) || path.StartsWith(AppData.Dir) || Exclusions.IsExcluded(path) || ScanOptions.IsSkipped(path)) return;
        if (_queued.Count >= MaxQueue) { FileSkipped?.Invoke(path, "busy"); return; }
        if (_queued.TryAdd(path, 0)) _queue?.Writer.TryWrite(path);
    }

    async Task Worker(Channel<string> q, CancellationToken ct)
    {
        try
        {
            while (await q.Reader.WaitToReadAsync(ct))
            {
                if (!q.Reader.TryRead(out var first)) continue;
                var batch = new List<string> { first };

                // Gather neighbours so one scanner call handles many files. With the daemon ready this is quick;
                // without it every call reloads the database, so wait longer and batch more.
                var deadline = DateTime.UtcNow.AddMilliseconds(ClamdService.Ready ? 400 : 3000);
                while (batch.Count < MaxBatch)
                {
                    if (q.Reader.TryRead(out var more)) { batch.Add(more); continue; }
                    if (DateTime.UtcNow >= deadline) break;
                    await Task.Delay(50, ct);
                }
                foreach (var b in batch) _queued.TryRemove(b, out _);

                try { await ScanBatch(batch, ct); }
                catch (OperationCanceledException) { throw; }
                catch { }

                // breathe between batches so a flood of files never pins the CPU
                await Task.Delay(_queued.Count > 20 ? 50 : 200, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    async Task<bool> WaitStable(string path, CancellationToken ct)
    {
        long last = -1;
        for (int i = 0; i < 60; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return false;
            long len;
            try { len = new FileInfo(path).Length; using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); }
            catch { await Task.Delay(1000, ct); continue; }
            if (len == last) return true;
            last = len;
            await Task.Delay(800, ct);
        }
        return false;
    }

    async Task ScanBatch(List<string> paths, CancellationToken ct)
    {
        // wait (in parallel) until each file has finished being written
        var ready = await Task.WhenAll(paths.Select(async p => (p, ok: await WaitStable(p, ct))));
        var files = new List<string>();
        foreach (var (p, ok) in ready)
        {
            if (!ok) continue;
            var info = new FileInfo(p);
            if (!info.Exists) continue;
            if (info.Length > ScanOptions.MaxFileSizeMb * 1024L * 1024L) { FileSkipped?.Invoke(p, "too large"); continue; }
            if (_recent.TryGetValue(p, out var r) && r.len == info.Length && (DateTime.Now - r.time).TotalSeconds < 5) continue;
            _recent[p] = (info.Length, DateTime.Now);
            files.Add(p);
        }
        if (files.Count == 0) return;

        BatchStarted?.Invoke(files.ToArray());
        try
        {
            var list = Path.Combine(Path.GetTempPath(), $"clamshield-rt-{Guid.NewGuid():N}.txt");
            await File.WriteAllLinesAsync(list, files, ct);
            try
            {
                var results = await RunScanner(list, ClamdService.Ready, ct);
                if (results == null && ClamdService.Ready) results = await RunScanner(list, false, ct);   // daemon failed: fall back
                Report(files, results ?? new());
            }
            finally { try { File.Delete(list); } catch { } }
        }
        finally { BatchDone?.Invoke(); }
    }

    static async Task<Dictionary<string, string>?> RunScanner(string listFile, bool fast, CancellationToken ct)
    {
        var exe = fast ? ClamAvLocator.Find("clamdscan") : ClamAvLocator.Find("clamscan");
        if (exe == null) return null;
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("--no-summary");
        if (fast) psi.ArgumentList.Add("--config-file=" + ClamdService.Conf);
        else { foreach (var a in ClamAvLocator.DbArgs()) psi.ArgumentList.Add(a); foreach (var a in ScanOptions.ClamscanArgs()) psi.ArgumentList.Add(a); }
        psi.ArgumentList.Add("--file-list=" + listFile);

        using var p = Process.Start(psi)!;
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        var err = p.StandardError.ReadToEndAsync(ct);
        var results = new Dictionary<string, string>();
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync(ct)) != null)
        {
            var i = line.LastIndexOf(": ", StringComparison.Ordinal);
            if (i > 0) results[line[..i]] = line[(i + 2)..].Trim();
        }
        await p.WaitForExitAsync(ct);
        await err;
        return results.Count == 0 && p.ExitCode == 2 ? null : results;
    }

    void Report(List<string> files, Dictionary<string, string> results)
    {
        foreach (var f in files)
        {
            results.TryGetValue(f, out var res);
            if (res != null && res.EndsWith(" FOUND"))
            {
                if (TrustedFiles.IsTrusted(f)) { FileChecked?.Invoke(f); FileClean?.Invoke(f); continue; }
                FileChecked?.Invoke(f);
                ThreatFound?.Invoke(new ThreatItem(f, res[..^6]));
            }
            else if (res != null && res.Contains("ERROR")) FileSkipped?.Invoke(f, "unreadable");
            else { FileChecked?.Invoke(f); FileClean?.Invoke(f); }
        }
    }

    public void Dispose() => Stop();
}
