using Supabase;

namespace LinkManager2.Data;

public static class SupabaseClientHolder
{
    private static Client? _client;
    private static string? _url;
    private static string? _anonKey;
    private static Task? _initTask;

    public static Client Client => _client ?? throw NotInit();
    public static string SupabaseUrl => _url ?? throw NotInit();
    public static string AnonKey => _anonKey ?? throw NotInit();
    public static bool IsReady => _client is not null && (_initTask?.IsCompletedSuccessfully ?? false);

    public static void Init(string url, string anonKey)
    {
        if (_client is not null) return;
        _url = url;
        _anonKey = anonKey;

        _client = new Client(url, anonKey, new SupabaseOptions
        {

            AutoConnectRealtime = false,
            AutoRefreshToken = true,
        });
        _client.Auth.SetPersistence(new LocalFileSessionHandler());
        _initTask = Task.Run(() => InitializeAndRefreshAsync(_client));
    }

    public static Task Ready => _initTask ?? throw NotInit();

    private static async Task InitializeAndRefreshAsync(Client c)
    {
        try
        {
            await c.InitializeAsync();
            c.Auth.LoadSession();

            var s = c.Auth.CurrentSession;
            if (s is null || string.IsNullOrEmpty(s.RefreshToken)) return;
            try { await c.Auth.RefreshSession(); }
            catch (Exception ex)
            {
                if (!IsTransientNet(ex)) WipeSession(c);
            }
        }
        catch (Exception ex)
        {

            if (!IsTransientNet(ex)) WipeSession(c);
        }
    }

    /// <summary>Best-effort teardown on app exit; each step is guarded so a failing disconnect can't
    /// block the others or the shutdown path.</summary>
    public static void Shutdown()
    {
        var c = _client;
        if (c is null) return;
        try { c.Realtime.Disconnect(); } catch { }
        try { c.Auth.Shutdown(); } catch { }
    }

    /// <summary>Drops a session that failed to refresh for a non-transient reason. Each step is
    /// guarded because wiping is a recovery path that must not throw.</summary>
    private static void WipeSession(Client c)
    {
        try { new LocalFileSessionHandler().DestroySession(); } catch { }
        try { c.Auth.Shutdown(); } catch { }
    }

    private static bool IsTransientNet(Exception? ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
            if (cur is System.Net.Http.HttpRequestException
                or System.Net.Sockets.SocketException
                or TaskCanceledException
                or System.IO.IOException) return true;
        return false;
    }

    private static InvalidOperationException NotInit() => new("Supabase not initialised.");
}
