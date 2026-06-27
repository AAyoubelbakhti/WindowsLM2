using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LinkManager2.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

public sealed partial class ItemTagsDialog : ContentDialog
{
    private readonly ItemsRepository _repo;
    private readonly string _itemId;
    private readonly ObservableCollection<TagEntry> _tags = new();

    public ItemTagsDialog(ItemsRepository repo, string itemId, string itemTitle,
        IReadOnlyList<Tag> allTags, IReadOnlyList<string> currentTagIds)
    {
        InitializeComponent();
        _repo = repo; _itemId = itemId;
        ItemLabel.Text = $"Etiquetas para: {itemTitle}";

        TagsList.ItemsSource = _tags;
        var current = new HashSet<string>(currentTagIds);
        foreach (var t in allTags)
        {
            var entry = new TagEntry(t.Id, t.Name);
            _tags.Add(entry);
        }
        Loaded += (_, _) =>
        {
            foreach (var t in _tags.Where(t => current.Contains(t.Id)))
                TagsList.SelectedItems.Add(t);
        };
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        var name = NewTagBox.Text.Trim();
        if (name.Length == 0) return;
        try
        {
            var created = await _repo.AddTagAsync(name);
            var entry = new TagEntry(created.Id, created.Name);
            _tags.Add(entry);
            TagsList.SelectedItems.Add(entry);
            NewTagBox.Text = string.Empty;
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex) { ShowError($"No se pudo crear: {ex.Message}"); }
    }

    private async void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var def = args.GetDeferral();
        try
        {
            var ids = TagsList.SelectedItems.OfType<TagEntry>().Select(t => t.Id).ToList();
            await _repo.SetItemTagsAsync(_itemId, ids);
            args.Cancel = false;
        }
        catch (Exception ex) { ShowError($"Error al guardar: {ex.Message}"); }
        finally { def.Complete(); }
    }

    private void ShowError(string msg) { ErrorBar.Title = msg; ErrorBar.IsOpen = true; }

    public sealed record TagEntry(string Id, string Name);
}
