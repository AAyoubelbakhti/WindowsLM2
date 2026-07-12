using System;
using System.Threading.Tasks;
using Supabase.Realtime;
using Supabase.Realtime.PostgresChanges;

namespace LinkManager2.Data;

/// <summary>
/// Subscribes to realtime changes on the items table and invokes a callback per change.
/// Rebindable across sign-out/sign-in: Stop() tears down the current channel so a fresh
/// StartAsync after a new login re-subscribes against the new session instead of no-opping.
/// The client runs with AutoConnectRealtime=false, so the socket is connected here, after
/// authentication, rather than eagerly at bootstrap.
/// </summary>
internal static class RealtimeSync
{
    private static RealtimeChannel? _channel;
    private static bool _started;

    public static async Task StartAsync(Func<Task> onChange)
    {
        if (_started) Stop();
        _started = true;
        try
        {
            await SupabaseClientHolder.Client.Realtime.ConnectAsync();
            var channel = SupabaseClientHolder.Client.Realtime.Channel("realtime", "public", "items");
            channel.AddPostgresChangeHandler(PostgresChangesOptions.ListenType.All, (_, _) => { _ = onChange(); });
            await channel.Subscribe();
            _channel = channel;
        }
        catch (Exception ex)
        {
            Diagnostics.Log("realtime start", ex);
            _started = false;
            _channel = null;
        }
    }

    public static void Stop()
    {
        _started = false;
        var channel = _channel;
        _channel = null;
        if (channel is null) return;
        try { channel.Unsubscribe(); }
        catch (Exception ex) { Diagnostics.Log("realtime unsubscribe", ex); }
    }
}
