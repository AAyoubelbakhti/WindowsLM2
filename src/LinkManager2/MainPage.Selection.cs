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
        UpdateSelectionDetails();
        UpdateBulkBar();
    }

    /// <summary>Shows the selected row's details and words the favorite command for it; also run after a refresh that kept the same row selected.</summary>
    private void UpdateSelectionDetails()
    {
        DetailsText.Text = Selected?.Details ?? string.Empty;
        FavoriteMenuItem.Text =
            Selected?.IsFavorite == true
                ? Strings.Get("Main_FavoriteRemove")
                : Strings.Get("Main_FavoriteAdd");
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
            BulkCountText.Text = Strings.Format("Main_BulkSelectedCount", count);
            if (_lastSelectionCount <= 1)
                SetStatus(Strings.Format("Main_BulkSelectedAnnounce", count));
        }
        else
        {
            BulkBar.Visibility = Visibility.Collapsed;
            if (_lastSelectionCount > 1)
                SetStatus(Strings.Get("Main_BulkBarHidden"));
        }
        _lastSelectionCount = count;
    }

    private void OnBulkOpenClick(object sender, RoutedEventArgs e)
    {
        var items = SelectedItems;
        var opened = 0;
        foreach (var vm in items)
        {
            try
            {
                ItemActions.Open(vm.Source);
                BumpUsage(vm.Source);
                opened++;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("bulk-open", ex);
            }
        }
        SetStatus(Strings.Format("Main_BulkOpened", opened, items.Count));
    }

    private void OnBulkCopyClick(object sender, RoutedEventArgs e)
    {
        var items = SelectedItems;
        if (items.Count == 0)
            return;
        try
        {
            ItemActions.Copy(string.Join(Environment.NewLine, items.Select(i => i.Value)));
            SetStatus(Strings.Format("Main_BulkCopied", items.Count));
        }
        catch (Exception ex)
        {
            SetStatus(Strings.Format("Main_BulkCopyError", ex.Message));
        }
    }

    private async void OnBulkCollectionClick(object sender, RoutedEventArgs e)
    {
        var ids = SelectedItems.Select(v => v.Id).ToList();
        if (ids.Count == 0)
            return;
        var dlg = new ShareCollectionDialog(App.State.Repo, ids) { XamlRoot = XamlRoot };
        await dlg.ShowGuardedAsync();
    }

    private async void OnBulkDeleteClick(object sender, RoutedEventArgs e)
    {
        var items = SelectedItems;
        if (items.Count == 0)
            return;
        var ok = await DialogHelper.ConfirmAsync(
            XamlRoot,
            Strings.Get("Main_BulkDeleteTitle"),
            Strings.Format("Main_BulkDeleteConfirm", items.Count),
            Strings.Get("Main_BulkDeleteButton"),
            Strings.Get("Common_Cancel")
        );
        if (!ok)
            return;

        var deletedItems = new List<Item>();
        var queuedCount = 0;
        foreach (var vm in items)
        {
            try
            {
                var result = await App.State.DeleteItemAsync(vm.Id);
                if (result == AppState.OpResult.QueuedOffline)
                    queuedCount++;
                else
                    deletedItems.Add(vm.Source);
            }
            catch (Exception ex)
            {
                Diagnostics.Log("bulk-delete", ex);
            }
        }
        if (deletedItems.Count > 0)
        {
            _lastUndo = new UndoAction(
                deletedItems.Count == 1
                    ? Strings.Format("Main_UndoDeleteOne", deletedItems[0].Title)
                    : Strings.Format("Main_UndoDeleteMany", deletedItems.Count),
                async () =>
                {
                    foreach (var it in deletedItems)
                        await App.State.Repo.RestoreAsync(it);
                }
            );
        }
        RefreshVisible();
        if (queuedCount > 0)
            SetStatus(
                deletedItems.Count > 0
                    ? Strings.Format(
                        "Main_BulkDeletedQueued",
                        deletedItems.Count,
                        queuedCount,
                        items.Count
                    )
                    : Strings.Format("Main_BulkQueuedOnly", queuedCount, items.Count)
            );
        else
            SetStatus(Strings.Format("Main_BulkDeleted", deletedItems.Count, items.Count));
    }

    private static bool IsDown(Windows.System.VirtualKey key) =>
        Microsoft
            .UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
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
                    OnCopyClick(sender, e);
                    e.Handled = true;
                    return;
                case Windows.System.VirtualKey.E when Selected is not null:
                    _ = AddOrEditAsync(Selected.Source);
                    e.Handled = true;
                    return;
            }
            return;
        }

        if (ctrl || alt)
            return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Enter when Selected is not null:
                OpenItem(Selected.Source);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Delete when Selected is not null:
                _ = DeleteWithConfirmAsync(Selected.Source);
                e.Handled = true;
                break;
            default:
                if (TypeAhead.TrySelect(e.Key))
                    e.Handled = true;
                break;
        }
    }

    private void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Selected is { } vm)
            OpenItem(vm.Source);
    }

    private void OnListRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ItemViewModel vm)
            ItemsList.SelectedItem = vm;
    }
}
