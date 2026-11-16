using System.Diagnostics;
using Avalonia.Platform;

namespace Clamshield.Services;

/// <summary>
/// Makes Clamshield a "real" desktop app on Linux: a launcher entry and icons in the user's own folders,
/// so the dock, the app grid and the window switcher show the Clamshield icon instead of a generic one.
/// Nothing outside the user's home folder is touched and no administrator rights are needed.
/// </summary>
public static class LinuxIntegration
{
    public const string AppId = "clamshield";   // also the window class, which the desktop shell matches against StartupWMClass

    static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x
        ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    public static string DesktopFilePath => Path.Combine(DataHome, "applications", AppId + ".desktop");

    /// <summary>Content of the launcher entry (public so it can be tested).</summary>
    public static string BuildDesktopEntry(string execLine) => string.Join('\n', new[]
    {
        "[Desktop Entry]",
        "Type=Application",
        "Version=1.0",
        "Name=Clamshield",
        "GenericName=Antivirus",
        "Comment=A friendly antivirus built on ClamAV",
        "Exec=" + execLine,
        "Icon=" + AppId,
        "Terminal=false",
        "Categories=System;Security;",
        "Keywords=antivirus;clamav;virus;malware;scan;security;",
        "StartupNotify=true",
        "StartupWMClass=" + AppId,
        "",
    });

    /// <summary>Creates or refreshes the launcher entry and icons. Safe to call on every start; does nothing off Linux.</summary>
    public static void EnsureDesktopEntry()
    {
        if (!OperatingSystem.IsLinux()) return;
        try
        {
            var (file, lead) = AppLauncher.Command();
            var exec = string.Join(' ', new[] { AppLauncher.Quote(file) }.Concat(lead.Select(AppLauncher.Quote)));
            var changed = WriteIfDifferent(DesktopFilePath, BuildDesktopEntry(exec));

            changed |= WriteResource("avares://Clamshield/Assets/icon.png", Path.Combine(DataHome, "icons", "hicolor", "512x512", "apps", AppId + ".png"));
            changed |= WriteResource("avares://Clamshield/Assets/icon.svg", Path.Combine(DataHome, "icons", "hicolor", "scalable", "apps", AppId + ".svg"));

            if (changed)
            {
                Run("update-desktop-database", Path.GetDirectoryName(DesktopFilePath)!);
                Run("gtk-update-icon-cache", "-q", "-t", "-f", Path.Combine(DataHome, "icons", "hicolor"));
            }
        }
        catch { /* purely cosmetic: never stop the app because of it */ }
    }

    static bool WriteIfDifferent(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) == content) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return true;
    }

    static bool WriteResource(string uri, string dest)
    {
        using var src = AssetLoader.Open(new Uri(uri));
        using var ms = new MemoryStream(); src.CopyTo(ms);
        var bytes = ms.ToArray();
        if (File.Exists(dest) && File.ReadAllBytes(dest).AsSpan().SequenceEqual(bytes)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllBytes(dest, bytes);
        return true;
    }

    static void Run(string tool, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi); p?.WaitForExit(4000);
        }
        catch { /* the tool may simply not be installed */ }
    }
}
