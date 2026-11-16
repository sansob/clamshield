using Avalonia;
using Avalonia.Media.Imaging;
using Clamshield.ViewModels;

namespace Clamshield;

/// <summary>
/// Developer aid: with CLAMSHIELD_SHOTS=&lt;folder&gt; set, the app renders every screen to a PNG and quits.
/// It is how the screenshots in the docs are made. See docs/DEVELOPMENT.md.
/// </summary>
public partial class MainWindow
{
    async Task RunShots(string dir)
    {
        Directory.CreateDirectory(dir);
        void Save(string name)
        {
            var px = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var rtb = new RenderTargetBitmap(px, new Vector(96, 96));
            rtb.Render(this);
            rtb.Save(Path.Combine(dir, name + ".png"));
        }
        async Task Shot(string name, Action setup, int wait = 900) { setup(); await Task.Delay(wait); Save(name); }
        await Task.Delay(3500);
        var themes = Environment.GetEnvironmentVariable("CLAMSHIELD_THEMES") == "light" ? new[] { ("light", 1) }
                   : Environment.GetEnvironmentVariable("CLAMSHIELD_THEMES") == "dark" ? new[] { ("dark", 2) } : new[] { ("dark", 2), ("light", 1) };
        foreach (var (tag, theme) in themes)
        {
            MainViewModel.ApplyTheme(theme); await Task.Delay(700);
            await Shot($"{tag}-01-home", () => Vm.Navigate(AppPage.Home), 1200);
            await Shot($"{tag}-02-scan", () => Vm.Navigate(AppPage.Scan));
            await Shot($"{tag}-03-tools", () => { Vm.ToolView = 0; Vm.Navigate(AppPage.Tools); });
            await Shot($"{tag}-04-tool-link", () => Vm.ToolView = 3);
            await Shot($"{tag}-05-tool-password", () => Vm.ToolView = 4);
            await Shot($"{tag}-06-tool-startup", () => Vm.ToolView = 5, 1500);
            await Shot($"{tag}-07-tool-clean", () => Vm.ToolView = 6, 1800);
            await Shot($"{tag}-08-tool-vt", () => Vm.ToolView = 1);
            await Shot($"{tag}-09-tool-shred", () => Vm.ToolView = 2);
            await Shot($"{tag}-09b-tool-block", () => Vm.ToolView = 7);
            await Shot($"{tag}-09c-tool-engine", () => Vm.ToolView = 8, 2500);
            await Shot($"{tag}-10-updates", () => { Vm.ToolView = 0; Vm.Navigate(AppPage.Update); });
            for (int t = 0; t < 6; t++) { var i = t; await Shot($"{tag}-11-settings{i}", () => Vm.OpenSettings(i)); }
            Vm.IsWizard = true;
            for (int st = 0; st < 5; st++) { var i = st; await Shot($"{tag}-12-wizard{i}", () => { Vm.WizardStep = i; }); }
            Vm.IsWizard = false;
        }
        // states that need real data: live log, paused banner, running and finished scans (dark theme)
        MainViewModel.ApplyTheme(themes[0].Item2); await Task.Delay(500);
        var tag0 = themes[0].Item1;
        var addLog = typeof(MainViewModel).GetMethod("AddRtLog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (var (r, f, d) in new[] { ("Clean", "/home/user/Downloads/report-q3.pdf", ""), ("Clean", "/home/user/Downloads/photo_2026.jpg", ""), ("Threat found", "/home/user/Downloads/invoice_2026.exe", "Win.Trojan.Agent-1234"), ("Skipped", "/home/user/Downloads/disk-image.iso", "File too large to scan"), ("Clean", "/home/user/Desktop/notes.txt", "") })
            addLog.Invoke(Vm, new object[] { r, f, d });
        await Shot($"{tag0}-20-home-log", () => Vm.Navigate(AppPage.Home), 1200);
        MainViewModel.DefsDateOverride = DateTime.Now.AddDays(-5);
        await Shot($"{tag0}-19-home-outdated", () => { Vm.Navigate(AppPage.Home); Vm.RefreshForShots(); }, 1200);
        MainViewModel.DefsDateOverride = null; Vm.RefreshForShots();
        await Shot($"{tag0}-21-home-paused", () => Vm.PauseProtection(15), 1000);
        Vm.ResumeProtection();
        if (Environment.GetEnvironmentVariable("CLAMSHIELD_SCANDIR") is { Length: > 0 } sd)
        {
            Vm.Navigate(AppPage.Scan); await Task.Delay(300);
            var scan = Vm.CustomScan(sd);
            await Task.Delay(120); Save($"{tag0}-22-scan-preparing");
            await scan; await Task.Delay(900); Save($"{tag0}-23-scan-done");
            await Shot($"{tag0}-24-home-after", () => Vm.Navigate(AppPage.Home), 1200);
        }
        Console.Error.WriteLine("SHOTS DONE");
        App.Quitting = true; Close();
    }
}
