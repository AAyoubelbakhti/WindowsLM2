using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Supabase.Gotrue;

namespace LinkManager2.Data;

internal static class OAuthListener
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public static async Task<bool> RunAsync(
        AuthService auth,
        Constants.Provider provider,
        CancellationToken ct = default)
    {
        var port = GetFreePort();
        var redirect = $"http://127.0.0.1:{port}/auth-callback";

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        OAuthFlow flow;
        try
        {
            flow = await auth.StartOAuthAsync(provider, redirect);
        }
        catch
        {
            listener.Stop();
            throw;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = flow.Url, UseShellExecute = true });
        }
        catch
        {
            listener.Stop();
            throw;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(Timeout);

        HttpListenerContext ctx;
        try
        {
            var ctxTask = listener.GetContextAsync();
            var doneTask = await Task.WhenAny(ctxTask, Task.Delay(Timeout.Add(TimeSpan.FromSeconds(5)), linkedCts.Token));
            if (doneTask != ctxTask)
            {
                listener.Stop();
                return false;
            }
            ctx = await ctxTask;
        }
        catch (OperationCanceledException)
        {
            listener.Stop();
            return false;
        }

        var code = ctx.Request.QueryString["code"];
        var error = ctx.Request.QueryString["error_description"] ?? ctx.Request.QueryString["error"];

        await WriteHtmlAsync(ctx, error is null
            ? "Sesión iniciada. Ya puedes cerrar esta pestaña y volver a LinkManager."
            : $"Error de autenticación: {error}");
        listener.Stop();

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code)) return false;

        await auth.ExchangeCodeAsync(code, flow.Verifier);
        return auth.IsAuthenticated;
    }

    public static async Task<bool> RunWebAssistedAsync(
        AuthService auth,
        string provider,
        CancellationToken ct = default)
    {
        var port = GetFreePort();
        var redirect = $"http://127.0.0.1:{port}/auth-callback";

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        var bridge = "https://lm.aelbak.dev/auth/app-login"
            + $"?provider={Uri.EscapeDataString(provider)}"
            + $"&redirect={Uri.EscapeDataString(redirect)}"
            + $"&state={Uri.EscapeDataString(state)}";

        try
        {
            Process.Start(new ProcessStartInfo { FileName = bridge, UseShellExecute = true });
        }
        catch
        {
            listener.Stop();
            throw;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(Timeout);

        HttpListenerContext ctx;
        while (true)
        {
            try
            {
                var ctxTask = listener.GetContextAsync();
                var doneTask = await Task.WhenAny(ctxTask, Task.Delay(Timeout.Add(TimeSpan.FromSeconds(5)), linkedCts.Token));
                if (doneTask != ctxTask)
                {
                    listener.Stop();
                    return false;
                }
                ctx = await ctxTask;
            }
            catch (OperationCanceledException)
            {
                listener.Stop();
                return false;
            }

            if (ctx.Request.HttpMethod == "GET" &&
                string.Equals(ctx.Request.Url?.AbsolutePath, "/auth-callback", StringComparison.Ordinal))
            {
                break;
            }
            try { ctx.Response.StatusCode = 404; ctx.Response.Close(); } catch { }
        }

        var appCode = ctx.Request.QueryString["code"];
        var returnedState = ctx.Request.QueryString["state"];
        var error = ctx.Request.QueryString["error_description"] ?? ctx.Request.QueryString["error"];

        var stateOk = !string.IsNullOrEmpty(returnedState) &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(returnedState), Encoding.UTF8.GetBytes(state));

        var ok = stateOk && string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(appCode);
        await WriteHtmlAsync(ctx, ok
            ? "Sesión iniciada. Ya puedes cerrar esta pestaña y volver a LinkManager."
            : $"Error de autenticación: {error ?? "no se recibió la sesión"}");
        listener.Stop();

        if (!ok) return false;

        var (access, refresh) = await ExchangeAppCodeAsync(appCode!, ct);
        if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) return false;
        await auth.SetSessionFromTokensAsync(access!, refresh!);
        return auth.IsAuthenticated;
    }

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static async Task<(string? Access, string? Refresh)> ExchangeAppCodeAsync(string code, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://lm.aelbak.dev/api/app-session")
            {
                Content = JsonContent.Create(new { code }),
            };
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return (null, null);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            string? Get(string k) =>
                root.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return (Get("access_token"), Get("refresh_token"));
        }
        catch
        {
            return (null, null);
        }
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task WriteHtmlAsync(HttpListenerContext ctx, string message)
    {
        var html = $$"""
            <!doctype html>
            <html lang="es"><head><meta charset="utf-8"><title>LinkManager</title>
            <style>body{font-family:system-ui;padding:32px;max-width:520px;margin:auto}
            h1{margin-top:0}p{font-size:16px;line-height:1.5}</style></head>
            <body>
              <h1>LinkManager</h1>
              <p>{{WebUtility.HtmlEncode(message)}}</p>
            </body></html>
            """;
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, CancellationToken.None);
        ctx.Response.OutputStream.Close();
    }
}
