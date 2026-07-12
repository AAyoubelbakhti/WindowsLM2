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

    public readonly record struct LinkCheckSummary(int Checked, int Ok, int Broken, int Pending);

    /// <summary>
    /// Rechecks every link. Sends an empty JSON body so the request carries an application/json
    /// content type: without it the API host rejects the bodyless POST as a cross-site form
    /// submission (CSRF origin check) and the check silently fails.
    /// </summary>
    public static async Task<LinkCheckSummary?> CheckAllLinksAsync(string bearer)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/api/items/check-all")
        {
            Content = JsonContent.Create(new { }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        int Get(string k) => root.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetInt32() : 0;
        return new LinkCheckSummary(Get("checked"), Get("ok"), Get("broken"), Get("pending"));
    }

    /// <summary>Rechecks a single link. Empty JSON body required for the same CSRF reason as
    /// <see cref="CheckAllLinksAsync"/>.</summary>
    public static async Task<string?> CheckLinkAsync(string itemId, string bearer)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/api/items/{itemId}/check")
        {
            Content = JsonContent.Create(new { }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("status", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString() : null;
    }
}
