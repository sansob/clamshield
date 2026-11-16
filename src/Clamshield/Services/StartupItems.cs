using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Clamshield.Services;

public sealed record StartupEntry(string Id, string Name, string Command, string Source, bool Enabled, bool CanChange);

/// <summary>Lists programs that start at login and toggles them reversibly (nothing is ever deleted,
/// except a user override file this class itself created on Linux).</summary>
public static class StartupItems
{
    const string Marker = "# Created by Clamshield (override)";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static List<StartupEntry> List()
    {
        try
        {
            var l = OperatingSystem.IsWindows() ? ListWindows()
                  : OperatingSystem.IsMacOS() ? ListMac()
                  : ListLinux();
            return l.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch { return new(); }
    }

    public static string? SetEnabled(StartupEntry entry, bool enabled)
    {
        try
        {
            if (!entry.CanChange) return "This entry is managed by the system and cannot be changed from here. Nothing was changed.";
            if (OperatingSystem.IsWindows()) return SetWindows(entry, enabled);
            if (OperatingSystem.IsMacOS()) return SetMac(entry, enabled);
            return SetLinux(entry, enabled);
        }
        catch (Exception ex) { return "Could not change the entry: " + ex.Message; }
    }

    // ---------------------------------------------------------------- Linux

    static string UserAutostart()
    {
        var x = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var cfg = !string.IsNullOrWhiteSpace(x) && Path.IsPathRooted(x) ? x
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(cfg, "autostart");
    }

    const string SystemAutostart = "/etc/xdg/autostart";

    static Dictionary<string, string> ParseDesktop(string[] lines)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inMain = false;
        foreach (var raw in lines)
        {
            var s = raw.Trim();
            if (s.StartsWith('[')) { inMain = s == "[Desktop Entry]"; continue; }
            if (!inMain || s.Length == 0 || s[0] == '#') continue;
            int i = s.IndexOf('=');
            if (i <= 0) continue;
            var k = s[..i].Trim();
            if (k.Contains('[')) continue; // localized keys
            d.TryAdd(k, s[(i + 1)..].Trim());
        }
        return d;
    }

    static bool DesktopEnabled(Dictionary<string, string> d) =>
        !(d.TryGetValue("Hidden", out var h) && h.Equals("true", StringComparison.OrdinalIgnoreCase)) &&
        !(d.TryGetValue("X-GNOME-Autostart-enabled", out var g) && g.Equals("false", StringComparison.OrdinalIgnoreCase));

    static List<StartupEntry> ListLinux()
    {
        var res = new List<StartupEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (dir, src) in new[] { (UserAutostart(), "User"), (SystemAutostart, "System") })
        {
            string[] files;
            try { files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.desktop") : []; } catch { continue; }
            foreach (var f in files.OrderBy(x => x, StringComparer.Ordinal))
            {
                try
                {
                    var fn = Path.GetFileName(f);
                    if (!seen.Add(fn)) continue; // user masks system
                    var d = ParseDesktop(File.ReadAllLines(f));
                    d.TryGetValue("Name", out var name);
                    d.TryGetValue("Exec", out var exec);
                    d.TryGetValue("Comment", out var comment);
                    var cmd = exec ?? "";
                    if (!string.IsNullOrEmpty(comment)) cmd = cmd.Length > 0 ? cmd + "  (" + comment + ")" : comment;
                    res.Add(new(f, string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(f) : name,
                        cmd, src, DesktopEnabled(d), true));
                }
                catch { }
            }
        }
        return res;
    }

    /// <summary>Sets keys inside [Desktop Entry], replacing existing lines and keeping everything else.</summary>
    static string[] SetKeys(string[] lines, params (string Key, string Value)[] kv)
    {
        var list = lines.ToList();
        int start = list.FindIndex(l => l.Trim() == "[Desktop Entry]");
        if (start < 0) { list.Insert(0, "[Desktop Entry]"); start = 0; }
        int end = list.FindIndex(start + 1, l => l.Trim().StartsWith('['));
        if (end < 0) end = list.Count;
        foreach (var (k, v) in kv)
        {
            bool done = false;
            for (int i = start + 1; i < end; i++)
            {
                var t = list[i].TrimStart();
                int eq = t.IndexOf('=');
                if (eq > 0 && t[..eq].Trim() == k) { list[i] = k + "=" + v; done = true; }
            }
            if (!done)
            {
                int ins = end;
                while (ins > start + 1 && string.IsNullOrWhiteSpace(list[ins - 1])) ins--;
                list.Insert(ins, k + "=" + v);
                end++;
            }
        }
        return list.ToArray();
    }

    static string? SetLinux(StartupEntry e, bool enable)
    {
        var path = e.Id;
        var fn = Path.GetFileName(path);
        var userPath = Path.Combine(UserAutostart(), fn);
        bool isUser = string.Equals(Path.GetFullPath(path), Path.GetFullPath(userPath), StringComparison.Ordinal);
        if (!File.Exists(path)) return "The startup file no longer exists.";
        var lines = File.ReadAllLines(path);

        if (isUser)
        {
            bool isOverride = lines.Any(l => l.Trim() == Marker);
            if (enable && isOverride)
            {
                var sys = Path.Combine(SystemAutostart, fn);
                bool sysEnabled = !File.Exists(sys) || DesktopEnabled(ParseDesktop(File.ReadAllLines(sys)));
                if (sysEnabled) { File.Delete(userPath); return null; }
            }
            File.WriteAllLines(userPath, enable
                ? SetKeys(lines, ("X-GNOME-Autostart-enabled", "true"), ("Hidden", "false"))
                : SetKeys(lines, ("X-GNOME-Autostart-enabled", "false")));
            return null;
        }

        // System entry: only ever write a user-dir override; /etc is never touched.
        if (enable == DesktopEnabled(ParseDesktop(lines))) return null;
        Directory.CreateDirectory(UserAutostart());
        var body = enable
            ? SetKeys(lines, ("Hidden", "false"), ("X-GNOME-Autostart-enabled", "true"))
            : SetKeys(lines, ("Hidden", "true"));
        File.WriteAllLines(userPath, body.Append(Marker));
        return null;
    }

    // ---------------------------------------------------------------- Windows

    static (int Code, string Out) Reg(params string[] args)
    {
        var psi = new ProcessStartInfo("reg.exe")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync();
        var er = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } return (-1, "timeout"); }
        return (p.ExitCode, o.Result + er.Result);
    }

    static List<(string Name, string Type, string Data)> RegValues(string key)
    {
        var res = new List<(string, string, string)>();
        var (code, text) = Reg("query", key);
        if (code != 0) return res;
        var rx = new Regex(@"^\s+(.+?)\s{4}(REG_[A-Z_]+)(?:\s{4}(.*))?$");
        foreach (var line in text.Split('\n'))
        {
            var m = rx.Match(line.TrimEnd('\r'));
            if (m.Success) res.Add((m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value.Trim()));
        }
        return res;
    }

    static List<StartupEntry> ListWindows()
    {
        var res = new List<StartupEntry>();
        if (!OperatingSystem.IsWindows()) return res;
        foreach (var (hive, src) in new[] { ("HKCU", "User"), ("HKLM", "System") })
        {
            var approved = RegValues(hive + "\\" + ApprovedKey)
                .Where(v => v.Type == "REG_BINARY")
                .ToDictionary(v => v.Name, v => v.Data, StringComparer.OrdinalIgnoreCase);
            foreach (var v in RegValues(hive + "\\" + RunKey))
            {
                bool enabled = true;
                if (approved.TryGetValue(v.Name, out var hex) && hex.Length >= 2)
                    enabled = !hex.StartsWith("03", StringComparison.Ordinal);
                res.Add(new(hive + "|" + v.Name, v.Name, v.Data, src, enabled, hive == "HKCU"));
            }
        }
        return res;
    }

    static string? SetWindows(StartupEntry e, bool enable)
    {
        if (!OperatingSystem.IsWindows()) return "Not supported on this platform.";
        int bar = e.Id.IndexOf('|');
        if (bar < 0 || e.Id[..bar] != "HKCU") return "Machine-wide entries cannot be changed without administrator rights. Nothing was changed.";
        var name = e.Id[(bar + 1)..];
        var data = (enable ? "02" : "03") + new string('0', 22);
        var (code, text) = Reg("add", "HKCU\\" + ApprovedKey, "/v", name, "/t", "REG_BINARY", "/d", data, "/f");
        return code == 0 ? null : "reg.exe failed: " + text.Trim();
    }

    // ---------------------------------------------------------------- macOS

    static List<StartupEntry> ListMac()
    {
        var res = new List<StartupEntry>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (dir, src, change) in new[] { (Path.Combine(home, "Library", "LaunchAgents"), "User", true),
                                                   ("/Library/LaunchAgents", "System", false) })
        {
            string[] files;
            try { files = Directory.Exists(dir) ? Directory.GetFiles(dir) : []; } catch { continue; }
            foreach (var f in files)
            {
                bool dis = f.EndsWith(".plist.disabled", StringComparison.Ordinal);
                if (!dis && !f.EndsWith(".plist", StringComparison.Ordinal)) continue;
                string name = Path.GetFileName(f).Replace(".plist.disabled", "").Replace(".plist", ""), cmd = "";
                try
                {
                    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                    using var xr = XmlReader.Create(f, settings);
                    var dict = XDocument.Load(xr).Root?.Element("dict");
                    if (dict != null)
                    {
                        var els = dict.Elements().ToList();
                        for (int i = 0; i + 1 < els.Count; i++)
                        {
                            if (els[i].Name != "key") continue;
                            var v = els[i + 1];
                            switch (els[i].Value)
                            {
                                case "Label" when v.Value.Length > 0: name = v.Value; break;
                                case "Program" when cmd.Length == 0: cmd = v.Value; break;
                                case "ProgramArguments" when v.Name == "array":
                                    cmd = string.Join(' ', v.Elements("string").Select(s => s.Value)); break;
                            }
                        }
                    }
                }
                catch { }
                res.Add(new(f, name, cmd, src, !dis, change));
            }
        }
        return res;
    }

    static string? SetMac(StartupEntry e, bool enable)
    {
        var p = e.Id;
        if (!File.Exists(p)) return "The startup file no longer exists.";
        string target;
        if (enable) { if (!p.EndsWith(".disabled", StringComparison.Ordinal)) return null; target = p[..^".disabled".Length]; }
        else { if (p.EndsWith(".disabled", StringComparison.Ordinal)) return null; target = p + ".disabled"; }
        if (File.Exists(target)) return "A file with the target name already exists. Nothing was changed.";
        File.Move(p, target);
        return null;
    }
}
