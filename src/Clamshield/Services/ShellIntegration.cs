using System.Diagnostics;

namespace Clamshield.Services;

/// <summary>"Scan with Clamshield" file-manager context-menu entry (Linux file managers, Windows Explorer).</summary>
public static class ShellIntegration
{
    const string Label = "Scan with Clamshield";
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    static string DataHome()
    {
        var x = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(x) && Path.IsPathRooted(x)) return x;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
    }
    static string NautilusFile() => Path.Combine(DataHome(), "nautilus", "scripts", Label);
    static string NemoFile() => Path.Combine(DataHome(), "nemo", "actions", "clamshield-scan.nemo_action");
    static string KdeFile() => Path.Combine(DataHome(), "kio", "servicemenus", "clamshield-scan.desktop");

    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsLinux()) return File.Exists(NautilusFile());
            if (OperatingSystem.IsWindows()) return Reg("query", @"HKCU\Software\Classes\*\shell\Clamshield") == null;
        }
        catch { }
        return false;
    }

    public static string? Enable()
    {
        try
        {
            if (OperatingSystem.IsLinux()) return EnableLinux();
            if (OperatingSystem.IsWindows()) return EnableWindows();
            return "Shell integration is not available on macOS yet.";
        }
        catch (Exception e) { return e.Message; }
    }

    public static string? Disable()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                foreach (var f in new[] { NautilusFile(), NemoFile(), KdeFile() })
                    if (File.Exists(f)) File.Delete(f);
                return null;
            }
            if (OperatingSystem.IsWindows())
            {
                foreach (var k in WinKeys())
                {
                    if (Reg("query", k) == null) { var err = Reg("delete", k, "/f"); if (err != null) return err; }
                }
                return null;
            }
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    static string? EnableLinux()
    {
        var (file, lead) = AppLauncher.Command();
        var cmd = string.Join(" ", new[] { AppLauncher.Quote(file) }.Concat(lead.Select(AppLauncher.Quote)));

        // Nautilus: selected paths arrive as positional args.
        WriteFile(NautilusFile(), $"#!/bin/sh\nexec {cmd} --scan \"$@\"\n", exec: true);
        // Nemo action
        WriteFile(NemoFile(),
            $"[Nemo Action]\nName={Label}\nComment={Label}\nExec={cmd} --scan %F\nSelection=any\nExtensions=any;\nIcon-Name=security-high\n", false);
        // Dolphin / KDE service menu
        WriteFile(KdeFile(),
            $"[Desktop Entry]\nType=Service\nX-KDE-ServiceTypes=KonqPopupMenu/Plugin\nMimeType=all/all;inode/directory;\nActions=scan;\nX-KDE-Priority=TopLevel\n\n" +
            $"[Desktop Action scan]\nName={Label}\nIcon=security-high\nExec={cmd} --scan %F\n", exec: true);
        return null;
    }

    static void WriteFile(string path, string content, bool exec)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, exec
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }

    static string[] WinKeys() => new[]
    {
        @"HKCU\Software\Classes\*\shell\Clamshield",
        @"HKCU\Software\Classes\Directory\shell\Clamshield",
    };

    static string? EnableWindows()
    {
        var (file, lead) = AppLauncher.Command();
        var cmd = string.Join(" ", new[] { $"\"{file}\"" }.Concat(lead.Select(l => $"\"{l}\""))) + " --scan \"%1\"";
        foreach (var k in WinKeys())
        {
            var err = Reg("add", k, "/ve", "/d", Label, "/f")
                   ?? Reg("add", k, "/v", "Icon", "/d", $"\"{file}\"", "/f")
                   ?? Reg("add", k + @"\command", "/ve", "/d", cmd, "/f");
            if (err != null) return err;
        }
        return null;
    }

    /// <summary>Run reg.exe; null on success, else error text.</summary>
    static string? Reg(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("reg.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return "Could not start reg.exe";
            var err = p.StandardError.ReadToEnd();
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } return "reg.exe timed out"; }
            return p.ExitCode == 0 ? null : (string.IsNullOrWhiteSpace(err) ? "reg.exe failed" : err.Trim());
        }
        catch (Exception e) { return e.Message; }
    }
}
