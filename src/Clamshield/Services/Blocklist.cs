using System.Security.Cryptography;
using System.Text;

namespace Clamshield.Services;

public sealed class BlockEntry
{
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public DateTime Added { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string FileName => System.IO.Path.GetFileName(Path);
    [System.Text.Json.Serialization.JsonIgnore] public string AddedText => Added.ToString("dd MMM yyyy, HH:mm");
}

/// <summary>
/// The user's own "never let this through" list. It is written as a ClamAV hash database (.hsb) into the
/// database folder, so every scan (clamscan and the clamd daemon) flags those files and any copy of them.
/// </summary>
public static class Blocklist
{
    public sealed class Store { public List<BlockEntry> Items { get; set; } = new(); }
    static readonly object Gate = new();
    public static string HsbPath => System.IO.Path.Combine(ClamAvLocator.DbDir, "clamshield-blocklist.hsb");

    public static List<BlockEntry> All() { lock (Gate) return AppData.Load<Store>("blocklist.json").Items.ToList(); }

    public static string? Add(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return "missing";
            string hash;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            lock (Gate)
            {
                var store = AppData.Load<Store>("blocklist.json");
                if (store.Items.Any(i => i.Sha256 == hash)) return null;
                var safe = new string(info.Name.Select(c => char.IsLetterOrDigit(c) && c < 128 || c is '.' or '_' or '-' ? c : '_').ToArray());
                if (safe.Length > 40) safe = safe[..40];
                store.Items.Add(new BlockEntry { Sha256 = hash, Size = info.Length, Name = "Clamshield.Blocked." + safe, Path = path, Added = DateTime.Now });
                AppData.Save("blocklist.json", store);
                Write(store);
            }
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    public static void Remove(string sha256)
    {
        lock (Gate)
        {
            var store = AppData.Load<Store>("blocklist.json");
            store.Items.RemoveAll(i => i.Sha256 == sha256);
            AppData.Save("blocklist.json", store);
            Write(store);
        }
    }

    /// <summary>Rewrites the database file (e.g. after the database folder was recreated).</summary>
    public static void Sync() { lock (Gate) Write(AppData.Load<Store>("blocklist.json")); }

    static void Write(Store store)
    {
        try
        {
            Directory.CreateDirectory(ClamAvLocator.DbDir);
            if (store.Items.Count == 0) { File.Delete(HsbPath); return; }
            var sb = new StringBuilder();
            foreach (var i in store.Items) sb.Append(i.Sha256).Append(':').Append(i.Size).Append(':').Append(i.Name).Append('\n');
            File.WriteAllText(HsbPath, sb.ToString());
        }
        catch { }
    }
}
