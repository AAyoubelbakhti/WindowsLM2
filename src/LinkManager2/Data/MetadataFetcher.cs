using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LinkManager2.Data;

public sealed class MetadataResult
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("site_name")] public string? SiteName { get; set; }
    [JsonPropertyName("favicon_url")] public string? FaviconUrl { get; set; }
}

public static class MetadataFetcher
{
    private const string ApiBase = "https://lm.aelbak.dev";
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Best-effort metadata lookup; returns null on any failure (network, auth, parse) so
    /// callers treat missing metadata as a non-error and skip enrichment.</summary>
    public static async Task<MetadataResult?> FetchAsync(string url, CancellationToken ct = default)
    {
        var token = SupabaseClientHolder.Client.Auth.CurrentSession?.AccessToken;
        if (token is null) return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/api/metadata")
            {
                Content = JsonContent.Create(new { url }),
            };
            req.Headers.Authorization = new("Bearer", token);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<MetadataResult>(cancellationToken: ct);
        }
        catch
        {
            return null;
        }
    }
}
