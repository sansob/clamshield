using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Clamshield.Services;

/// <summary>Installs ClamAV when it is missing. Returns null on success, or an error message.</summary>
public static class EngineInstaller
{
    public static async Task<string?> InstallAsync(IProgress<(double pct, string msg)> progress, CancellationToken ct)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return await DownloadAsync("win", ".win.x64.zip", false, progress, ct);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return await DownloadAsync("mac", ".macos.universal.pkg", true, progress, ct);
            return await LinuxPackageAsync(progress, ct);
        }
        catch (Exception e) { return "Could not install ClamAV: " + e.Message; }
        finally { ClamAvLocator.ForgetCache(); }
    }

    // ---------- Windows / macOS: official release from GitHub ----------
    static async Task<string?> DownloadAsync(string os, string suffix, bool pkg, IProgress<(double, string)> progress, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ClamshieldSetup/1.0");
        progress.Report((-1, "Finding the latest ClamAV release…"));
        var json = await http.GetStringAsync("https://api.github.com/repos/Cisco-Talos/clamav/releases/latest", ct);
        using var doc = JsonDocument.Parse(json);
        string? url = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            if (a.GetProperty("name").GetString() is { } n && n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            { url = a.GetProperty("browser_download_url").GetString(); break; }
        if (url == null) return "No ClamAV download was found for this system. Install ClamAV manually from clamav.net.";

        var tmp = Path.Combine(Path.GetTempPath(), "clamshield-engine" + Path.GetExtension(url));
        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            var buf = new byte[81920]; long done = 0; int n; var last = DateTime.MinValue;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct); done += n;
                if (total > 0 && (DateTime.Now - last).TotalMilliseconds > 150)
                { last = DateTime.Now; progress.Report((done * 100.0 / total, $"Downloading ClamAV… {done / 1048576} / {total / 1048576} MB")); }
            }
        }

        progress.Report((-1, "Installing…"));
        if (Directory.Exists(ClamAvLocator.EngineDir)) Directory.Delete(ClamAvLocator.EngineDir, true);
        Directory.CreateDirectory(ClamAvLocator.EngineDir);
        if (!pkg) await Task.Run(() => ZipFile.ExtractToDirectory(tmp, ClamAvLocator.EngineDir, true), ct);
        else
        {
            var psi = new ProcessStartInfo("pkgutil") { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--expand-full"); psi.ArgumentList.Add(tmp); psi.ArgumentList.Add(Path.Combine(ClamAvLocator.EngineDir, "pkg"));
            using var p = Process.Start(psi)!; await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0) return "Could not unpack the ClamAV installer.";
        }
        try { File.Delete(tmp); } catch { }
        return ClamAvLocator.Find("clamscan") == null ? "ClamAV was downloaded but could not be found afterwards." : null;
    }

    // ---------- Linux: distro package manager through the graphical admin prompt ----------
    static async Task<string?> LinuxPackageAsync(IProgress<(double, string)> progress, CancellationToken ct)
    {
        string[]? cmd =
            Has("dnf") ? new[] { "dnf", "install", "-y", "clamav", "clamav-update", "clamd" } :
            Has("apt-get") ? new[] { "apt-get", "install", "-y", "clamav", "clamav-daemon" } :
            Has("zypper") ? new[] { "zypper", "--non-interactive", "install", "clamav" } :
            Has("pacman") ? new[] { "pacman", "-S", "--noconfirm", "clamav" } : null;
        if (cmd == null) return "Your Linux distribution isn't recognised. Please install the ClamAV package manually.";
        if (!Has("pkexec")) return "Administrator access (pkexec) isn't available. Run: sudo " + string.Join(' ', cmd);

        progress.Report((-1, "Waiting for your administrator password…"));
        var psi = new ProcessStartInfo("pkexec") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (cmd[0] == "apt-get") { psi.ArgumentList.Add("env"); psi.ArgumentList.Add("DEBIAN_FRONTEND=noninteractive"); }
        foreach (var a in cmd) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync(ct);
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync(ct)) != null)
            if (line.Trim().Length > 0) progress.Report((-1, "Installing ClamAV… " + (line.Length > 70 ? line[..70] : line)));
        await p.WaitForExitAsync(ct);
        if (p.ExitCode == 126 || p.ExitCode == 127) return "The administrator prompt was cancelled. Try again to install ClamAV.";
        if (p.ExitCode != 0) return "The package manager reported an error: " + (await err).Trim().Split('\n').LastOrDefault();
        return null;
    }

    static bool Has(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Concat(new[] { "/usr/bin", "/usr/sbin", "/bin" }).Any(d => File.Exists(Path.Combine(d, exe)));
}
