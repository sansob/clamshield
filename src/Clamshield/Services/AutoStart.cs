using System.Diagnostics;

namespace Clamshield.Services;

/// <summary>Starts Clamshield (hidden in the tray) when the user logs in.</summary>
public static class AutoStart
{
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static string LinuxFile => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Home, ".config"), "autostart", "clamshield.desktop");
    static string MacFile => Path.Combine(Home, "Library", "LaunchAgents", "com.clamshield.app.plist");
    const string WinKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";

    static string CommandLine()
    {
        var (file, lead) = AppLauncher.Command();
        return string.Join(' ', new[] { AppLauncher.Quote(file) }.Concat(lead.Select(AppLauncher.Quote)).Append("--background"));
    }

    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return Reg("query", WinKey, "/v", "Clamshield") == 0;
            return File.Exists(OperatingSystem.IsMacOS() ? MacFile : LinuxFile);
        }
        catch { return false; }
    }

    public static string? Enable()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return Reg("add", WinKey, "/v", "Clamshield", "/t", "REG_SZ", "/d", CommandLine(), "/f") == 0 ? null : "Could not write the startup entry.";
            var (file, lead) = AppLauncher.Command();
            if (OperatingSystem.IsMacOS())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MacFile)!);
                var args = string.Join("", new[] { file }.Concat(lead).Append("--background").Select(a => $"<string>{System.Security.SecurityElement.Escape(a)}</string>"));
                File.WriteAllText(MacFile, $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<plist version=\"1.0\"><dict><key>Label</key><string>com.clamshield.app</string><key>ProgramArguments</key><array>{args}</array><key>RunAtLoad</key><true/></dict></plist>\n");
                return null;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(LinuxFile)!);
            File.WriteAllText(LinuxFile, "[Desktop Entry]\nType=Application\nName=Clamshield\nComment=Antivirus protection\nExec=" + CommandLine() + "\nX-GNOME-Autostart-enabled=true\nTerminal=false\n");
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    public static string? Disable()
    {
        try
        {
            if (OperatingSystem.IsWindows()) { Reg("delete", WinKey, "/v", "Clamshield", "/f"); return null; }
            var f = OperatingSystem.IsMacOS() ? MacFile : LinuxFile;
            if (File.Exists(f)) File.Delete(f);
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    static int Reg(params string[] args)
    {
        var psi = new ProcessStartInfo("reg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!; p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(5000);
        return p.ExitCode;
    }
}
