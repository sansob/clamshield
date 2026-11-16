using System.Security.Cryptography;

namespace Clamshield.Services;

public sealed record TrustedEntry(string Sha256, string Path, DateTime Added);

public static class TrustedFiles
{
    public sealed class StoredEntry
    {
        public string Sha256 { get; set; } = "";
        public string Path { get; set; } = "";
        public DateTime Added { get; set; }
    }
    public sealed class Store
    {
        public List<StoredEntry> Items { get; set; } = new();
    }

    const string FileName = "trusted.json";
    static readonly object Gate = new();
    static Store? _store;
    static readonly Dictionary<string, (long len, long ticks, string hash)> Cache = new();

    static Store S => _store ??= AppData.Load<Store>(FileName);

    public static string HashFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    public static bool IsTrusted(string path)
    {
        try
        {
            lock (Gate) { if (S.Items.Count == 0) return false; }
            var full = Path.GetFullPath(path);
            var fi = new FileInfo(full);
            if (!fi.Exists) return false;
            string hash;
            lock (Gate)
            {
                if (Cache.TryGetValue(full, out var c) && c.len == fi.Length && c.ticks == fi.LastWriteTimeUtc.Ticks)
                    return IsHashTrusted(c.hash);
            }
            hash = HashFile(full);
            lock (Gate)
            {
                Cache[full] = (fi.Length, fi.LastWriteTimeUtc.Ticks, hash);
                return IsHashTrusted(hash);
            }
        }
        catch { return false; }
    }

    static bool IsHashTrusted(string h) => S.Items.Any(e => e.Sha256 == h);

    public static void Add(string sha256, string originalPath)
    {
        sha256 = sha256.Trim().ToLowerInvariant();
        lock (Gate)
        {
            if (IsHashTrusted(sha256)) return;
            S.Items.Add(new StoredEntry { Sha256 = sha256, Path = originalPath, Added = DateTime.UtcNow });
            AppData.Save(FileName, S);
        }
    }

    public static void Remove(string sha256)
    {
        sha256 = sha256.Trim().ToLowerInvariant();
        lock (Gate)
        {
            if (S.Items.RemoveAll(e => e.Sha256 == sha256) > 0) AppData.Save(FileName, S);
        }
    }

    public static IReadOnlyList<TrustedEntry> All()
    {
        lock (Gate) return S.Items.Select(e => new TrustedEntry(e.Sha256, e.Path, e.Added)).ToList();
    }

    // test hook: drop in-memory state so the next call reloads from disk
    internal static void ResetForTests() { lock (Gate) { _store = null; Cache.Clear(); } }
}
