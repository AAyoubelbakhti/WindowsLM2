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
        try { App.State.LoadCachedItems(); RefreshVisible(); }
        catch (Exception ex) { SetStatus($"Caché ilegible: {ex.Message}"); }
        SetStatus("Cargando…");

        try
        {
            await App.State.ReloadAllAsync();
            _loading = false;
            RefreshVisible();
            SetStatus(warning ?? $"{App.State.Items.Count} elementos · {App.State.Auth.UserEmail}");
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
            if (await TryRecoverExpiredSessionAsync(ex)) return;
            UpdateEmptyState();
            SetStatus($"Sin conexión: {ex.Message}. Mostrando caché.");
        }
    }

    /// <summary>
    /// Direct-startup path: waits for the Supabase client to load and refresh the persisted
    /// session (no extra network beyond the client's own refresh) and reports whether the
    /// session is now usable. Returns false so the caller can fall back to LoginPage.
    /// </summary>
    private static async Task<bool> EnsureAuthenticatedFromLocalAsync()
    {
        try { await App.State.WaitReadyAsync(); }
        catch (Exception ex) { Diagnostics.Log("wait-ready on direct start", ex); }
        return App.State.Auth.IsAuthenticated;
    }

    private async Task<bool> TryRecoverExpiredSessionAsync(Exception ex)
    {
        if (!AuthService.IsAuthExpired(ex)) return false;

        SetStatus("Sesión caducada. Reconectando…");
        if (await App.State.Auth.TryRefreshSessionAsync())
        {
            try
            {
                await App.State.ReloadAllAsync();
                RefreshVisible();
                SetStatus($"{App.State.Items.Count} elementos · {App.State.Auth.UserEmail}");
                return true;
            }
            catch (Exception retryEx) { Diagnostics.Log("reload after refresh", retryEx); }
        }

        SetStatus("Tu sesión ha caducado. Vuelve a iniciar sesión.");
        RealtimeSync.Stop();
        App.State.Clear();
        ((App)Application.Current).Window!.NavigateTo(typeof(LoginPage));
        return true;
    }

    private async Task RunStartupChecksAsync()
    {
        if (await CheckAppConfigAsync()) return;
        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        var (status, version) = await UpdateService.CheckAndDownloadAsync();
        if (status != UpdateStatus.Downloaded) return;

        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Actualización disponible",
            Content = $"Se ha descargado la versión {version}. ¿Reiniciar ahora para aplicarla?",
            PrimaryButtonText = "Reiniciar y actualizar",
            CloseButtonText = "Más tarde",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dlg.ShowGuardedAsync() == ContentDialogResult.Primary)
        {
            SetStatus("Aplicando actualización…");
            if (!await UpdateService.ApplyAndRestartAsync())
                SetStatus("No se pudo aplicar la actualización. Inténtalo desde Ajustes.");
        }
    }

    private async Task<bool> CheckAppConfigAsync()
    {
        var gate = await VersionGate.CheckAsync(App.Build);
        if (!gate.UpdateRequired) return false;

        var message = gate.Message ?? "Hay una versión nueva. Actualiza la app para continuar.";
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Actualización necesaria",
            Content = message,
            PrimaryButtonText = "Cerrar sesión",
        };
        var (shown, _) = await dlg.TryShowGuardedAsync();
        if (!shown) SetStatus($"Actualización necesaria: {message} Se cerrará la sesión.");
        RealtimeSync.Stop();
        App.State.ClearLocalCacheForCurrentUser();
        try { await App.State.Auth.SignOutAsync(); }
        catch (Exception ex) { Diagnostics.Log("version-gate sign-out", ex); }
        ((App)Application.Current).Window!.NavigateTo(typeof(LoginPage));
        return true;
    }

    private async Task FlushPendingAsync()
    {
        try { await App.State.FlushPendingAsync(); RefreshVisible(); }
        catch (Exception ex) { Diagnostics.Log("flush-pending", ex); }
    }
}
