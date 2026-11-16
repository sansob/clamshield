namespace Clamshield.Services;

public sealed record CleanupCategory(string Id, string Name, string Description, long Bytes, int Files);

public sealed record CleanupResult(long FreedBytes, int Deleted, int Failed);

/// <summary>
/// Conservative disk clean-up: only temp files older than 2 days, thumbnail caches and the user's trash.
/// Never follows symlinks/reparse points, never leaves the allowed roots, never touches AppData.Dir.
/// </summary>
public static class CleanupService
{
    private static readonly TimeSpan TempAge = TimeSpan.FromDays(2);

    private sealed record Root(string Path, bool Recurse, bool RemoveEmptyDirs, bool AgeFilter, string? NamePattern, bool DeleteLinks);

    private sealed record Entry(string Path, bool IsDir, bool IsLink, long Length, bool IsOld);

    private static readonly (string Id, string Name, string Description)[] Meta =
    {
        ("temp", "Temporary files", "Files in the temp folder not modified or used for over 2 days"),
        ("thumbnails", "Thumbnail cache", "Cached image previews; they are rebuilt when needed"),
        ("trash", "Trash", "Files already moved to the trash"),
    };

    public static Task<List<CleanupCategory>> AnalyzeAsync(CancellationToken ct) => Task.Run(() =>
    {
        var list = new List<CleanupCategory>();
        foreach (var (id, name, desc) in Meta)
        {
            ct.ThrowIfCancellationRequested();
            long bytes = 0; int files = 0;
            foreach (var root in RootsFor(id))
                foreach (var e in Collect(root, ct))
                    if (IsCandidateFile(e, root)) { bytes += e.Length; files++; }
            if (files > 0) list.Add(new CleanupCategory(id, name, desc, bytes, files));
        }
        return list;
    }, ct);

    public static Task<CleanupResult> CleanAsync(IEnumerable<string> categoryIds, IProgress<string>? progress, CancellationToken ct) => Task.Run(() =>
    {
        long freed = 0; int deleted = 0, failed = 0;
        var ids = new HashSet<string>(categoryIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name, _) in Meta)
        {
            if (!ids.Contains(id)) continue;
            ct.ThrowIfCancellationRequested();
            progress?.Report(id switch
            {
                "temp" => "Cleaning temporary files…",
                "thumbnails" => "Cleaning thumbnail cache…",
                _ => "Emptying trash…",
            });
            foreach (var root in RootsFor(id))
            {
                var entries = Collect(root, ct);
                foreach (var e in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!IsCandidateFile(e, root)) continue;
                    if (!IsSafe(e.Path, root.Path)) { failed++; continue; }
                    try
                    {
                        if (root.AgeFilter && !StillOld(e.Path)) continue; // changed meanwhile: leave alone
                        if (OperatingSystem.IsWindows() && e.IsLink && Directory.Exists(e.Path)) Directory.Delete(e.Path, false);
                        else File.Delete(e.Path);
                        freed += e.Length; deleted++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { failed++; }
                }
                if (!root.RemoveEmptyDirs) continue;
                // deepest first; non-recursive delete only succeeds on empty folders
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    ct.ThrowIfCancellationRequested();
                    var e = entries[i];
                    if (!e.IsDir || e.IsLink) continue;
                    if (root.AgeFilter && !e.IsOld) continue;
                    if (!IsSafe(e.Path, root.Path)) continue;
                    try
                    {
                        if (Directory.EnumerateFileSystemEntries(e.Path).Any()) continue;
                        Directory.Delete(e.Path, false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { /* leave it */ }
                }
            }
        }
        return new CleanupResult(freed, deleted, failed);
    }, ct);

    // ---- roots ----

    private static IEnumerable<Root> RootsFor(string id)
    {
        var list = new List<Root>();
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            switch (id)
            {
                case "temp":
                    list.Add(new Root(Path.GetTempPath(), true, true, true, null, false));
                    break;
                case "thumbnails":
                    if (OperatingSystem.IsLinux())
                        list.Add(new Root(Path.Combine(XdgCache(home), "thumbnails"), true, true, false, null, true));
                    else if (OperatingSystem.IsWindows())
                    {
                        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        if (!string.IsNullOrEmpty(local))
                            list.Add(new Root(Path.Combine(local, "Microsoft", "Windows", "Explorer"), false, false, false, "thumbcache_*.db", false));
                    }
                    break;
                case "trash":
                    if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(home))
                    {
                        string trash = Path.Combine(XdgData(home), "Trash");
                        list.Add(new Root(Path.Combine(trash, "files"), true, true, false, null, true));
                        list.Add(new Root(Path.Combine(trash, "info"), true, true, false, null, true));
                    }
                    else if (OperatingSystem.IsMacOS() && !string.IsNullOrEmpty(home))
                        list.Add(new Root(Path.Combine(home, ".Trash"), true, true, false, null, true));
                    break;
            }
        }
        catch { }

        foreach (var r in list)
        {
            string? full = Normalize(r.Path);
            if (full == null || full.Length < 2 || IsFsRoot(full)) continue; // never a filesystem root
            if (!Directory.Exists(full)) continue;
            if (IsLinkPath(full)) continue; // root itself must be a real folder
            if (IsUnderOrEqual(full, Normalize(AppDataDir()))) continue; // root inside our own data
            yield return r with { Path = full };
        }
    }

    private static string XdgData(string home)
    {
        string? x = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return !string.IsNullOrWhiteSpace(x) && Path.IsPathRooted(x) ? x : Path.Combine(home, ".local", "share");
    }

    private static string XdgCache(string home)
    {
        string? x = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        return !string.IsNullOrWhiteSpace(x) && Path.IsPathRooted(x) ? x : Path.Combine(home, ".cache");
    }

    private static string AppDataDir()
    {
        try { return AppData.Dir; } catch { return ""; }
    }

    // ---- path safety ----

    private static StringComparison Cmp => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static string? Normalize(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)); } catch { return null; }
    }

    private static bool IsFsRoot(string full) => Path.GetPathRoot(full) is { } r && PathsEqual(Path.TrimEndingDirectorySeparator(r), full);

    private static bool PathsEqual(string? a, string? b) => a != null && b != null && string.Equals(a, b, Cmp);

    private static bool IsUnderOrEqual(string? path, string? parent)
    {
        if (path == null || parent == null) return false;
        if (string.Equals(path, parent, Cmp)) return true;
        return path.StartsWith(parent + Path.DirectorySeparatorChar, Cmp);
    }

    private static bool IsLinkPath(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.LinkTarget != null || (fi.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch { return true; }
    }

    /// <summary>Candidate must be strictly inside root, not the root, and not under AppData.Dir.</summary>
    private static bool IsSafe(string path, string root)
    {
        string? full = Normalize(path);
        if (full == null || PathsEqual(full, root)) return false;
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, Cmp)) return false;
        string app = Normalize(AppDataDir()) ?? "";
        if (app.Length > 0 && IsUnderOrEqual(full, app)) return false;
        return true;
    }

    // ---- enumeration (never follows links) ----

    private static bool IsCandidateFile(Entry e, Root root)
    {
        if (e.IsDir) return false;
        if (e.IsLink) return root.DeleteLinks;
        if (root.AgeFilter && !e.IsOld) return false;
        return true;
    }

    private static bool StillOld(string path)
    {
        var fi = new FileInfo(path);
        var limit = DateTime.UtcNow - TempAge;
        return fi.LastWriteTimeUtc < limit && fi.LastAccessTimeUtc < limit;
    }

    private static List<Entry> Collect(Root root, CancellationToken ct)
    {
        var result = new List<Entry>();
        string app = Normalize(AppDataDir()) ?? "";
        var limit = DateTime.UtcNow - TempAge;
        var stack = new Stack<string>();
        stack.Push(root.Path);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            string dir = stack.Pop();
            IEnumerable<FileSystemInfo> items;
            try { items = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToList(); }
            catch { continue; }
            foreach (var fsi in items)
            {
                try
                {
                    string full = fsi.FullName;
                    if (app.Length > 0 && IsUnderOrEqual(full, app)) continue;
                    if (app.Length > 0 && IsUnderOrEqual(app, full)) continue; // ancestor of our data
                    bool link = fsi.LinkTarget != null || (fsi.Attributes & FileAttributes.ReparsePoint) != 0;
                    bool old = fsi.LastWriteTimeUtc < limit && fsi.LastAccessTimeUtc < limit;
                    if (link)
                    {
                        // Only the link itself is ever considered; the target is never touched.
                        result.Add(new Entry(full, false, true, 0, old));
                        continue;
                    }
                    if (fsi is DirectoryInfo)
                    {
                        if (!root.Recurse) continue;
                        result.Add(new Entry(full, true, false, 0, old));
                        stack.Push(full);
                    }
                    else if (fsi is FileInfo f)
                    {
                        if (root.NamePattern != null && !MatchesPattern(f.Name, root.NamePattern)) continue;
                        if (LooksSpecial(f)) continue;
                        result.Add(new Entry(full, false, false, f.Length, old));
                    }
                }
                catch { /* unreadable entry: skip */ }
            }
        }
        return result;
    }

    private static bool LooksSpecial(FileInfo f)
    {
        if ((f.Attributes & (FileAttributes.Device | FileAttributes.System)) != 0 && OperatingSystem.IsWindows()) return true;
        if (!OperatingSystem.IsWindows())
        {
            // Sockets/FIFOs are not distinguishable via FileInfo; skip the usual names.
            string n = f.Name;
            if (n.EndsWith(".sock", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".socket", StringComparison.OrdinalIgnoreCase)
                || n.EndsWith(".pipe", StringComparison.OrdinalIgnoreCase) || n.StartsWith("S.", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool MatchesPattern(string name, string pattern)
    {
        // only "prefix*suffix" patterns are used
        int star = pattern.IndexOf('*');
        if (star < 0) return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        string pre = pattern[..star], suf = pattern[(star + 1)..];
        return name.Length >= pre.Length + suf.Length
            && name.StartsWith(pre, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(suf, StringComparison.OrdinalIgnoreCase);
    }
}
