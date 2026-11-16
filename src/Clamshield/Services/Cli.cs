using System.Text.Json;
using Clamshield.Models;

namespace Clamshield.Services;

/// <summary>Headless mode: clamshield --cli [--quick|--full|--scan PATH...] [--json]. Exit code 0 clean, 1 threats, 2 error.</summary>
public static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("Clamshield command line\n  clamshield --cli --quick            scan Downloads, Desktop and Documents\n  clamshield --cli --full             scan the whole device\n  clamshield --cli --running          scan the programs that are running now\n  clamshield --cli --block FILE       block a file by its fingerprint\n  clamshield --cli --yara-add FILE    add a YARA rule file\n  clamshield --cli --engine           show loaded databases\n  clamshield --cli --scan PATH...     scan specific files or folders\n  add --json for machine-readable output\nExit codes: 0 clean, 1 threats found, 2 error");
            return 0;
        }
        AppData.Harden();
        // management commands
        if (args.Contains("--block"))
        {
            var f = args.SkipWhile(a => a != "--block").Skip(1).FirstOrDefault() ?? "";
            var err = Blocklist.Add(f); Console.WriteLine(err == null ? "blocked " + f : "error: " + err); return err == null ? 0 : 2;
        }
        if (args.Contains("--yara-add"))
        {
            var f = args.SkipWhile(a => a != "--yara-add").Skip(1).FirstOrDefault() ?? "";
            var err = YaraRules.AddAsync(f).GetAwaiter().GetResult(); Console.WriteLine(err == null ? "rule added" : "rejected: " + err); return err == null ? 0 : 2;
        }
        if (args.Contains("--engine"))
        {
            var e = EngineDetails.LoadAsync().GetAwaiter().GetResult();
            Console.WriteLine($"{e.Version}\nfolder: {e.DbDir} (own database: {e.OwnDb})\ntotal signatures: {e.TotalSignatures:N0}");
            foreach (var d in e.Databases) Console.WriteLine($"  {d.File,-34} {d.Signatures,10:N0}  {d.Version}  {d.Built}");
            return 0;
        }
        // honour the user's scan settings (strict mode, archive handling, size limit)
        var st = SettingsService.Load();
        ScanOptions.ScanArchives = st.ScanArchives; ScanOptions.MaxFileSizeMb = st.MaxFileSizeMb; ScanOptions.SkipMedia = st.SkipMedia;
        ScanOptions.Strict = st.Strict; ScanOptions.ExtraSignatures = st.ExtraSignatures;
        bool json = args.Contains("--json");
        List<RunningProgram>? running = args.Contains("--running") ? RunningPrograms.List() : null;
        string[] roots = running != null ? running.Where(r => r.Warning != "deleted" && File.Exists(r.Path)).Select(r => r.Path).Distinct().ToArray()
            : args.Contains("--full") ? ScanService.FullRoots()
            : args.Contains("--scan") ? SingleInstance.ParseScanPaths(args)
            : ScanService.QuickRoots();
        if (roots.Length == 0) { Console.Error.WriteLine("Nothing to scan."); return 2; }

        var state = new ScanState();
        var threats = new List<ThreatItem>();
        var start = DateTime.Now;
        ScanService.RunAsync(roots, state, t => { lock (threats) threats.Add(t); if (!json) Console.WriteLine($"FOUND  {t.Virus}  {t.Path}"); }, CancellationToken.None).GetAwaiter().GetResult();

        var secs = (DateTime.Now - start).TotalSeconds;
        if (state.Error != null) { Console.Error.WriteLine(state.Error); return 2; }
        var odd = running?.Where(r => r.Warning != null).Select(r => new CliSuspicious(r.Pid, r.Name, r.Path, r.Warning)).ToList();
        if (json)
            Console.WriteLine(JsonSerializer.Serialize(new CliResult(state.Scanned, threats.Count, Math.Round(secs, 1), threats.Select(t => new CliThreat(t.Path, t.Virus)).ToList(), odd), AppJson.Info<CliResult>()));
        else Console.WriteLine($"Scanned {state.Scanned:N0} files in {secs:0.#}s, {threats.Count} threat(s).");
        return threats.Count > 0 ? 1 : 0;
    }
}
