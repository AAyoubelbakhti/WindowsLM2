using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using LinkManager2.Data;
using LinkManager2.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LinkManager2;

public sealed partial class MainPage : Page
{
    public ObservableCollection<ItemViewModel> Visible { get; } = new();

    private FilterState _filters = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _searchDebounce;
    private bool _loading;

    public MainPage()
    {
        InitializeComponent();
        _searchDebounce = DispatcherQueue.CreateTimer();
        _searchDebounce.Interval = TimeSpan.FromMilliseconds(160);
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            _filters.Search = SearchBox.Text;
            RefreshVisible();
        };
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!App.SupabaseReady || !App.State.Auth.IsAuthenticated)
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
                DispatcherQueue.TryEnqueue(async () =>
                {

                    try
                    {
                        await App.State.FlushPendingAsync();
                        await App.State.ReloadAllAsync();
                        RefreshVisible();
                    }
                    catch (Exception ex) { Diagnostics.Log("realtime-reconcile", ex); }
                });
                return System.Threading.Tasks.Task.CompletedTask;
            });

            _ = CheckAppConfigAsync();
            _ = CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            _loading = false;
            UpdateEmptyState();
            SetStatus($"Sin conexión: {ex.Message}. Mostrando caché.");
        }
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
            UpdateService.ApplyAndRestart();
    }

    private async Task CheckAppConfigAsync()
    {
        var gate = await VersionGate.CheckAsync(App.Build);
        if (!gate.UpdateRequired) return;

        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Actualización necesaria",
            Content = gate.Message ?? "Hay una versión nueva. Actualiza la app para continuar.",
            PrimaryButtonText = "Cerrar sesión",
        };
        await dlg.ShowAsync();
        try { await App.State.Auth.SignOutAsync(); }
        catch (Exception ex) { Diagnostics.Log("version-gate sign-out", ex); }
        ((App)Application.Current).Window!.NavigateTo(typeof(LoginPage));
    }

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {

        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        UpdateSuggestions(sender.Text);
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is string title)
        {
            sender.Text = title;
            _filters.Search = title;
            RefreshVisible();
        }
    }

    private void UpdateSuggestions(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) { SearchBox.ItemsSource = null; return; }
        SearchBox.ItemsSource = App.State.Items
            .Where(i => i.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Title)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private void OnFocusSearch(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Programmatic);
        args.Handled = true;
    }

    private async void OnFiltersClick(object sender, RoutedEventArgs e)
    {
        var dlg = new FiltersDialog(_filters, App.State.Categories, App.State.Tags)
        {
            XamlRoot = XamlRoot,
        };
        var result = await dlg.ShowGuardedAsync();
        if (result == ContentDialogResult.Primary || result == ContentDialogResult.Secondary)
        {
            _filters = dlg.Result;
            _filters.Search = SearchBox.Text;
            RefreshVisible();
        }
    }

    private void RefreshVisible()
    {
        var selectedId = (ItemsList.SelectedItem as ItemViewModel)?.Id;
        Visible.Clear();
        var byId = App.State.Categories.ToDictionary(c => c.Id, c => c.Name);
        foreach (var item in ApplyFilters(App.State.Items))
        {
            var catName = item.CategoryId is not null && byId.TryGetValue(item.CategoryId, out var n) ? n : null;
            Visible.Add(new ItemViewModel(item, catName));
        }

        if (selectedId is not null)
        {
            var match = Visible.FirstOrDefault(v => v.Id == selectedId);
            if (match is not null) ItemsList.SelectedItem = match;
        }
        UpdateFiltersStatus();
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var empty = Visible.Count == 0 && !_loading;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty) return;
        var filtering = _filters.ActiveCount > 0 || !string.IsNullOrWhiteSpace(_filters.Search);
        EmptyStateText.Text = filtering
            ? "Ningún resultado para la búsqueda o los filtros actuales."
            : "Aún no tienes enlaces. Pulsa «Añadir enlace» para empezar.";
    }

    public async Task RefreshAfterExternalChangeAsync()
    {
        try { await App.State.ReloadItemsAsync(); RefreshVisible(); }
        catch (Exception ex) { SetStatus($"Error recargando: {ex.Message}"); }
    }

    public void SetExternalStatus(string text) => SetStatus(text);

    public void FocusSearch() => SearchBox.Focus(FocusState.Programmatic);

    private IEnumerable<Item> ApplyFilters(IEnumerable<Item> items)
    {
        var q = items;
        if (!string.IsNullOrWhiteSpace(_filters.Search))
        {
            var s = _filters.Search.Trim();
            q = q.Where(i => i.Title.Contains(s, StringComparison.OrdinalIgnoreCase));
        }
        if (_filters.Type != FilterType.All)
        {
            var t = _filters.Type == FilterType.Url ? ItemTypes.Url : ItemTypes.Path;
            q = q.Where(i => i.Type == t);
        }
        if (_filters.CategoryId is not null) q = q.Where(i => i.CategoryId == _filters.CategoryId);
        if (_filters.TagId is not null) q = q.Where(i => App.State.ItemHasTag(i.Id, _filters.TagId));
        if (_filters.FavoritesOnly) q = q.Where(i => i.IsFavorite);

        return _filters.Sort switch
        {
            SortKey.AlphaAsc => q.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase),
            SortKey.AlphaDesc => q.OrderByDescending(i => i.Title, StringComparer.OrdinalIgnoreCase),
            SortKey.DateDesc => q.OrderByDescending(i => i.CreatedAt),
            SortKey.DateAsc => q.OrderBy(i => i.CreatedAt),
            SortKey.UsageDesc => q.OrderByDescending(i => i.UsageCount),
            SortKey.CategoryAsc => q.OrderBy(i => i.CategoryId ?? string.Empty).ThenBy(i => i.Title),
            _ => q,
        };
    }

    private void UpdateFiltersStatus()
    {
        var count = _filters.ActiveCount;
        FiltersButton.Content = count > 0 ? $"Filtros ({count})" : "Filtros";

        if (count == 0 && _filters.Sort == SortKey.AlphaAsc) { FiltersStatus.Text = string.Empty; return; }

        var parts = new List<string>();
        if (_filters.Type != FilterType.All)
            parts.Add(_filters.Type == FilterType.Url ? "Tipo: enlaces" : "Tipo: rutas");
        if (_filters.CategoryId is not null)
            parts.Add($"Categoría: {App.State.Categories.FirstOrDefault(c => c.Id == _filters.CategoryId)?.Name ?? "?"}");
        if (_filters.TagId is not null)
            parts.Add($"Etiqueta: {App.State.Tags.FirstOrDefault(t => t.Id == _filters.TagId)?.Name ?? "?"}");
        if (_filters.FavoritesOnly) parts.Add("Solo favoritos");
        if (_filters.Sort != SortKey.AlphaAsc)
            parts.Add(_filters.Sort switch
            {
                SortKey.AlphaDesc => "Orden: Z–A",
                SortKey.DateDesc => "Orden: más recientes",
                SortKey.DateAsc => "Orden: más antiguos",
                SortKey.UsageDesc => "Orden: más usados",
                SortKey.CategoryAsc => "Orden: por categoría",
                _ => "",
            });
        FiltersStatus.Text = "Activos: " + string.Join(" · ", parts);
    }

    private ItemViewModel? Selected => ItemsList.SelectedItem as ItemViewModel;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailsText.Text = Selected?.Details ?? string.Empty;
        FavoriteMenuItem.Text = Selected?.IsFavorite == true ? "Quitar de favoritos" : "Marcar como favorito";
    }

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter when Selected is not null:
                OpenItem(Selected.Source); e.Handled = true; break;
            case Windows.System.VirtualKey.Delete when Selected is not null:
                _ = DeleteWithConfirmAsync(Selected.Source); e.Handled = true; break;
        }
    }

    private void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Selected is { } vm) OpenItem(vm.Source);
    }

    private void OnListRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ItemViewModel vm)
            ItemsList.SelectedItem = vm;
    }

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

        try
        {
            if (initial is null)
            {
                var result = await App.State.AddItemAsync(dlg.ItemTitle, value, type, dlg.ItemCategoryId, dlg.SelectedTagIds);
                SetStatus(result == AppState.AddResult.QueuedOffline
                    ? $"Sin conexión, encolado: {dlg.ItemTitle}."
                    : $"Añadido: {dlg.ItemTitle}");

                if (result == AppState.AddResult.Added)
                {
                    var added = System.Linq.Enumerable.FirstOrDefault(
                        App.State.Items, i => i.Value == value && i.Title == dlg.ItemTitle);
                    if (added is not null)
                    {
                        _ = System.Threading.Tasks.Task.Run(async () =>
                        {
                            await App.State.Repo.EnrichMetadataAsync(added);
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
                await App.State.Repo.UpdateAsync(initial.Id, dlg.ItemTitle, value, type, dlg.ItemCategoryId);
                await App.State.ReloadItemsAsync();
                SetStatus($"Actualizado: {dlg.ItemTitle}");
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
            await App.State.Repo.DeleteAsync(item.Id);
            await App.State.ReloadItemsAsync();
            RefreshVisible();
            SetStatus($"Borrado: {item.Title}");
        }
        catch (Exception ex) { SetStatus($"Error al borrar: {ex.Message}"); }
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
            await App.State.Repo.ArchiveAsync(item.Id, true);
            await App.State.ReloadItemsAsync();
            RefreshVisible();
            SetStatus($"Archivado: {item.Title}");
        }
        catch (Exception ex) { SetStatus($"Error al archivar: {ex.Message}"); }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } vm) OpenItem(vm.Source);
    }

    private void OpenItem(Item item)
    {
        try { ItemActions.Open(item); SetStatus($"Abierto: {item.Title}"); }
        catch (Exception ex) { SetStatus($"No se pudo abrir: {ex.Message}"); }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (Selected is { } vm)
        {
            try { ItemActions.Copy(vm.Value); SetStatus($"Copiado: {vm.Title}"); }
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
            await App.State.Repo.ToggleFavoriteAsync(vm.Id, !vm.IsFavorite);
            vm.Source.IsFavorite = !vm.IsFavorite;
            await App.State.ReloadItemsAsync();
            RefreshVisible();
            SetStatus(vm.Source.IsFavorite ? $"Favorito: {vm.Source.Title}" : $"Quitado de favoritos: {vm.Source.Title}");
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
            await App.State.ReloadItemsAsync();
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
        try { await App.State.Auth.SignOutAsync(); }
        catch (Exception ex) { Diagnostics.Log("sign-out", ex); }
        App.State.Clear();
        var w = ((App)Application.Current).Window!;
        w.UninstallSystemHooks();
        w.NavigateTo(typeof(LoginPage));
    }

    private async Task FlushPendingAsync()
    {
        try { await App.State.FlushPendingAsync(); RefreshVisible(); }
        catch (Exception ex) { Diagnostics.Log("flush-pending", ex); }
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;

        Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer
            .CreatePeerForElement(StatusText)
            ?.RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
}
