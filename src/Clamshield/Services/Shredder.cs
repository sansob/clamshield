using System.Security.Cryptography;

namespace Clamshield.Services;

public sealed record ShredResult(int Shredded, int Failed, long Bytes, string[] Errors);

public static class Shredder
{
    const int Chunk = 1024 * 1024;

    public static async Task<ShredResult> ShredAsync(IEnumerable<string> paths,
        IProgress<(int done, int total, string current)>? progress, CancellationToken ct, int passes = 3)
    {
        var errors = new List<string>();
        int shredded = 0, failed = 0; long bytes = 0;
        var files = new List<string>();
        var dirs = new List<string>();

        foreach (var raw in paths)
        {
            try
            {
                var p = Path.GetFullPath(raw);
                if (IsProtectedPath(p)) { failed++; errors.Add($"{raw}: refusing to shred protected path"); continue; }
                var fi = new FileInfo(p);
                if (fi.Exists || (fi.LinkTarget != null && !Directory.Exists(p))) { files.Add(p); continue; }
                var di = new DirectoryInfo(p);
                if (!di.Exists) { failed++; errors.Add($"{raw}: not found"); continue; }
                if (IsLink(di)) { files.Add(p); continue; } // link itself only
                Collect(di, files, dirs);
                dirs.Add(p);
            }
            catch (Exception ex) { failed++; errors.Add($"{raw}: {ex.Message}"); }
        }

        int total = files.Count, done = 0;
        foreach (var f in files)
        {
            if (ct.IsCancellationRequested) break;
            progress?.Report((done, total, f));
            try
            {
                bytes += await ShredFileAsync(f, passes, ct).ConfigureAwait(false);
                shredded++;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { failed++; errors.Add($"{f}: {ex.Message}"); }
            done++;
        }
        progress?.Report((done, total, ""));

        if (!ct.IsCancellationRequested)
            foreach (var d in dirs.OrderByDescending(x => x.Length))
            {
                try
                {
                    if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
                    else if (Directory.Exists(d)) { failed++; errors.Add($"{d}: directory not empty, left in place"); }
                }
                catch (Exception ex) { failed++; errors.Add($"{d}: {ex.Message}"); }
            }

        return new ShredResult(shredded, failed, bytes, errors.ToArray());
    }

    static bool IsLink(FileSystemInfo i) =>
        i.LinkTarget != null || i.Attributes.HasFlag(FileAttributes.ReparsePoint);

    static void Collect(DirectoryInfo d, List<string> files, List<string> dirs)
    {
        foreach (var e in d.EnumerateFileSystemInfos())
        {
            if (IsLink(e)) { files.Add(e.FullName); continue; }
            if (e is DirectoryInfo sub) { Collect(sub, files, dirs); dirs.Add(sub.FullName); }
            else files.Add(e.FullName);
        }
    }

    static async Task<long> ShredFileAsync(string path, int passes, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (IsLink(info))
        {
            // delete the link only, never touch the target
            if (Directory.Exists(path)) Directory.Delete(path); else File.Delete(path);
            return 0;
        }
        File.SetAttributes(path, FileAttributes.Normal);
        long len = info.Length;
        if (passes < 1) passes = 1;
        var buf = new byte[Chunk];
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            for (int pass = 0; pass < passes; pass++)
            {
                fs.Position = 0;
                long left = len;
                bool last = pass == passes - 1;
                while (left > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = (int)Math.Min(Chunk, left);
                    if (last) Array.Clear(buf, 0, n); else RandomNumberGenerator.Fill(buf.AsSpan(0, n));
                    await fs.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    left -= n;
                }
                fs.Flush(true);
            }
            fs.SetLength(0);
            fs.Flush(true);
        }
        var dir = Path.GetDirectoryName(path)!;
        var target = path;
        try
        {
            var np = Path.Combine(dir, Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant());
            File.Move(path, np);
            target = np;
        }
        catch { /* rename is best-effort */ }
        File.Delete(target);
        return len;
    }

    public static bool IsProtectedPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var trimmed = full.Length > 1 ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(trimmed)) return true;
            if (root != null && Eq(trimmed, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))) return true;
            if (root != null && Eq(full, root)) return true;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home) && Eq(trimmed, home.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))) return true;

            var prot = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                prot.Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                prot.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
                prot.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
                prot.Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
            }
            else
            {
                prot.AddRange(new[] { "/bin", "/sbin", "/usr", "/etc", "/boot", "/dev", "/proc", "/sys", "/lib", "/lib64",
                    "/var", "/opt", "/root", "/home", "/Users", "/System", "/Library", "/Applications", "/private" });
            }
            // the protected folder itself, or anything under it (system dirs), except under the user's home
            foreach (var p in prot.Where(x => !string.IsNullOrEmpty(x)))
            {
                var pp = p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (Eq(trimmed, pp)) return true;
                bool under = trimmed.StartsWith(pp + Path.DirectorySeparatorChar, Cmp);
                if (!under) continue;
                // /home, /Users, /var etc. hold user data legitimately: only block direct children for those
                if (!OperatingSystem.IsWindows() && pp is "/home" or "/Users" or "/root" or "/var" or "/opt" or "/Library" or "/Applications" or "/private")
                {
                    if (!string.IsNullOrEmpty(home) && trimmed.StartsWith(home.TrimEnd('/') + "/", Cmp)) continue;
                    if (pp is "/home" or "/Users") { if (trimmed.IndexOf(Path.DirectorySeparatorChar, pp.Length + 1) < 0) return true; continue; }
                    if (pp is "/root") return true;
                    continue;
                }
                return true;
            }
            return false;
        }
        catch { return true; }
    }

    static StringComparison Cmp => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    static bool Eq(string a, string b) => string.Equals(a, b, Cmp);
}
