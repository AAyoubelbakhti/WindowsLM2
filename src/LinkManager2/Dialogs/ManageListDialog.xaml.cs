using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

public readonly record struct ListEntry(string Id, string Name);

public sealed partial class ManageListDialog : ContentDialog
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<ListEntry>>> _list;
    private readonly Func<string, CancellationToken, Task> _add;
    private readonly Func<string, string, CancellationToken, Task> _rename;
    private readonly Func<string, CancellationToken, Task> _delete;

    private readonly ObservableCollection<ListEntry> _items = new();

    public ManageListDialog(
        string title,
        Func<CancellationToken, Task<IReadOnlyList<ListEntry>>> list,
        Func<string, CancellationToken, Task> add,
        Func<string, string, CancellationToken, Task> rename,
        Func<string, CancellationToken, Task> delete)
    {
        InitializeComponent();
        Title = title;
        _list = list; _add = add; _rename = rename; _delete = delete;
        ItemsList.ItemsSource = _items;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private ListEntry? Selected => ItemsList.SelectedItem is ListEntry e ? e : null;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var entry = Selected;
        var on = entry is not null;
        RenameBox.IsEnabled = on;
        RenameButton.IsEnabled = on;
        DeleteButton.IsEnabled = on;
        RenameBox.Text = entry?.Name ?? string.Empty;
        DeleteConfirmText.Text = entry is null
            ? string.Empty
            : $"¿Borrar \"{entry.Value.Name}\"? Los elementos vinculados se desasocian.";
    }

    private async Task ReloadAsync()
    {
        try
        {
            var entries = await _list(CancellationToken.None);
            _items.Clear();
            foreach (var e in entries) _items.Add(e);
            ItemsList.SelectedItem = null;
            Status(InfoBarSeverity.Informational, $"{entries.Count} elementos.");
        }
        catch (Exception ex) { Status(InfoBarSeverity.Error, ex.Message); }
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var name = NewNameBox.Text.Trim();
        if (name.Length == 0) return;
        try { await _add(name, CancellationToken.None); NewNameBox.Text = ""; await ReloadAsync(); }
        catch (Exception ex) { Status(InfoBarSeverity.Error, ex.Message); }
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        var name = RenameBox.Text.Trim();
        if (name.Length == 0 || name == entry.Name) return;
        try { await _rename(entry.Id, name, CancellationToken.None); await ReloadAsync(); }
        catch (Exception ex) { Status(InfoBarSeverity.Error, ex.Message); }
    }

    private async void OnDeleteConfirmClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        DeleteFlyout.Hide();
        try { await _delete(entry.Id, CancellationToken.None); await ReloadAsync(); }
        catch (Exception ex) { Status(InfoBarSeverity.Error, ex.Message); }
    }

    private void Status(InfoBarSeverity severity, string msg)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = msg;
        StatusBar.IsOpen = true;
    }
}
