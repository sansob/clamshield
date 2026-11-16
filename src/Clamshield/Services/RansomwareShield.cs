using System.IO;

namespace Clamshield.Services;

/// <summary>
/// Watches folders and raises <see cref="Alert"/> on ransomware-like bursts of file activity
/// (many distinct files touched in a short window, or many files renamed to one uncommon extension).
/// </summary>
public sealed class RansomwareShield : IDisposable
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);
    private const int RenameExtThreshold = 15;
    private const int MaxTracked = 20000;

    private static readonly HashSet<string> CommonExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "", ".txt", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".jpg", ".jpeg", ".png",
        ".gif", ".bmp", ".mp3", ".mp4", ".mkv", ".avi", ".mov", ".zip", ".rar", ".7z", ".gz", ".tar",
        ".cs", ".js", ".ts", ".json", ".xml", ".html", ".htm", ".css", ".md", ".csv", ".log", ".bak",
        ".py", ".java", ".c", ".cpp", ".h", ".odt", ".ods", ".svg", ".webp", ".wav", ".flac", ".exe", ".dll"
    };

    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Dictionary<string, DateTime> _touched = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Ext, DateTime At)> _renamed = new(StringComparer.Ordinal);
    private string[] _folders = Array.Empty<string>();
    private int _threshold = 40;
    private TimeSpan _window = TimeSpan.FromSeconds(10);
    private DateTime _lastAlert = DateTime.MinValue;
    private bool _disposed;

    public event Action<string, string[]>? Alert;

    public void Start(IEnumerable<string> folders, int threshold = 40, TimeSpan? window = null)
    {
        Stop();
        lock (_gate)
        {
            if (_disposed) return;
            _threshold = Math.Max(1, threshold);
            _window = window is { } w && w > TimeSpan.Zero ? w : TimeSpan.FromSeconds(10);
            _lastAlert = DateTime.MinValue;
            var list = new List<string>();
            foreach (var f in folders ?? Array.Empty<string>())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(f) || !Directory.Exists(f)) continue;
                    var fw = new FileSystemWatcher(f)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        InternalBufferSize = 64 * 1024
                    };
                    fw.Changed += (_, e) => OnEvent(e.FullPath, null, f);
                    fw.Created += (_, e) => OnEvent(e.FullPath, null, f);
                    fw.Deleted += (_, e) => OnEvent(e.FullPath, null, f);
                    fw.Renamed += (_, e) => OnEvent(e.FullPath, e.OldFullPath, f);
                    fw.Error += (_, _) => { };
                    fw.EnableRaisingEvents = true;
                    _watchers.Add(fw);
                    list.Add(f);
                }
                catch { /* skip unwatchable folder */ }
            }
            _folders = list.ToArray();
        }
    }

    public void Stop()
    {
        List<FileSystemWatcher> old;
        lock (_gate)
        {
            old = new List<FileSystemWatcher>(_watchers);
            _watchers.Clear();
            _touched.Clear();
            _renamed.Clear();
            _folders = Array.Empty<string>();
        }
        foreach (var w in old)
        {
            try { w.EnableRaisingEvents = false; w.Dispose(); } catch { }
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Stop();
    }

    private static readonly string[] DevDirs =
    {
        "/node_modules/", "/.git/", "/bin/", "/obj/", "/target/", "/.venv/", "/venv/", "/__pycache__/",
        "/build/", "/dist/", "/.cache/", "/.idea/", "/.vs/", "/.gradle/", "/.nuget/",
    };

    private static bool Ignored(string path)
    {
        try
        {
            var dir = AppData.Dir;
            if (!string.IsNullOrEmpty(dir) && path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) return true;

            var name = Path.GetFileName(path);
            if (name.StartsWith("~$", StringComparison.Ordinal)) return true;
            var ext = Path.GetExtension(name);
            if (ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".crdownload", StringComparison.OrdinalIgnoreCase)) return true;

            var norm = path.Replace('\\', '/');
            // build output / caches written in bulk by developer tools are not ransomware
            foreach (var d in DevDirs)
                if (norm.Contains(d, StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch { }
        return false;
    }

    private void OnEvent(string path, string? oldPath, string root)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || Ignored(path)) return;
            var now = DateTime.UtcNow;
            string? summary = null;
            string[]? samples = null;

            lock (_gate)
            {
                if (_watchers.Count == 0) return;
                _touched[path] = now;

                if (oldPath != null)
                {
                    var newExt = Path.GetExtension(path);
                    var oldExt = Path.GetExtension(oldPath);
                    if (!newExt.Equals(oldExt, StringComparison.OrdinalIgnoreCase) &&
                        !CommonExtensions.Contains(newExt))
                        _renamed[path] = (newExt.ToLowerInvariant(), now);
                }

                Prune(now);

                if (now - _lastAlert < Cooldown) return;

                var distinct = _touched.Count;
                string? badExt = null;
                int badCount = 0;
                if (_renamed.Count >= RenameExtThreshold)
                {
                    var top = _renamed.Values.GroupBy(v => v.Ext).OrderByDescending(g => g.Count()).First();
                    if (top.Count() >= RenameExtThreshold) { badExt = top.Key; badCount = top.Count(); }
                }

                var where = _folders.Length == 1 ? _folders[0] : $"{_folders.Length} watched folders";
                if (badExt != null)
                {
                    summary = $"{badCount} files were renamed to the unusual extension \"{badExt}\" within {_window.TotalSeconds:0} seconds in {where}.";
                    samples = _renamed.Where(kv => kv.Value.Ext == badExt).Select(kv => kv.Key).Take(5).ToArray();
                }
                else if (distinct >= _threshold)
                {
                    summary = $"{distinct} files were modified, created, deleted or renamed within {_window.TotalSeconds:0} seconds in {where}.";
                    samples = _touched.Keys.Take(5).ToArray();
                }
                else return;

                _lastAlert = now;
                _touched.Clear();
                _renamed.Clear();
            }

            try { Alert?.Invoke(summary, samples!); } catch { }
        }
        catch { }
    }

    // Caller holds _gate.
    private void Prune(DateTime now)
    {
        var cutoff = now - _window;
        if (_touched.Count > 0)
        {
            List<string>? dead = null;
            foreach (var kv in _touched)
                if (kv.Value < cutoff) (dead ??= new()).Add(kv.Key);
            if (dead != null) foreach (var k in dead) _touched.Remove(k);
        }
        if (_renamed.Count > 0)
        {
            List<string>? dead = null;
            foreach (var kv in _renamed)
                if (kv.Value.At < cutoff) (dead ??= new()).Add(kv.Key);
            if (dead != null) foreach (var k in dead) _renamed.Remove(k);
        }
        if (_touched.Count > MaxTracked) _touched.Clear();
        if (_renamed.Count > MaxTracked) _renamed.Clear();
    }
}
