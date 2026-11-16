using System.Diagnostics;

namespace Clamshield.Services;

public sealed record YaraRule(string Name, string File);

/// <summary>User-added YARA rules. ClamAV loads .yar files from the database folder (a subset of YARA is supported).</summary>
public static class YaraRules
{
    const string Prefix = "clamshield-yara-";

    public static List<YaraRule> List()
    {
        try
        {
            if (!Directory.Exists(ClamAvLocator.DbDir)) return new();
            return Directory.EnumerateFiles(ClamAvLocator.DbDir, Prefix + "*.yar")
                .Select(f => new YaraRule(Path.GetFileNameWithoutExtension(f)[Prefix.Length..], f))
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch { return new(); }
    }

    /// <summary>Checks that ClamAV can load the rule file, then installs it. Returns null on success or the engine's complaint.</summary>
    public static async Task<string?> AddAsync(string path)
    {
        var exe = ClamAvLocator.Find("clamscan");
        if (exe == null) return "ClamAV was not found.";
        var tmp = Path.Combine(Path.GetTempPath(), "clamshield-yara-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var rule = Path.Combine(tmp, "rule.yar"); File.Copy(path, rule);
            var probe = Path.Combine(tmp, "probe.txt"); File.WriteAllText(probe, "probe");
            var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--no-summary"); psi.ArgumentList.Add("--database=" + rule); psi.ArgumentList.Add(probe);
            using var p = Process.Start(psi)!;
            var err = await p.StandardError.ReadToEndAsync(); var outp = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            // ClamAV reports a rule it cannot use only through messages (the exit code stays 0) and silently skips it
            var report = err + "\n" + outp;
            var lines = report.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
            bool failed = p.ExitCode == 2 || lines.Any(l => l.Contains("LibClamAV Error") || l.Contains("failed to parse or load") || l.Contains("empty database"));
            if (failed)
            {
                var why = lines.FirstOrDefault(l => l.Contains("yyerror(): ")) ?? lines.FirstOrDefault(l => l.Length > 0) ?? "The rule could not be loaded.";
                var at = why.IndexOf("yyerror(): ", StringComparison.Ordinal);
                if (at >= 0) why = why[(at + "yyerror(): ".Length)..];
                return why.Replace(rule, Path.GetFileName(path));
            }
            var safe = new string(Path.GetFileNameWithoutExtension(path).Select(c => char.IsLetterOrDigit(c) && c < 128 || c is '_' or '-' ? c : '_').ToArray());
            if (safe.Length == 0) safe = "rule";
            Directory.CreateDirectory(ClamAvLocator.DbDir);
            File.Copy(path, Path.Combine(ClamAvLocator.DbDir, Prefix + safe + ".yar"), true);
            return null;
        }
        catch (Exception e) { return e.Message; }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    public static void Remove(YaraRule r) { try { File.Delete(r.File); } catch { } }
}
