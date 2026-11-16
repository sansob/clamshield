using System.Security.Cryptography;
using Clamshield.Models;
using Clamshield.Services;

namespace Clamshield.Tests;

public class QuarantineTests
{
    [Fact]
    public void Quarantined_file_can_no_longer_be_executed_and_is_restored_exactly()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = TestEnvironment.NewDir("q");
        var f = Path.Combine(dir, "evil.sh");
        File.WriteAllText(f, "echo hi");
        File.SetUnixFileMode(f, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Assert.Null(QuarantineService.Add(new ThreatItem(f, "Test.Virus")));
        Assert.False(File.Exists(f));

        var item = QuarantineService.Load().Single(i => i.OriginalPath == f);
        var stored = QuarantineService.StoredPath(item);
        Assert.True(File.Exists(stored));
        Assert.False(File.GetUnixFileMode(stored).HasFlag(UnixFileMode.UserExecute));
        Assert.False(File.GetUnixFileMode(stored).HasFlag(UnixFileMode.GroupRead));

        Assert.Null(QuarantineService.Restore(item));
        Assert.Equal("echo hi", File.ReadAllText(f));
        Assert.True(File.GetUnixFileMode(f).HasFlag(UnixFileMode.UserExecute));
        Assert.DoesNotContain(QuarantineService.Load(), i => i.Id == item.Id);
    }

    [Fact]
    public void Purge_keeps_recent_items_and_ignores_zero_days()
    {
        var dir = TestEnvironment.NewDir("p");
        var f = Path.Combine(dir, "x.bin"); File.WriteAllText(f, "x");
        QuarantineService.Add(new ThreatItem(f, "Test.Purge"));
        Assert.Equal(0, QuarantineService.PurgeOlderThan(0));
        Assert.Equal(0, QuarantineService.PurgeOlderThan(30));
        var item = QuarantineService.Load().First(i => i.OriginalPath == f);
        QuarantineService.Delete(item);
        Assert.False(File.Exists(QuarantineService.StoredPath(item)));
    }
}

public class DataFolderTests
{
    [Fact]
    public void Settings_file_and_data_folder_are_private_to_the_owner()
    {
        if (OperatingSystem.IsWindows()) return;
        var s = new AppSettings { VtKey = "secret-key" };
        SettingsService.Save(s);
        AppData.Harden();
        var file = Path.Combine(AppData.Dir, "settings.json");
        Assert.True(File.Exists(file));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(AppData.Dir));
        Assert.Equal("secret-key", SettingsService.Load().VtKey);
    }
}

public class BlocklistTests
{
    [Fact]
    public void Blocking_writes_a_clamav_hash_signature_and_unblocking_removes_it()
    {
        var f = Path.Combine(TestEnvironment.NewDir("b"), "tool.bin");
        var bytes = RandomNumberGenerator.GetBytes(1234);
        File.WriteAllBytes(f, bytes);
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.Null(Blocklist.Add(f));
        Assert.Null(Blocklist.Add(f));   // adding again must not duplicate
        var lines = File.ReadAllLines(Blocklist.HsbPath).Where(l => l.StartsWith(sha)).ToList();
        Assert.Single(lines);
        Assert.Equal($"{sha}:1234:Clamshield.Blocked.tool.bin", lines[0]);

        Blocklist.Remove(sha);
        Assert.DoesNotContain(Blocklist.All(), e => e.Sha256 == sha);
        Assert.True(!File.Exists(Blocklist.HsbPath) || !File.ReadAllText(Blocklist.HsbPath).Contains(sha));
    }

    [Fact]
    public void Missing_file_is_reported_not_thrown() => Assert.NotNull(Blocklist.Add("/definitely/not/here.bin"));
}

public class TrustedFilesTests
{
    [Fact]
    public void Trust_follows_the_content_not_the_path()
    {
        var f = Path.Combine(TestEnvironment.NewDir("t"), "a.txt");
        File.WriteAllText(f, "original");
        TrustedFiles.Add(TrustedFiles.HashFile(f), f);
        Assert.True(TrustedFiles.IsTrusted(f));
        File.WriteAllText(f, "changed!");
        Assert.False(TrustedFiles.IsTrusted(f));
        Assert.False(TrustedFiles.IsTrusted("/no/such/file"));
    }
}

public class ShredderTests
{
    [Fact]
    public async Task Shreds_files_but_never_follows_links_or_touches_protected_paths()
    {
        var dir = TestEnvironment.NewDir("s");
        var victim = Path.Combine(dir, "victim.txt"); File.WriteAllText(victim, new string('x', 3000));
        var outside = Path.Combine(TestEnvironment.NewDir("o"), "canary.txt"); File.WriteAllText(outside, "keep me");
        if (!OperatingSystem.IsWindows()) File.CreateSymbolicLink(Path.Combine(dir, "link"), outside);

        var res = await Shredder.ShredAsync(new[] { victim, Path.Combine(dir, "link") }, null, CancellationToken.None);
        Assert.False(File.Exists(victim));
        Assert.True(File.Exists(outside));
        Assert.Equal("keep me", File.ReadAllText(outside));
        Assert.True(res.Shredded >= 1);

        var protectedRun = await Shredder.ShredAsync(new[] { "/" }, null, CancellationToken.None);
        Assert.True(protectedRun.Failed >= 1);
        Assert.True(Directory.Exists("/usr") || OperatingSystem.IsWindows());
    }
}

public class ScanSettingsTests
{
    [Fact]
    public void Strict_mode_adds_the_extra_detection_flags()
    {
        ScanOptions.Strict = false;
        Assert.DoesNotContain("--detect-pua=yes", ScanOptions.ClamscanArgs());
        ScanOptions.Strict = true;
        var args = ScanOptions.ClamscanArgs().ToList();
        Assert.Contains("--detect-pua=yes", args);
        Assert.Contains("--alert-macros=yes", args);
        Assert.Contains("--alert-encrypted=yes", args);
        ScanOptions.Strict = false;
    }

    [Fact]
    public void Exclusions_match_whole_path_segments_only()
    {
        Exclusions.Set(new[] { "/home/u/Games" });
        Assert.True(Exclusions.IsExcluded("/home/u/Games"));
        Assert.True(Exclusions.IsExcluded("/home/u/Games/save.dat"));
        Assert.False(Exclusions.IsExcluded("/home/u/GamesBackup/x"));
        Exclusions.Set(Array.Empty<string>());
    }

    [Fact]
    public void Password_range_parser_finds_the_suffix_and_handles_padding()
    {
        var body = "AAAAA:0\r\n1E4C9B93F3F0682250B6CF8331B7EE68FD8:10437277\r\nBBBBB:3\r\n";
        Assert.Equal(10437277, PasswordBreach.ParseRange(body, "1E4C9B93F3F0682250B6CF8331B7EE68FD8"));
        Assert.Equal(0, PasswordBreach.ParseRange(body, "FFFFF"));
        Assert.Equal(0, PasswordBreach.ParseRange("", "ABC"));
    }
}

public class TextTests
{
    [Fact]
    public void History_summary_uses_proper_singular_and_plural()
    {
        Loc.I.Language = "en";
        Assert.StartsWith("1 file  ·  1 threat  ·  ", new ScanRecord { Files = 1, Threats = 1, Seconds = 5 }.Summary);
        Assert.StartsWith("18,423 files  ·  0 threats  ·  ", new ScanRecord { Files = 18423, Threats = 0, Seconds = 83 }.Summary);
    }

    [Fact]
    public void Every_translation_key_used_in_the_interface_exists_in_the_dictionary()
    {
        var xaml = File.ReadAllText(TestEnvironment.RepoFile("src/Clamshield/MainWindow.axaml"));
        var keys = System.Text.RegularExpressions.Regex.Matches(xaml, @"\[([a-z0-9_]+)\], Source=").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(keys);
        var missing = keys.Where(k => !LocStrings.ByKey.ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Missing translation keys: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_English_text_passed_to_the_translator_has_an_indonesian_version()
    {
        var root = Path.GetDirectoryName(TestEnvironment.RepoFile("src/Clamshield/MainWindow.axaml"))!;
        var rx = new System.Text.RegularExpressions.Regex("Loc\\.[TF]\\(\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        var missing = new SortedSet<string>();
        foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.EndsWith("LocStrings.cs")))
            foreach (System.Text.RegularExpressions.Match m in rx.Matches(File.ReadAllText(f)))
            {
                var en = m.Groups[1].Value.Replace("\\\"", "\"");
                if (en.Any(char.IsLetter) && !LocStrings.ByEn.ContainsKey(en)) missing.Add(en);
            }
        Assert.True(missing.Count == 0, "No Indonesian text for: " + string.Join(" | ", missing));
    }
}

public class YaraTests
{
    [Fact]
    public async Task Broken_rules_are_rejected_and_valid_rules_are_accepted()
    {
        if (ClamAvLocator.Find("clamscan") == null) return;   // needs the real engine
        var dir = TestEnvironment.NewDir("y");
        var good = Path.Combine(dir, "good.yar"); File.WriteAllText(good, "rule Ok { strings: $a = \"MARKER_X\" condition: $a }\n");
        var bad = Path.Combine(dir, "bad.yar"); File.WriteAllText(bad, "rule Broken { strings: $a = \"x\" condition: $b and and }\n");
        var garbage = Path.Combine(dir, "garbage.yar"); File.WriteAllText(garbage, "this is not yara\n");

        Assert.Null(await YaraRules.AddAsync(good));
        var err = await YaraRules.AddAsync(bad);
        Assert.NotNull(err); Assert.Contains("bad.yar", err);
        Assert.NotNull(await YaraRules.AddAsync(garbage));
        var installed = YaraRules.List().Select(r => r.Name).ToList();
        Assert.Contains("good", installed); Assert.DoesNotContain("bad", installed); Assert.DoesNotContain("garbage", installed);
        foreach (var r in YaraRules.List()) YaraRules.Remove(r);
    }
}

public class DesktopEntryTests
{
    [Fact]
    public void Launcher_entry_matches_the_window_class_and_icon_name()
    {
        var e = LinuxIntegration.BuildDesktopEntry("\"/opt/clamshield/Clamshield\"");
        Assert.Contains("[Desktop Entry]", e);
        Assert.Contains("Exec=\"/opt/clamshield/Clamshield\"", e);
        Assert.Contains("Icon=clamshield", e);
        Assert.Contains("StartupWMClass=clamshield", e);   // the shell matches the window to the launcher with this
        Assert.Contains("Categories=System;Security;", e);
        Assert.EndsWith("\n", e);
    }

    [Fact]
    public void Definitions_older_than_three_days_or_never_fetched_count_as_out_of_date()
    {
        var now = new DateTime(2026, 11, 16, 12, 0, 0);
        Assert.True(Clamshield.ViewModels.MainViewModel.IsOutdated(null, now));
        Assert.True(Clamshield.ViewModels.MainViewModel.IsOutdated(now.AddDays(-3).AddMinutes(-1), now));
        Assert.False(Clamshield.ViewModels.MainViewModel.IsOutdated(now.AddDays(-2), now));
        Assert.False(Clamshield.ViewModels.MainViewModel.IsOutdated(now, now));
    }
}
