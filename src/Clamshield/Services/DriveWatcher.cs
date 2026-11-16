using System.Runtime.InteropServices;

namespace Clamshield.Services;

/// <summary>Polls for USB / external drives being plugged in (works on Windows, Linux and macOS).</summary>
public sealed class DriveWatcher : IDisposable
{
    readonly System.Threading.Timer _timer;
    HashSet<string>? _known;
    public event Action<string, string>? DriveArrived;   // root path, display name

    public DriveWatcher() => _timer = new System.Threading.Timer(_ => Poll(), null, 2000, 4000);

    static bool IsRemovable(DriveInfo d)
    {
        try
        {
            if (!d.IsReady) return false;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return d.DriveType == DriveType.Removable;
            var r = d.RootDirectory.FullName;
            if (r == "/") return false;
            return r.StartsWith("/media/") || r.StartsWith("/run/media/") || r.StartsWith("/Volumes/") || r.StartsWith("/mnt/");
        }
        catch { return false; }
    }

    void Poll()
    {
        try
        {
            var now = DriveInfo.GetDrives().Where(IsRemovable).ToDictionary(d => d.RootDirectory.FullName, d => d);
            if (_known == null) { _known = now.Keys.ToHashSet(); return; }   // ignore drives already connected at startup
            foreach (var (root, d) in now)
                if (_known.Add(root))
                {
                    string name; try { name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? root : d.VolumeLabel; } catch { name = root; }
                    DriveArrived?.Invoke(root, name);
                }
            _known.IntersectWith(now.Keys);
        }
        catch { }
    }

    public void Dispose() => _timer.Dispose();
}
