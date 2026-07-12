using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LinkManager2.Data;
using LinkManager2.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LinkManager2;

/// <summary>Single-item actions (add/edit/delete/favorite/share/…) and library management commands.</summary>
public sealed partial class MainPage : Page
{
    private async void OnAddClick(object sender, RoutedEventArgs e) => await AddOrEditAsync(null);
    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } vm) await AddOrEditAsync(vm.Source);
    }

    private async Task AddOrEditAsync(Item? initial)
    {
        var dlg = new AddEditDialog(App.State.Categories, initial,
            allTags: App.State.Tags, repo: App.State.Repo, showQuickCreate: initial is null)
        { XamlRoot = XamlRoot };
        if (await dlg.ShowGuardedAsync() != ContentDialogResult.Primary) return;

        var (ok, type) = Validation.DetectType(dlg.ItemValue);
        if (!ok) { SetStatus("Valor inválido."); return; }
        var value = type == ItemTypes.Url ? Validation.NormalizeUrl(dlg.ItemValue) : dlg.ItemValue;

        if (initial is null)
        {
            var existing = App.State.Items.FirstOrDefault(i =>
                string.Equals(i.Value, value, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                var keep = await DialogHelper.ConfirmAsync(XamlRoot,
                    "Enlace duplicado",
                    $"Ya tienes este enlace guardado como \"{existing.Title}\". ¿Guardarlo de todos modos?",
                    "Guardar igualmente", "Cancelar");
                if (!keep) { SetStatus("Cancelado: ya tenías ese enlace."); return; }
            }
        }

        try
        {
            if (initial is null)
            {
                var result = await App.State.AddItemAsync(dlg.ItemTitle, value, type, dlg.ItemCategoryId, dlg.SelectedTagIds, dlg.ItemDescription);
                SetStatus(result == AppState.AddResult.QueuedOffline
                    ? $"Sin conexión, encolado: {dlg.ItemTitle}."
                    : $"Añadido: {dlg.ItemTitle}");

                if (result == AppState.AddResult.Added)
                {
                    var added = System.Linq.Enumerable.FirstOrDefault(
                        App.State.Items, i => i.Value == value && i.Title == dlg.ItemTitle);
                    if (added is not null)
                    {
                        var userProvidedNotes = !string.IsNullOrWhiteSpace(dlg.ItemDescription);
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            await App.State.Repo.EnrichMetadataAsync(added, userProvidedNotes);
                            DispatcherQueue.TryEnqueue(async () =>
                            {
                                try { await App.State.ReloadItemsAsync(); RefreshVisible(); }
                                catch (Exception ex) { Diagnostics.Log("reload-after-enrich", ex); }
                            });
                        });
                    }
                }
            }
            else
            {
                var result = await App.State.UpdateItemAsync(initial.Id, dlg.ItemTitle, value, type, dlg.ItemCategoryId, dlg.ItemDescription);
                SetStatus(result == AppState.OpResult.QueuedOffline
                    ? $"Sin conexión, cambios encolados: {dlg.ItemTitle}."
                    : $"Actualizado: {dlg.ItemTitle}");
            }
            RefreshVisible();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } vm) await DeleteWithConfirmAsync(vm.Source);
    }

    private async Task DeleteWithConfirmAsync(Item item)
    {
        var ok = await DialogHelper.ConfirmAsync(XamlRoot,
            "Confirmar borrado",
            $"¿Borrar el elemento \"{item.Title}\"?",
            "Borrar", "Cancelar");
        if (!ok) return;
        try
        {
            var result = await App.State.DeleteItemAsync(item.Id);
            if (result == AppState.OpResult.QueuedOffline)
            {
                RefreshVisible();
                SetStatus($"Sin conexión, borrado encolado: {item.Title}.");
                return;
            }
            _lastUndo = new UndoAction($"borrar \"{item.Title}\"",
                () => App.State.Repo.RestoreAsync(item));
            RefreshVisible();
            SetStatus($"Borrado: {item.Title}. Ctrl+Z para deshacer.");
        }
        catch (Exception ex)
        {
            if (await TryRecoverExpiredSessionAsync(ex)) return;
            SetStatus($"Error al borrar: {ex.Message}");
        }
    }

    private void OnContextMenuOpening(object sender, object e)
    {
        var settings = App.State.Settings;
        foreach (var entry in ItemContextMenu.Items)
        {
            if (entry is MenuFlyoutItem mfi && mfi.Tag is string key)
                mfi.Visibility = (settings?.IsActionVisible(key) ?? true)
                    ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void OnArchiveClick(object sender, RoutedEventArgs e)
    {

        if (Selected is not { } vm) return;
        var item = vm.Source;
        try
        {
            var result = await App.State.ArchiveItemAsync(item.Id, true);
            if (result == AppState.OpResult.QueuedOffline)
            {
                RefreshVisible();
                SetStatus($"Sin conexión, archivado encolado: {item.Title}.");
                return;
            }
            _lastUndo = new UndoAction($"archivar \"{item.Title}\"",
                () => App.State.Repo.ArchiveAsync(item.Id, false));
            RefreshVisible();
            SetStatus($"Archivado: {item.Title}. Ctrl+Z para deshacer.");
        }
        catch (Exception ex) { SetStatus($"Error al archivar: {ex.Message}"); }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } vm) OpenItem(vm.Source);
    }

    private void OpenItem(Item item)
    {
        try { ItemActions.Open(item); SetStatus($"Abierto: {item.Title}"); BumpUsage(item); }
        catch (Exception ex) { SetStatus($"No se pudo abrir: {ex.Message}"); }
    }

    private void BumpUsage(Item item)
    {
        item.UsageCount++;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try { await App.State.Repo.BumpUsageAsync(item.Id); }
            catch (Exception ex) { Diagnostics.Log("bump-usage", ex); }
        });
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } vm)
        {
            try { ItemActions.Copy(vm.Value); SetStatus($"Copiado: {vm.Title}"); BumpUsage(vm.Source); }
            catch (Exception ex) { SetStatus($"Error al copiar: {ex.Message}"); }
        }
    }

    private void OnShareClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } vm) return;
        try
        {
            ItemActions.Share(((App)Application.Current).Window!, vm.Source);
            SetStatus($"Panel de compartir lanzado para {vm.Title}.");
        }
        catch (Exception ex) { SetStatus($"Share UI falló: {ex.Message}"); }
    }

    private async void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } vm) return;
        try
        {
            var target = !vm.IsFavorite;
            var result = await App.State.SetFavoriteAsync(vm.Id, target);
            vm.Source.IsFavorite = target;
            if (result == AppState.OpResult.QueuedOffline)
            {
                RefreshVisible();
                SetStatus(target
                    ? $"Sin conexión, favorito encolado: {vm.Source.Title}."
                    : $"Sin conexión, quitar favorito encolado: {vm.Source.Title}.");
                return;
            }
            RefreshVisible();
            SetStatus(target ? $"Favorito: {vm.Source.Title}" : $"Quitado de favoritos: {vm.Source.Title}");
        }
        catch (Exception ex) { SetStatus($"Error con favorito: {ex.Message}"); }
    }

    private async void OnEditTagsClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } vm) return;
        try
        {
            var current = await App.State.Repo.GetItemTagIdsAsync(vm.Id);
            var dlg = new ItemTagsDialog(App.State.Repo, vm.Id, vm.Title, App.State.Tags, current)
            {
                XamlRoot = XamlRoot,
            };
            if (await dlg.ShowGuardedAsync() == ContentDialogResult.Primary)
            {
                await App.State.ReloadTagsAsync();
                RefreshVisible();
                SetStatus("Etiquetas actualizadas.");
            }
        }
        catch (Exception ex) { SetStatus($"Error con etiquetas: {ex.Message}"); }
    }

    private async void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } vm) return;
        if (App.State.Settings is not null && !App.State.Settings.HistoryEnabled)
        {
            await DialogHelper.InfoAsync(XamlRoot, "Historial desactivado",
                "Actívalo en Ajustes si quieres ver eventos.");
            return;
        }
        try
        {
            var entries = await App.State.Repo.ListHistoryAsync(vm.Id);
            var dlg = new HistoryDialog(vm.Title, entries) { XamlRoot = XamlRoot };
            await dlg.ShowGuardedAsync();
        }
        catch (Exception ex) { SetStatus($"Error cargando historial: {ex.Message}"); }
    }

    private async void OnShareLinkClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } vm) return;
        var dlg = new ShareLinkDialog(App.State.Repo, vm.Id, vm.Title) { XamlRoot = XamlRoot };
        await dlg.ShowGuardedAsync();
    }

    private async void OnShareCollectionClick(object sender, RoutedEventArgs e)
    {
        var ids = Visible.Select(v => v.Id).ToList();
        if (ids.Count == 0)
        {
            SetStatus("No hay enlaces visibles para incluir. Ajusta los filtros.");
            return;
        }
        var dlg = new ShareCollectionDialog(App.State.Repo, ids) { XamlRoot = XamlRoot };
        await dlg.ShowGuardedAsync();
    }

    private async void OnEmailClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } vm) return;
        var dlg = new EmailShareDialog(vm.Id, vm.Title) { XamlRoot = XamlRoot };
        await dlg.ShowGuardedAsync();
    }

    private async void OnArchivedClick(object sender, RoutedEventArgs e)
    {
        var dlg = new ArchivedDialog(App.State.Repo) { XamlRoot = XamlRoot };
        await dlg.ShowGuardedAsync();
        if (dlg.Changed)
        {
            try { await App.State.ReloadItemsAsync(); RefreshVisible(); }
            catch (Exception ex) { SetStatus($"Error recargando: {ex.Message}"); }
        }
    }

    private async void OnManageSharedClick(object sender, RoutedEventArgs e)
    {
        var titles = App.State.Items.ToDictionary(i => i.Id, i => i.Title);
        var dlg = new SharedDialog(App.State.Repo, titles) { XamlRoot = XamlRoot };
        await dlg.ShowGuardedAsync();
    }

    private async void OnManageCategoriesClick(object sender, RoutedEventArgs e)
    {
        var dlg = new ManageListDialog(
            "Categorías",
            async ct => (await App.State.Repo.ListCategoriesAsync(ct)).Select(c => new ListEntry(c.Id, c.Name)).ToList(),
            async (name, ct) => { await App.State.Repo.AddCategoryAsync(name, ct); },
            (id, name, ct) => App.State.Repo.RenameCategoryAsync(id, name, ct),
            (id, ct) => App.State.Repo.DeleteCategoryAsync(id, ct))
        {
            XamlRoot = XamlRoot,
        };
        await dlg.ShowGuardedAsync();
        await App.State.ReloadAllAsync();
        RefreshVisible();
    }

    private async void OnManageTagsClick(object sender, RoutedEventArgs e)
    {
        var dlg = new ManageListDialog(
            "Etiquetas",
            async ct => (await App.State.Repo.ListTagsAsync(ct)).Select(t => new ListEntry(t.Id, t.Name)).ToList(),
            async (name, ct) => { await App.State.Repo.AddTagAsync(name, ct); },
            (id, name, ct) => App.State.Repo.RenameTagAsync(id, name, ct),
            (id, ct) => App.State.Repo.DeleteTagAsync(id, ct))
        {
            XamlRoot = XamlRoot,
        };
        await dlg.ShowGuardedAsync();
        await App.State.ReloadAllAsync();
        RefreshVisible();
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(((App)Application.Current).Window!));
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".db");
        picker.FileTypeFilter.Add(".sqlite");
        picker.FileTypeFilter.Add(".sqlite3");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        SetStatus("Importando…");
        try
        {
            var result = await ImportExportService.ImportFileAsync(App.State, file.Path);
            await App.State.ReloadAllAsync();
            RefreshVisible();
            SetStatus($"Importación: {result.Inserted} nuevos, {result.Skipped} omitidos.");
        }
        catch (Exception ex) { SetStatus($"Error importando: {ex.Message}"); }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(((App)Application.Current).Window!));
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        picker.SuggestedFileName = $"linkmanager-export-{DateTime.Now:yyyyMMdd}";
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            await ImportExportService.ExportJsonAsync(App.State, file.Path);
            SetStatus($"Exportado a {file.Path}");
        }
        catch (Exception ex) { SetStatus($"Error exportando: {ex.Message}"); }
    }

    private async void OnCheckLinksClick(object sender, RoutedEventArgs e)
    {
        var token = App.State.Auth.CurrentSession?.AccessToken;
        if (string.IsNullOrEmpty(token)) { SetStatus("Sesión no válida para comprobar enlaces."); return; }
        SetStatus("Comprobando enlaces…");
        try
        {
            var summary = await WebApi.CheckAllLinksAsync(token);
            if (summary is null) { SetStatus("No se pudieron comprobar los enlaces."); return; }
            await App.State.ReloadItemsAsync();
            RefreshVisible();
            var s = summary.Value;
            SetStatus(s.Pending > 0
                ? $"Comprobados {s.Checked}: {s.Broken} rotos. Quedan {s.Pending}, vuelve a comprobar."
                : $"Comprobados {s.Checked}: {s.Broken} rotos, {s.Ok} correctos.");
        }
        catch (Exception ex) { SetStatus($"Error comprobando enlaces: {ex.Message}"); }
    }

    private async void OnCheckLinkClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } vm) return;
        var token = App.State.Auth.CurrentSession?.AccessToken;
        if (string.IsNullOrEmpty(token)) { SetStatus("Sesión no válida para comprobar enlaces."); return; }
        SetStatus($"Comprobando: {vm.Title}…");
        try
        {
            var status = await WebApi.CheckLinkAsync(vm.Id, token);
            if (status is null) { SetStatus("No se pudo comprobar el enlace."); return; }
            await App.State.ReloadItemsAsync();
            RefreshVisible();
            SetStatus(status switch
            {
                "broken" => $"Roto: {vm.Title}",
                "ok" => $"Correcto: {vm.Title}",
                _ => $"Sin determinar: {vm.Title}",
            });
        }
        catch (Exception ex) { SetStatus($"Error comprobando enlace: {ex.Message}"); }
    }

    private async void OnReloadClick(object sender, RoutedEventArgs e)
    {
        SetStatus("Recargando…");
        try
        {
            await App.State.ReloadAllAsync();
            RefreshVisible();
            SetStatus($"{App.State.Items.Count} elementos.");
        }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e) =>
        ((App)Application.Current).Window!.RootFrame.Navigate(typeof(SettingsPage));

    private void OnOpenWebClick(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        { FileName = "https://lm.aelbak.dev/app", UseShellExecute = true }); }
        catch (Exception ex) { SetStatus($"No se pudo abrir el navegador: {ex.Message}"); }
    }

    private async void OnHelpClick(object sender, RoutedEventArgs e) =>
        await DialogHelper.InfoAsync(XamlRoot, Help.Title, Help.Body);

    private async void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        _lastUndo = null;
        RealtimeSync.Stop();
        App.State.ClearLocalCacheForCurrentUser();
        try { await App.State.Auth.SignOutAsync(); }
        catch (Exception ex) { Diagnostics.Log("sign-out", ex); }
        App.State.Clear();
        var w = ((App)Application.Current).Window!;
        w.UninstallSystemHooks();
        w.NavigateTo(typeof(LoginPage));
    }
}
