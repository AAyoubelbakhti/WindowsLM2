using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace LinkManager2.Data;

public static class WebApi
{
    private const string Base = "https://lm.aelbak.dev";

    private static readonly HttpClient _http = CreateClient();

    /// <summary>The client for the web API, which asks for its messages in the language of the app's texts.</summary>
    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.AcceptLanguage.TryParseAdd(Strings.Get("App_LanguageTag"));
        return http;
    }

    public static async Task<string?> SendEmailAsync(
        string itemId,
        string toEmail,
        string message,
        string bearer
    )
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/api/share-email")
        {
            Content = JsonContent.Create(
                new
                {
                    item_id = itemId,
                    to_email = toEmail,
                    message,
                }
            ),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var resp = await _http.SendAsync(req);
        if (resp.IsSuccessStatusCode)
            return null;
        var reason = MessageOf(await resp.Content.ReadAsStringAsync());
        return string.IsNullOrWhiteSpace(reason)
            ? Strings.Format("Web_HttpErrorCode", (int)resp.StatusCode)
            : Strings.Format("Web_HttpError", (int)resp.StatusCode, reason);
    }

    /// <summary>
    /// The message of an error answer of the web API, which comes as {"error": "..."} in the language asked for: the
    /// whole body would be read aloud as JSON.
    /// </summary>
    private static string? MessageOf(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == System.Text.Json.JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
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
        if (!resp.IsSuccessStatusCode)
            return null;
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        int Get(string k) =>
            root.TryGetProperty(k, out var v)
            && v.ValueKind == System.Text.Json.JsonValueKind.Number
                ? v.GetInt32()
                : 0;
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
        if (!resp.IsSuccessStatusCode)
            return null;
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return
            doc.RootElement.TryGetProperty("status", out var v)
            && v.ValueKind == System.Text.Json.JsonValueKind.String
            ? v.GetString()
            : null;
    }
}
