namespace Clamshield.Services;

/// <summary>Finds existing folders where desktop mail clients keep messages/attachments.</summary>
public static class EmailFolders
{
    public static string[] Detect()
    {
        var found = new List<string>();
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localShare = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var thunderbirdRoots = new List<string>();
            if (!string.IsNullOrEmpty(home))
            {
                if (OperatingSystem.IsLinux())
                {
                    thunderbirdRoots.Add(Path.Combine(home, ".thunderbird"));
                    thunderbirdRoots.Add(Path.Combine(home, ".var", "app", "org.mozilla.Thunderbird", ".thunderbird"));
                    thunderbirdRoots.Add(Path.Combine(home, "snap", "thunderbird", "common", ".thunderbird"));
                }
                if (OperatingSystem.IsMacOS())
                    thunderbirdRoots.Add(Path.Combine(home, "Library", "Thunderbird", "Profiles"));
            }
            if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(appData))
                thunderbirdRoots.Add(Path.Combine(appData, "Thunderbird", "Profiles"));

            foreach (var root in thunderbirdRoots)
            {
                foreach (var profile in SafeSubdirs(root))
                {
                    AddIfExists(found, Path.Combine(profile, "Mail"));
                    AddIfExists(found, Path.Combine(profile, "ImapMail"));
                }
            }

            if (!string.IsNullOrEmpty(home) && OperatingSystem.IsLinux())
            {
                var share = string.IsNullOrEmpty(localShare) ? Path.Combine(home, ".local", "share") : localShare;
                AddIfExists(found, Path.Combine(share, "evolution", "mail"));
                AddIfExists(found, Path.Combine(home, ".var", "app", "org.gnome.Evolution", "data", "evolution", "mail"));
            }
        }
        catch { }

        return found.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void AddIfExists(List<string> list, string path)
    {
        try { if (Directory.Exists(path)) list.Add(path); } catch { }
    }

    private static string[] SafeSubdirs(string root)
    {
        try { return Directory.Exists(root) ? Directory.GetDirectories(root) : []; }
        catch { return []; }
    }
}
