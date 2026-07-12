using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using LinkManager2.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

public sealed partial class AddEditDialog : ContentDialog
{
    public string ItemTitle => TitleBox.Text.Trim();
    public string ItemValue => ValueBox.Text.Trim();
    public string ItemDescription => DescriptionBox.Text.Trim();
    public string? ItemCategoryId => (CategoryCombo.SelectedItem as CategoryOption)?.Id;

    public IReadOnlyList<string> SelectedTagIds =>
        TagsList.SelectedItems.OfType<TagEntry>().Select(t => t.Id).ToList();

    private readonly ItemsRepository? _repo;
    private readonly ObservableCollection<TagEntry> _tagEntries = new();

    public AddEditDialog(IReadOnlyList<Category> categories, Item? initial,
        string? prefillTitle = null, string? prefillValue = null,
        IReadOnlyList<Tag>? allTags = null, ItemsRepository? repo = null, bool showQuickCreate = false)
    {
        InitializeComponent();
        Title = initial is null ? "Añadir elemento" : "Editar elemento";
        _repo = repo;

        TitleBox.Text = initial?.Title ?? prefillTitle ?? string.Empty;
        ValueBox.Text = initial?.Value ?? prefillValue ?? string.Empty;
        DescriptionBox.Text = initial?.Description ?? string.Empty;

        CategoryCombo.Items.Add(new CategoryOption(null, "Sin categoría"));
        foreach (var c in categories) CategoryCombo.Items.Add(new CategoryOption(c.Id, c.Name));
        CategoryCombo.SelectedIndex = initial?.CategoryId is null
            ? 0
            : Math.Max(0, CategoryCombo.Items.Cast<CategoryOption>().ToList().FindIndex(o => o.Id == initial.CategoryId));

        if (showQuickCreate && _repo is not null)
        {
            QuickCreatePanel.Visibility = Visibility.Visible;
            TagsList.ItemsSource = _tagEntries;
            foreach (var t in allTags ?? Array.Empty<Tag>())
                _tagEntries.Add(new TagEntry(t.Id, t.Name));
        }

        Loaded += (_, _) => TitleBox.Focus(FocusState.Programmatic);
    }

    private async void OnCreateCategory(object sender, RoutedEventArgs e)
    {
        if (_repo is null) return;
        var name = NewCategoryBox.Text.Trim();
        if (name.Length == 0) return;
        try
        {
            var created = await _repo.AddCategoryAsync(name);
            var opt = new CategoryOption(created.Id, created.Name);
            CategoryCombo.Items.Add(opt);
            CategoryCombo.SelectedItem = opt;
            NewCategoryBox.Text = string.Empty;
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex) { ShowError($"No se pudo crear la categoría: {ex.Message}"); }
    }

    private async void OnCreateTag(object sender, RoutedEventArgs e)
    {
        if (_repo is null) return;
        var name = NewTagBox.Text.Trim();
        if (name.Length == 0) return;
        try
        {
            var created = await _repo.AddTagAsync(name);
            var entry = new TagEntry(created.Id, created.Name);
            _tagEntries.Add(entry);
            TagsList.SelectedItems.Add(entry);
            NewTagBox.Text = string.Empty;
            ErrorBar.IsOpen = false;
        }
        catch (Exception ex) { ShowError($"No se pudo crear la etiqueta: {ex.Message}"); }
    }

    private async void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var def = args.GetDeferral();
        try
        {
            var (ok, _) = Validation.DetectType(ItemValue);
            if (!ok) { ShowError("El valor no es una URL ni una ruta válida."); ValueBox.Focus(FocusState.Programmatic); return; }

            if (string.IsNullOrWhiteSpace(ItemTitle))
            {
                ShowError("Buscando título…");
                await TryAutofillTitleAsync();
                if (string.IsNullOrWhiteSpace(ItemTitle))
                {
                    ShowError("Escribe un título o usa una URL con OpenGraph.");
                    TitleBox.Focus(FocusState.Programmatic);
                    return;
                }
                ShowError("Título sugerido. Revisa y pulsa Guardar de nuevo.");
                TitleBox.Focus(FocusState.Programmatic);
                TitleBox.SelectAll();
                return;
            }

            args.Cancel = false;
        }
        finally { def.Complete(); }
    }

    private async void OnValueLostFocus(object sender, RoutedEventArgs e) => await TryAutofillTitleAsync();

    private async Task TryAutofillTitleAsync()
    {
        if (!string.IsNullOrWhiteSpace(TitleBox.Text)) return;
        var url = ValueBox.Text.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var meta = await MetadataFetcher.FetchAsync(url);
            if (!string.IsNullOrWhiteSpace(meta?.Title) && string.IsNullOrWhiteSpace(TitleBox.Text))
                TitleBox.Text = meta!.Title;
        }
        catch (Exception ex) { Diagnostics.Log("autofill title", ex); }
    }

    private void ShowError(string msg)
    {
        ErrorBar.Title = msg;
        ErrorBar.IsOpen = true;
    }

    private sealed record CategoryOption(string? Id, string Name)
    {
        public override string ToString() => Name;
    }

    public sealed record TagEntry(string Id, string Name);
}
