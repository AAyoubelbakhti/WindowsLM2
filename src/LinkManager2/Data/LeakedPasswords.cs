using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LinkManager2.Data;

internal static class LeakedPasswords
{
    private const string RangeApi = "https://api.pwnedpasswords.com/range/";
    private const int PrefixLength = 5;

    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(3);
    private static readonly HttpClient _http = new();

    /// <summary>
    /// How many times a password appears in the Pwned Passwords lists, the check Supabase only offers on its paid plans.
    /// Neither the password nor its hash leaves this device: only the first characters of the hash do, and padding hides
    /// how many matches came back. When the service cannot answer in time it says zero, as a lookup must not stop anyone
    /// from signing up. The lists say how often, never where.
    /// </summary>
    public static async Task<long> TimesLeakedAsync(string password, HttpClient? client = null)
    {
        try
        {
            using var deadline = new CancellationTokenSource(LookupTimeout);
            var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                RangeApi + hash[..PrefixLength]
            );
            request.Headers.Add("Add-Padding", "true");
            request.Headers.UserAgent.ParseAdd("LinkManager");
            using var response = await (client ?? _http).SendAsync(request, deadline.Token);
            if (!response.IsSuccessStatusCode)
            {
                Diagnostics.Log($"leaked-password: HTTP {(int)response.StatusCode}");
                return 0;
            }
            var body = await response.Content.ReadAsStringAsync();
            return CountIn(body, hash[PrefixLength..]);
        }
        catch (Exception ex)
        {
            Diagnostics.Log("leaked-password", ex);
            return 0;
        }
    }

    private static long CountIn(string body, string rest)
    {
        foreach (var line in body.Split('\n'))
        {
            var fields = line.Trim().Split(':');
            if (fields[0] != rest)
                continue;
            return
                fields.Length > 1
                && long.TryParse(
                    fields[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var count
                )
                ? count
                : 0;
        }
        return 0;
    }
}
