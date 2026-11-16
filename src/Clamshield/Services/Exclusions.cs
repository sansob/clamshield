using System.Runtime.InteropServices;

namespace Clamshield.Services;

/// <summary>Paths that scans and real-time protection must skip.</summary>
public static class Exclusions
{
    static volatile string[] _list = Array.Empty<string>();
    static readonly StringComparison Cmp =
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static void Set(IEnumerable<string> items) =>
        _list = items.Select(i => i.TrimEnd(Path.DirectorySeparatorChar, '/')).Where(i => i.Length > 0).ToArray();

    public static bool IsExcluded(string path)
    {
        var list = _list;
        if (list.Length == 0) return false;
        foreach (var e in list)
            if (path.Equals(e, Cmp) || path.StartsWith(e + Path.DirectorySeparatorChar, Cmp) || path.StartsWith(e + "/", Cmp))
                return true;
        return false;
    }
}
