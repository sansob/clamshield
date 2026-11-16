using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Clamshield.Services;

public enum VtStatus { Detected, Clean, NotFound, NoKey, RateLimited, Error }

public sealed record VtResult(VtStatus Status, int Malicious, int Suspicious, int Total, string Permalink, string Message);

/// <summary>
/// VirusTotal hash lookup. Privacy: only the SHA-256 hash is ever sent, never the file.
/// </summary>
public static class VirusTotalService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Clamshield");
        return c;
    }

    public static async Task<string> Sha256Async(string path, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<VtResult> LookupAsync(string filePath, string? apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return new VtResult(VtStatus.NoKey, 0, 0, 0, "", "No VirusTotal API key set");

        string sha = "";
        try
        {
            sha = await Sha256Async(filePath, ct).ConfigureAwait(false);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.virustotal.com/api/v3/files/{sha}");
            req.Headers.Add("x-apikey", apiKey.Trim());
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseResponse((int)resp.StatusCode, body, sha);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new VtResult(VtStatus.Error, 0, 0, 0, Link(sha), "Cancelled");
        }
        catch (OperationCanceledException)
        {
            return new VtResult(VtStatus.Error, 0, 0, 0, Link(sha), "Request timed out");
        }
        catch (HttpRequestException)
        {
            return new VtResult(VtStatus.Error, 0, 0, 0, Link(sha), "Network error, check your connection");
        }
        catch (Exception ex)
        {
            return new VtResult(VtStatus.Error, 0, 0, 0, Link(sha), "Lookup failed: " + ex.Message);
        }
    }

    private static string Link(string sha) =>
        string.IsNullOrEmpty(sha) ? "" : $"https://www.virustotal.com/gui/file/{sha}";

    public static VtResult ParseResponse(int httpStatus, string body, string sha256)
    {
        var link = Link(sha256);
        switch (httpStatus)
        {
            case 404:
                return new VtResult(VtStatus.NotFound, 0, 0, 0, link, "VirusTotal has never seen this file");
            case 401:
            case 403:
                return new VtResult(VtStatus.Error, 0, 0, 0, link, "Invalid API key");
            case 429:
                return new VtResult(VtStatus.RateLimited, 0, 0, 0, link, "Rate limited, free keys allow 4 lookups per minute");
        }
        if (httpStatus != 200)
            return new VtResult(VtStatus.Error, 0, 0, 0, link, $"VirusTotal returned HTTP {httpStatus}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var stats = doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("last_analysis_stats");
            int mal = 0, sus = 0, total = 0;
            foreach (var p in stats.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt32(out var n)) continue;
                if (p.Name == "malicious") mal = n;
                else if (p.Name == "suspicious") sus = n;
                if (p.Name is "type-unsupported" or "failure" or "timeout") continue;
                total += n;
            }
            if (mal + sus > 0)
                return new VtResult(VtStatus.Detected, mal, sus, total, link, $"{mal + sus} of {total} engines flagged this file");
            return new VtResult(VtStatus.Clean, 0, 0, total, link, $"No detections ({total} engines)");
        }
        catch (Exception)
        {
            return new VtResult(VtStatus.Error, 0, 0, 0, link, "Unexpected response from VirusTotal");
        }
    }

    public static string MaskKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        key = key.Trim();
        return key.Length <= 8 ? new string('•', key.Length) : key[..4] + "…" + key[^4..];
    }
}
