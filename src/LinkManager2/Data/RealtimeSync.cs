using System;
using System.Threading.Tasks;
using Supabase.Realtime.PostgresChanges;

namespace LinkManager2.Data;

internal static class RealtimeSync
{
    private static bool _started;

    public static async Task StartAsync(Func<Task> onChange)
    {
        if (_started) return;
        _started = true;
        try
        {
            var channel = SupabaseClientHolder.Client.Realtime.Channel("realtime", "public", "items");
            channel.AddPostgresChangeHandler(PostgresChangesOptions.ListenType.All, (_, _) => { _ = onChange(); });
            await channel.Subscribe();
        }
        catch
        {
            _started = false;
        }
    }
}
