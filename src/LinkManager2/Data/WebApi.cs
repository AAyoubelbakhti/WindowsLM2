using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace LinkManager2.Data;

public static class WebApi
{
    private const string Base = "https://lm.aelbak.dev";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<string?> SendEmailAsync(string itemId, string toEmail, string message, string bearer)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/api/share-email")
        {
            Content = JsonContent.Create(new { item_id = itemId, to_email = toEmail, message }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var resp = await _http.SendAsync(req);
        if (resp.IsSuccessStatusCode) return null;
        var body = await resp.Content.ReadAsStringAsync();
        return $"Error {(int)resp.StatusCode}: {body}";
    }

    public static async Task<int> DeleteAccountAsync(string bearer)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{Base}/api/account");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var resp = await _http.SendAsync(req);
        return (int)resp.StatusCode;
    }
}
