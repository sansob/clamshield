using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clamshield.Services;

/// <summary>
/// VirusTotal web-link check. Privacy: the link itself is sent to VirusTotal (and submitted
/// for analysis if VirusTotal has never seen it).
/// </summary>
public static class VirusTotalUrl
{
    private const int MaxPolls = 3;
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(4);
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Clamshield");
        return c;
    }

    private static VtResult Err(string link, string msg) => new(VtStatus.Error, 0, 0, 0, link, msg);

    /// <summary>Returns the normalized http/https URL, or "" if the input is not a usable web link.</summary>
    public static string NormalizeUrl(string input)
    {
        try
        {
            var s = (input ?? "").Trim();
            if (s.Length == 0 || s.Any(char.IsWhiteSpace)) return "";
            if (!s.Contains("://", StringComparison.Ordinal))
            {
                // "javascript:alert(1)", "mailto:x" etc. have a scheme but no "//"; host:port is allowed.
                int colon = s.IndexOf(':');
                if (colon > 0 && !(colon + 1 < s.Length && char.IsDigit(s[colon + 1]))) return "";
                s = "https://" + s;
            }
            if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return "";
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return "";
            if (string.IsNullOrEmpty(u.Host)) return "";
            return s;
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static string UrlId(string normalized) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(normalized)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Permalink(string normalized) =>
        "https://www.virustotal.com/gui/url/" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();

    public static async Task<VtResult> LookupAsync(string url, string? apiKey, CancellationToken ct = default)
    {
        var norm = NormalizeUrl(url);
        if (norm.Length == 0) return Err("", "That doesn't look like a web link.");
        if (string.IsNullOrWhiteSpace(apiKey))
            return new VtResult(VtStatus.NoKey, 0, 0, 0, "", "No VirusTotal API key set");

        var link = Permalink(norm);
        var key = apiKey.Trim();
        try
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.virustotal.com/api/v3/urls/{UrlId(norm)}"))
            {
                req.Headers.Add("x-apikey", key);
                using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (resp.StatusCode != HttpStatusCode.NotFound)
                    return ParseUrlResponse((int)resp.StatusCode, body, link);
            }

            // Never seen: submit it.
            string analysisId;
            using (var req = new HttpRequestMessage(HttpMethod.Post, "https://www.virustotal.com/api/v3/urls"))
            {
                req.Headers.Add("x-apikey", key);
                req.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("url", norm) });
                using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (resp.StatusCode != HttpStatusCode.OK)
                    return ParseUrlResponse((int)resp.StatusCode == 404 ? 500 : (int)resp.StatusCode, body, link);
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    analysisId = doc.RootElement.GetProperty("data").GetProperty("id").GetString() ?? "";
                }
                catch (Exception)
                {
                    analysisId = "";
                }
                if (analysisId.Length == 0) return Err(link, "Unexpected response from VirusTotal");
            }

            for (int i = 0; i < MaxPolls; i++)
            {
                await Task.Delay(PollDelay, ct).ConfigureAwait(false);
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"https://www.virustotal.com/api/v3/analyses/{Uri.EscapeDataString(analysisId)}");
                req.Headers.Add("x-apikey", key);
                using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (resp.StatusCode != HttpStatusCode.OK)
                    return ParseUrlResponse((int)resp.StatusCode == 404 ? 500 : (int)resp.StatusCode, body, link);
                var r = ParseAnalysis(body, link);
                if (r != null) return r;
            }
            return Err(link, "VirusTotal is still analysing this link. Try again in a minute.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Err(link, "Cancelled");
        }
        catch (OperationCanceledException)
        {
            return Err(link, "Request timed out");
        }
        catch (HttpRequestException)
        {
            return Err(link, "Network error, check your connection");
        }
        catch (Exception)
        {
            return Err(link, "Lookup failed");
        }
    }

    /// <summary>Parses a GET /urls/{id} response.</summary>
    public static VtResult ParseUrlResponse(int httpStatus, string body, string permalink)
    {
        switch (httpStatus)
        {
            case 404:
                return new VtResult(VtStatus.NotFound, 0, 0, 0, permalink, "VirusTotal has never seen this link");
            case 401:
            case 403:
                return Err(permalink, "Invalid API key");
            case 429:
                return new VtResult(VtStatus.RateLimited, 0, 0, 0, permalink,
                    "Rate limited, free keys allow 4 lookups per minute");
        }
        if (httpStatus != 200) return Err(permalink, $"VirusTotal returned HTTP {httpStatus}");
        return FromStats(body, "last_analysis_stats", permalink) ?? Err(permalink, "Unexpected response from VirusTotal");
    }

    // Returns null while the analysis is not completed (or on malformed JSON -> treated as error by caller via null check below).
    private static VtResult? ParseAnalysis(string body, string link)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var attrs = doc.RootElement.GetProperty("data").GetProperty("attributes");
            if (attrs.GetProperty("status").GetString() != "completed") return null;
        }
        catch (Exception)
        {
            return Err(link, "Unexpected response from VirusTotal");
        }
        return FromStats(body, "stats", link) ?? Err(link, "Unexpected response from VirusTotal");
    }

    private static VtResult? FromStats(string body, string statsName, string link)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var stats = doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty(statsName);
            int mal = 0, sus = 0, harmless = 0, undetected = 0;
            foreach (var p in stats.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt32(out var n)) continue;
                switch (p.Name)
                {
                    case "malicious": mal = n; break;
                    case "suspicious": sus = n; break;
                    case "harmless": harmless = n; break;
                    case "undetected": undetected = n; break;
                }
            }
            int total = harmless + mal + sus + undetected;
            if (mal + sus > 0)
                return new VtResult(VtStatus.Detected, mal, sus, total, link, $"{mal + sus} of {total} engines flagged this link");
            return new VtResult(VtStatus.Clean, 0, 0, total, link, $"No detections ({total} engines)");
        }
        catch (Exception)
        {
            return null;
        }
    }
}
