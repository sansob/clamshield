using System.Text.Json;
using Clamshield.Models;

namespace Clamshield.Services;

public static class QuarantineService
{
    static readonly string Dir = Path.Combine(AppData.Dir, "quarantine");
    static string Index => Path.Combine(Dir, "index.json");

    public static string StoredPath(QuarantineItem q) => Path.Combine(Dir, q.Id + ".bin");

    /// <summary>Deletes quarantined files older than the given number of days. Returns how many were removed.</summary>
    public static int PurgeOlderThan(int days)
    {
        if (days <= 0) return 0;
        var old = Load().Where(q => (DateTime.Now - q.Date).TotalDays > days).ToList();
        foreach (var q in old) Delete(q);
        return old.Count;
    }

    public static List<QuarantineItem> Load()
    {
        try { return File.Exists(Index) ? JsonSerializer.Deserialize(File.ReadAllText(Index), AppJson.Info<List<QuarantineItem>>()) ?? new() : new(); }
        catch { return new(); }
    }

    static void Save(List<QuarantineItem> items)
    {
        AppData.EnsureDir(Dir); AppData.Secure(Dir, true);
        File.WriteAllText(Index, JsonSerializer.Serialize(items, AppJson.Info<List<QuarantineItem>>()));
        AppData.Secure(Index);
    }

    public static string? Add(ThreatItem t)
    {
        try
        {
            AppData.EnsureDir(Dir); AppData.Secure(Dir, true);
            var id = Guid.NewGuid().ToString("N");
            int mode = 0;
            if (!OperatingSystem.IsWindows()) { try { mode = (int)File.GetUnixFileMode(t.Path); } catch { } }
            var stored = Path.Combine(Dir, id + ".bin");
            File.Move(t.Path, stored);
            AppData.Secure(stored);   // owner read/write only: a quarantined program can no longer be run
            var items = Load();
            items.Add(new QuarantineItem { Id = id, OriginalPath = t.Path, Virus = t.Virus, Date = DateTime.Now, Mode = mode });
            Save(items);
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    public static string? Restore(QuarantineItem q)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(q.OriginalPath)!);
            File.Move(Path.Combine(Dir, q.Id + ".bin"), q.OriginalPath);
            if (!OperatingSystem.IsWindows() && q.Mode > 0) { try { File.SetUnixFileMode(q.OriginalPath, (UnixFileMode)q.Mode); } catch { } }
            Remove(q);
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    public static void Delete(QuarantineItem q)
    {
        try { File.Delete(Path.Combine(Dir, q.Id + ".bin")); } catch { }
        Remove(q);
    }

    static void Remove(QuarantineItem q) => Save(Load().Where(x => x.Id != q.Id).ToList());
}
