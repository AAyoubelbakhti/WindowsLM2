using System;
using System.Collections.Generic;
using System.Linq;
using LinkManager2.Data;
using LinkManager2.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LinkManager2;

/// <summary>List selection, keyboard/pointer handling and the bulk-actions bar.</summary>
public sealed partial class MainPage : Page
{
    private ItemViewModel? Selected => ItemsList.SelectedItem as ItemViewModel;

    private List<ItemViewModel> SelectedItems =>
        ItemsList.SelectedItems.OfType<ItemViewModel>().ToList();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailsText.Text = Selected?.Details ?? string.Empty;
        FavoriteMenuItem.Text = Selected?.IsFavorite == true ? "Quitar de favoritos" : "Marcar como favorito";
        UpdateBulkBar();
    }

    /// <summary>
    /// Shows or hides the bulk-actions bar and announces via the status live region when
    /// multi-selection starts or ends, so screen reader users know the bar is reachable.
    /// </summary>
    private void UpdateBulkBar()
    {
        var count = ItemsList.SelectedItems.Count;
        if (count > 1)
        {
            BulkBar.Visibility = Visibility.Visible;
            BulkCountText.Text = $"{count} seleccionados";
            if (_lastSelectionCount <= 1)
                SetStatus($"{count} elementos seleccionados. Barra de acciones disponible.");
        }
        else
        {
            BulkBar.Visibility = Visibility.Collapsed;
            if (_lastSelectionCount > 1)
                SetStatus("Barra de acciones oculta.");
        }
        _lastSelectionCount = count;
    }

    private void OnBulkOpenClick(object sender, RoutedEventArgs e)
    {
        var items = SelectedItems;
        var opened = 0;
        foreach (var vm in items)
        {
            try { ItemActions.Open(vm.Source); BumpUsage(vm.Source); opened++; }
            catch (Exception ex) { Diagnostics.Log("bulk-open", ex); }
        }
        SetStatus($"Abiertos {opened} de {items.Count}.");
    }

    private void OnBulkCopyClick(object sender, RoutedEventArgs e)
    {
        var items = SelectedItems;
        if (items.Count == 0) return;
        try
        {
            ItemActions.Copy(string.Join(Environment.NewLine, items.Select(i => i.Value)));
            SetStatus($"Copiadas {items.Count} URL.");
        }
        catch (Exception ex) { SetStatus($"Error al copiar: {ex.Message}"); }
    }

    private async void OnBulkCollectionClick(object sender, RoutedEventArgs e)
    {
        var ids = SelectedItems.Select(v => v.Id).ToList();
        if (ids.Count == 0) return;
        var dlg = new ShareCollectionDialog(App.State.Repo, ids) { XamlRoot = XamlRoot };
        await dlg.ShowGuardedAsync();
    }

    private async void OnBulkDeleteClick(object sender, RoutedEventArgs e)
    {
        var items = SelectedItems;
        if (items.Count == 0) return;
        var ok = await DialogHelper.ConfirmAsync(XamlRoot,
            "Confirmar borrado múltiple",
            $"¿Borrar {items.Count} elementos? Podrás deshacerlo con Ctrl+Z.",
            "Borrar", "Cancelar");
        if (!ok) return;

        var deletedItems = new List<Item>();
        foreach (var vm in items)
        {
            try { await App.State.Repo.DeleteAsync(vm.Id); deletedItems.Add(vm.Source); }
            catch (Exception ex) { Diagnostics.Log("bulk-delete", ex); }
        }
        if (deletedItems.Count > 0)
        {
            _lastUndo = new UndoAction(
                deletedItems.Count == 1
                    ? $"borrar \"{deletedItems[0].Title}\""
                    : $"borrar {deletedItems.Count} elementos",
                async () =>
                {
                    foreach (var it in deletedItems)
                        await App.State.Repo.RestoreAsync(it);
                });
        }
        try { await App.State.ReloadItemsAsync(); RefreshVisible(); }
        catch (Exception ex) { Diagnostics.Log("bulk-delete reload", ex); }
        SetStatus($"Borrados {deletedItems.Count} de {items.Count}. Ctrl+Z para deshacer.");
    }

    private static bool IsDown(Windows.System.VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = IsDown(Windows.System.VirtualKey.Control);
        var alt = IsDown(Windows.System.VirtualKey.Menu);

        if (ctrl && !alt)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.C when Selected is not null:
                    OnCopyClick(sender, e); e.Handled = true; return;
                case Windows.System.VirtualKey.E when Selected is not null:
                    _ = AddOrEditAsync(Selected.Source); e.Handled = true; return;
            }
            return;
        }

        if (ctrl || alt) return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter when Selected is not null:
                OpenItem(Selected.Source); e.Handled = true; break;
            case Windows.System.VirtualKey.Delete when Selected is not null:
                _ = DeleteWithConfirmAsync(Selected.Source); e.Handled = true; break;
            default:
                if (TypeAhead.TrySelect(e.Key)) e.Handled = true;
                break;
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
}
