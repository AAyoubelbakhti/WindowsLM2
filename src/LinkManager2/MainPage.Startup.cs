using System;
using System.Threading.Tasks;
using LinkManager2.Data;
using LinkManager2.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2;

/// <summary>Startup, session recovery and background app-config/update checks.</summary>
public sealed partial class MainPage : Page
{
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!App.SupabaseReady)
        {
            ((App)Application.Current).Window!.NavigateTo(typeof(LoginPage));
            return;
        }

        if (!App.State.Auth.IsAuthenticated && !await EnsureAuthenticatedFromLocalAsync())
        {
            ((App)Application.Current).Window!.NavigateTo(typeof(LoginPage));
            return;
        }

        var warning = ((App)Application.Current).Window?.HotkeyWarning;

        _loading = true;
        try
        {
            App.State.LoadCachedItems();
            RefreshVisible();
        }
        catch (Exception ex)
        {
            SetStatus(Strings.Format("Startup_CacheUnreadable", ex.Message));
        }
        SetStatus(Strings.Get("Startup_Loading"));

        try
        {
            await App.State.ReloadAllAsync();
            _loading = false;
            RefreshVisible();
            SetStatus(
                warning
                    ?? Strings.Format(
                        "Startup_ItemCountWithUser",
                        App.State.Items.Count,
                        App.State.Auth.UserEmail
                    )
            );
            await FlushPendingAsync();

            _ = RealtimeSync.StartAsync(() =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _realtimeDebounce.Stop();
                    _realtimeDebounce.Start();
                });
                return System.Threading.Tasks.Task.CompletedTask;
            });

            _ = RunStartupChecksAsync();
        }
        catch (Exception ex)
        {
            _loading = false;
            if (await TryRecoverExpiredSessionAsync(ex))
                return;
            UpdateEmptyState();
            SetStatus(Strings.Format("Startup_OfflineShowingCache", ex.Message));
        }
    }

    /// <summary>
    /// Direct-startup path: waits for the Supabase client to load and refresh the persisted
    /// session (no extra network beyond the client's own refresh) and reports whether the
    /// session is now usable. Returns false so the caller can fall back to LoginPage.
    /// </summary>
    private static async Task<bool> EnsureAuthenticatedFromLocalAsync()
    {
        try
        {
            await App.State.WaitReadyAsync();
        }
        catch (Exception ex)
        {
            Diagnostics.Log("wait-ready on direct start", ex);
        }
        return App.State.Auth.IsAuthenticated;
    }

    private async Task<bool> TryRecoverExpiredSessionAsync(Exception ex)
    {
        if (!AuthService.IsAuthExpired(ex))
            return false;

        SetStatus(Strings.Get("Startup_SessionExpiredReconnecting"));
        if (await App.State.Auth.TryRefreshSessionAsync())
        {
            try
            {
                await App.State.ReloadAllAsync();
                RefreshVisible();
                SetStatus(
                    Strings.Format(
                        "Startup_ItemCountWithUser",
                        App.State.Items.Count,
                        App.State.Auth.UserEmail
                    )
                );
                await FlushPendingAsync();
                return true;
            }
            catch (Exception retryEx)
            {
                Diagnostics.Log("reload after refresh", retryEx);
            }
        }

        SetStatus(Strings.Get("Startup_SessionExpired"));
        RealtimeSync.Stop();
        App.State.Clear();
        ((App)Application.Current).Window!.NavigateTo(typeof(LoginPage));
        return true;
    }

    private async Task RunStartupChecksAsync()
    {
        var gate = await VersionGate.CheckAsync(App.Build);
        if (gate.UpdateRequired)
        {
            await ForceUpdateAsync(gate.Message);
            return;
        }
        await CheckForUpdatesAsync();
    }

    private async Task ForceUpdateAsync(string? gateMessage)
    {
        SetStatus(Strings.Get("Startup_ForcedUpdateDownloading"));
        var (status, _) = await UpdateService.CheckAndDownloadAsync();
        if (status == UpdateStatus.Downloaded)
        {
            SetStatus(Strings.Get("Startup_ApplyingUpdate"));
            await UpdateService.ApplyAndRestartAsync();
            return;
        }

        var message = gateMessage ?? Strings.Get("Startup_ForcedUpdateDefaultMessage");
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("Startup_ForcedUpdateTitle"),
            Content = Strings.Format("Startup_ForcedUpdateBody", message),
            PrimaryButtonText = Strings.Get("Startup_ForcedUpdateRetry"),
            CloseButtonText = Strings.Get("Common_Close"),
            DefaultButton = ContentDialogButton.Primary,
        };
        var (shown, result) = await dlg.TryShowGuardedAsync();
        if (shown && result == ContentDialogResult.Primary)
        {
            await ForceUpdateAsync(gateMessage);
            return;
        }
        SetStatus(Strings.Get("Startup_UpdatePending"));
    }

    private async Task CheckForUpdatesAsync()
    {
        var (status, version) = await UpdateService.CheckAndDownloadAsync();
        if (status != UpdateStatus.Downloaded)
            return;

        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("Startup_UpdateAvailableTitle"),
            Content = Strings.Format("Startup_UpdateAvailableBody", version),
            PrimaryButtonText = Strings.Get("Startup_UpdateAvailableRestart"),
            CloseButtonText = Strings.Get("Startup_UpdateAvailableLater"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dlg.ShowGuardedAsync() == ContentDialogResult.Primary)
        {
            SetStatus(Strings.Get("Startup_ApplyingUpdate"));
            if (!await UpdateService.ApplyAndRestartAsync())
                SetStatus(Strings.Get("Startup_ApplyUpdateFailed"));
        }
    }

    private async Task FlushPendingAsync()
    {
        try
        {
            await App.State.FlushPendingAsync();
            RefreshVisible();
        }
        catch (Exception ex)
        {
            Diagnostics.Log("flush-pending", ex);
        }
    }
}
