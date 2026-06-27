using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LinkManager2.Data;

public readonly record struct VersionGateResult(bool UpdateRequired, string? Message);

public static class VersionGate
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private const string ConfigUrl = "https://lm.aelbak.dev/api/app-config";

    public static async Task<VersionGateResult> CheckAsync(int currentBuild)
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(ConfigUrl));
            var root = doc.RootElement;
            var min = root.TryGetProperty("min_windows_build", out var mv) && mv.ValueKind == JsonValueKind.Number ? mv.GetInt32() : 0;
            var msg = root.TryGetProperty("message", out var ms) && ms.ValueKind == JsonValueKind.String ? ms.GetString() : null;
            return new VersionGateResult(currentBuild < min, msg);
        }
        catch (Exception ex)
        {
            Diagnostics.Log("version-gate fetch", ex);
            return new VersionGateResult(false, null);
        }
    }
}
