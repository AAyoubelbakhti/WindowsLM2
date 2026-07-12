using System;
using System.Threading;
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
    private static readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Idempotent: concurrent or repeated calls never open a second channel. The gate serializes
    /// the connect+subscribe so a slow first call can't race a second into a duplicate subscription;
    /// if subscribing throws, the partial channel is torn down so a later call can retry cleanly.
    /// </summary>
    public static async Task StartAsync(Func<Task> onChange)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_channel is not null) return;

            RealtimeChannel? channel = null;
            try
            {
                await SupabaseClientHolder.Client.Realtime.ConnectAsync().ConfigureAwait(false);
                channel = SupabaseClientHolder.Client.Realtime.Channel("realtime", "public", "items");
                channel.AddPostgresChangeHandler(PostgresChangesOptions.ListenType.All, (_, _) => { _ = onChange(); });
                await channel.Subscribe().ConfigureAwait(false);
                _channel = channel;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("realtime start", ex);
                if (channel is not null)
                {
                    try { channel.Unsubscribe(); }
                    catch (Exception cleanupEx) { Diagnostics.Log("realtime start cleanup", cleanupEx); }
                }
                _channel = null;
            }
        }
        finally { _gate.Release(); }
    }

    public static void Stop()
    {
        _gate.Wait();
        try
        {
            var channel = _channel;
            _channel = null;
            if (channel is null) return;
            try { channel.Unsubscribe(); }
            catch (Exception ex) { Diagnostics.Log("realtime unsubscribe", ex); }
        }
        finally { _gate.Release(); }
    }
}
