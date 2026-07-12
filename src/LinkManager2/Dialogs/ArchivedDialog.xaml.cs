using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using LinkManager2.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

/// <summary>
/// Lists archived items and lets the user restore them (un-archive) or delete them
/// permanently. Closes the soft-delete loop that ArchiveAsync already supports.
/// </summary>
public sealed partial class ArchivedDialog : ContentDialog
{
    private readonly ItemsRepository _repo;
    private readonly ObservableCollection<Item> _items = new();

    public bool Changed { get; private set; }

    public ArchivedDialog(ItemsRepository repo)
    {
        InitializeComponent();
        _repo = repo;
        ItemsList.ItemsSource = _items;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private Item? Selected => ItemsList.SelectedItem as Item;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var on = Selected is not null;
        RestoreButton.IsEnabled = on;
        DeleteButton.IsEnabled = on;
        DeleteConfirmText.Text = Selected is null
            ? string.Empty
            : $"¿Borrar definitivamente \"{Selected.Title}\"? No se puede deshacer.";
    }

    private async Task ReloadAsync()
    {
        try
        {
            var entries = await _repo.ListArchivedAsync();
            _items.Clear();
            foreach (var i in entries) _items.Add(i);
            ItemsList.SelectedItem = null;
            Status(InfoBarSeverity.Informational,
                entries.Count == 1 ? "1 archivado." : $"{entries.Count} archivados.");
        }
        catch (Exception ex) { Status(InfoBarSeverity.Error, ex.Message); }
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        try
        {
            await _repo.ArchiveAsync(item.Id, false);
            Changed = true;
            Status(InfoBarSeverity.Success, $"Restaurado: {item.Title}");
            await ReloadAsync();
        }
        catch (Exception ex) { Status(InfoBarSeverity.Error, ex.Message); }
    }

    private async void OnDeleteConfirmClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        DeleteFlyout.Hide();
        try
        {
            await _repo.DeleteAsync(item.Id);
            Changed = true;
            Status(InfoBarSeverity.Success, $"Borrado: {item.Title}");
            await ReloadAsync();
        }
        catch (Exception ex) { Status(InfoBarSeverity.Error, ex.Message); }
    }

    private void Status(InfoBarSeverity severity, string msg)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = msg;
        StatusBar.IsOpen = true;
    }
}
