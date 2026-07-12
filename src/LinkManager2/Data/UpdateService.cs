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

    private static UpdateManager? _pendingManager;
    private static UpdateInfo? _pendingInfo;

    public static async Task<(UpdateStatus Status, string? Version)> CheckAndDownloadAsync()
    {
        try
        {
            var mgr = Manager();
            if (!mgr.IsInstalled) return (UpdateStatus.NotInstalled, null);

            var info = await mgr.CheckForUpdatesAsync();
            if (info is null) return (UpdateStatus.UpToDate, null);

            await mgr.DownloadUpdatesAsync(info);
            _pendingManager = mgr;
            _pendingInfo = info;
            var version = info.TargetFullRelease.Version.ToString();
            Notifications.Show("Actualización descargada",
                $"LinkManager {version} está listo. Reinicia para aplicarlo.");
            return (UpdateStatus.Downloaded, version);
        }
        catch (Exception ex)
        {
            Diagnostics.Log("update check", ex);
            return (UpdateStatus.Failed, null);
        }
    }

    public static async Task<bool> ApplyAndRestartAsync()
    {
        try
        {
            var mgr = _pendingManager ?? Manager();
            var info = _pendingInfo ?? await mgr.CheckForUpdatesAsync();
            if (info is null) return false;
            if (_pendingInfo is null) await mgr.DownloadUpdatesAsync(info);
            mgr.ApplyUpdatesAndRestart(info);
            return true;
        }
        catch (Exception ex) { Diagnostics.Log("update apply", ex); return false; }
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
