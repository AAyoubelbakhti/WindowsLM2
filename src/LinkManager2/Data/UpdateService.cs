using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace LinkManager2.Data;

public enum UpdateStatus { UpToDate, Available, Downloaded, NotInstalled, Failed }

public static class UpdateService
{

    private const string FeedUrl = "https://ngzc9gudb8lql269.public.blob.vercel-storage.com/updates/win";

    private static UpdateManager Manager() => new(new SimpleWebSource(FeedUrl));

    public static async Task<(UpdateStatus Status, string? Version)> CheckAndDownloadAsync()
    {
        try
        {
            var mgr = Manager();
            if (!mgr.IsInstalled) return (UpdateStatus.NotInstalled, null);

            var info = await mgr.CheckForUpdatesAsync();
            if (info is null) return (UpdateStatus.UpToDate, null);

            await mgr.DownloadUpdatesAsync(info);
            return (UpdateStatus.Downloaded, info.TargetFullRelease.Version.ToString());
        }
        catch (Exception ex)
        {
            Diagnostics.Log("update check", ex);
            return (UpdateStatus.Failed, null);
        }
    }

    public static void ApplyAndRestart()
    {
        try
        {
            var mgr = Manager();
            var info = mgr.CheckForUpdates();
            if (info is not null) mgr.ApplyUpdatesAndRestart(info);
        }
        catch (Exception ex) { Diagnostics.Log("update apply", ex); }
    }

    public static bool IsInstalled
    {
        get
        {
            try { return Manager().IsInstalled; }
            catch { return false; }
        }
    }
}
