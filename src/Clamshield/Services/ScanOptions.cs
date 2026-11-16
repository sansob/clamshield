namespace Clamshield.Services;

/// <summary>Scanner tuning shared by clamscan, clamd and the file enumerator.</summary>
public static class ScanOptions
{
    public static volatile bool ScanArchives = true;
    public static volatile int MaxFileSizeMb = 100;
    public static volatile bool SkipMedia;
    public static volatile bool Strict;            // also flag adware, risky Office macros and encrypted archives
    public static volatile bool ExtraSignatures;   // community signature lists downloaded next to the official database

    static readonly HashSet<string> Media = new(StringComparer.OrdinalIgnoreCase)
    { ".mp3", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flac", ".wav", ".ogg", ".m4a", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".bmp" };

    public static bool IsSkipped(string file) => SkipMedia && Media.Contains(Path.GetExtension(file));

    /// <summary>Extra clamscan arguments.</summary>
    public static IEnumerable<string> ClamscanArgs()
    {
        yield return "--scan-archive=" + (ScanArchives ? "yes" : "no");
        yield return $"--max-filesize={MaxFileSizeMb}M";
        yield return $"--max-scansize={Math.Max(MaxFileSizeMb * 4, 100)}M";
        if (Strict) { yield return "--detect-pua=yes"; yield return "--alert-macros=yes"; yield return "--alert-encrypted=yes"; }
    }

    /// <summary>Community signature lists (checked: reachable, valid, small). Official ClamAV data is unaffected.</summary>
    public static readonly string[] ExtraSignatureFiles = { "badmacro.ndb", "rogue.hdb", "malwarehash.hsb", "hackingteam.hsb", "winnow_malware_links.ndb" };
    public const string ExtraSignatureBase = "https://ftp.swin.edu.au/sanesecurity/";
}
