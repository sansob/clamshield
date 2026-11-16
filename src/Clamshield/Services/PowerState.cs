using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Clamshield.Services;

/// <summary>Best-effort checks used to postpone heavy scans (battery, fullscreen apps).</summary>
public static class PowerState
{
    private const int ProcessTimeoutMs = 1500;

    public static bool ShouldDeferHeavyWork(bool pauseOnBattery, bool pauseWhenFullscreen)
    {
        try
        {
            if (pauseOnBattery && IsOnBattery()) return true;
            if (pauseWhenFullscreen && IsFullscreenAppActive()) return true;
        }
        catch { }
        return false;
    }

    public static bool IsOnBattery()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WindowsOnBattery();
            if (OperatingSystem.IsLinux()) return LinuxOnBattery();
            if (OperatingSystem.IsMacOS())
            {
                var o = Run("pmset", "-g batt");
                return o != null && o.Contains("Battery Power", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
        return false;
    }

    public static bool IsFullscreenAppActive()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WindowsFullscreen();
            if (OperatingSystem.IsLinux()) return LinuxFullscreen();
        }
        catch { }
        return false;
    }

    // ---------- Linux ----------

    private static bool LinuxOnBattery()
    {
        const string root = "/sys/class/power_supply";
        if (!Directory.Exists(root)) return false;
        bool hasBattery = false, discharging = false, acOnline = false;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            string type = ReadTrim(Path.Combine(dir, "type"));
            if (type.Equals("Battery", StringComparison.OrdinalIgnoreCase))
            {
                // Skip peripheral batteries (mice, etc.)
                if (ReadTrim(Path.Combine(dir, "scope")).Equals("Device", StringComparison.OrdinalIgnoreCase))
                    continue;
                hasBattery = true;
                if (ReadTrim(Path.Combine(dir, "status")).Equals("Discharging", StringComparison.OrdinalIgnoreCase))
                    discharging = true;
            }
            else if (type.Equals("Mains", StringComparison.OrdinalIgnoreCase) ||
                     type.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
            {
                if (ReadTrim(Path.Combine(dir, "online")) == "1") acOnline = true;
            }
        }
        return hasBattery && discharging && !acOnline;
    }

    private static bool LinuxFullscreen()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return false;
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase)) return false;

        var root = Run("xprop", "-root _NET_ACTIVE_WINDOW");
        if (root == null) return false;
        int i = root.IndexOf("0x", StringComparison.Ordinal);
        if (i < 0) return false;
        var id = new string(root[(i + 2)..].TakeWhile(Uri.IsHexDigit).ToArray());
        if (id.Length == 0 || id.All(c => c == '0')) return false;
        var state = Run("xprop", $"-id 0x{id} _NET_WM_STATE");
        return state != null && state.Contains("_NET_WM_STATE_FULLSCREEN", StringComparison.Ordinal);
    }

    // ---------- Windows ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    private static bool WindowsOnBattery()
    {
        if (!OperatingSystem.IsWindows()) return false;
        return GetSystemPowerStatus(out var s) && s.ACLineStatus == 0;
    }

    private static bool WindowsFullscreen()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (SHQueryUserNotificationState(out int state) != 0) return false;
        return state is 2 or 3 or 4;
    }

    // ---------- helpers ----------

    private static string ReadTrim(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : ""; }
        catch { return ""; }
    }

    /// <summary>Runs a process with a short timeout; returns stdout or null on any failure.</summary>
    private static string? Run(string file, string args)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo(file, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            p.Start();
            var outTask = p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(ProcessTimeoutMs))
            {
                try { p.Kill(true); } catch { }
                return null;
            }
            if (!outTask.Wait(ProcessTimeoutMs)) return null;
            return p.ExitCode == 0 ? outTask.Result : null;
        }
        catch { return null; }
    }
}
