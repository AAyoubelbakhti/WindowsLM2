using System;
using System.Threading;
using System.Threading.Tasks;
using Supabase.Realtime;
using Supabase.Realtime.PostgresChanges;

namespace LinkManager2.Data;

/// <summary>
/// Subscribes to realtime changes on the items table and invokes a callback per change.
/// Rebindable across sign-out/sign-in: StopAsync() tears down the current channel so a fresh
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
    /// The callback receives the id of the row a delete removed, or null for any other change.
    /// </summary>
    public static async Task StartAsync(Func<string?, Task> onChange)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_channel is not null)
                return;

            RealtimeChannel? channel = null;
            try
            {
                await SupabaseClientHolder.Client.Realtime.ConnectAsync().ConfigureAwait(false);
                channel = SupabaseClientHolder.Client.Realtime.Channel("items");
                channel.OnPostgresChange(
                    (_, change) => onChange(DeletedItemId(change)),
                    PostgresChangesOptions.ListenType.All,
                    new PostgresChangesFilter { Table = "items" }
                );
                await channel.Subscribe().ConfigureAwait(false);
                _channel = channel;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("realtime start", ex);
                if (channel is not null)
                {
                    try
                    {
                        channel.Unsubscribe();
                    }
                    catch (Exception cleanupEx)
                    {
                        Diagnostics.Log("realtime start cleanup", cleanupEx);
                    }
                }
                _channel = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Realtime cannot check row level security on a row that no longer exists, so every
    /// subscriber receives every user's deletes with the primary key alone; the id lets the caller
    /// ignore the ones for rows it never had.
    /// </summary>
    private static string? DeletedItemId(PostgresChangesResponse change) =>
        change.Payload?.Data?.Type == Constants.EventType.Delete
            ? change.OldModel<Item>()?.Id
            : null;

    public static async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var channel = _channel;
            _channel = null;
            if (channel is null)
                return;
            try
            {
                channel.Unsubscribe();
            }
            catch (Exception ex)
            {
                Diagnostics.Log("realtime unsubscribe", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
