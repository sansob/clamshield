using System.Security.Cryptography;
using System.Text;

namespace Clamshield.Services;

/// <summary>
/// Checks a password against the Have I Been Pwned database using k-anonymity:
/// only the first 5 characters of the SHA-1 hash leave the computer, never the password.
/// </summary>
public static class PasswordBreach
{
    static readonly HttpClient Http = CreateClient();
    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Clamshield");
        c.DefaultRequestHeaders.Add("Add-Padding", "true");   // hides how many real matches the range holds
        return c;
    }

    /// <summary>Times the password appears in known breaches; 0 = never seen; -1 = the lookup failed.</summary>
    public static async Task<int> CountAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password)) return -1;
        try
        {
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));   // protocol requires SHA-1
            var prefix = hash[..5]; var suffix = hash[5..];
            var body = await Http.GetStringAsync("https://api.pwnedpasswords.com/range/" + prefix, ct);
            return ParseRange(body, suffix);
        }
        catch { return -1; }
    }

    /// <summary>Finds a hash suffix in an API response made of "SUFFIX:COUNT" lines.</summary>
    public static int ParseRange(string body, string suffix)
    {
        foreach (var line in body.Split('\n'))
        {
            var t = line.Trim();
            var i = t.IndexOf(':');
            if (i <= 0) continue;
            if (string.Equals(t[..i], suffix, StringComparison.OrdinalIgnoreCase))
                return int.TryParse(t[(i + 1)..], out var n) ? n : 0;
        }
        return 0;
    }
}
