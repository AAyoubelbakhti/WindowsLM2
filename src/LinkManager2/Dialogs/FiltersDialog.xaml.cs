using System.Collections.Generic;
using LinkManager2.Data;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

public sealed partial class FiltersDialog : ContentDialog
{
    public FilterState Result { get; private set; }

    public FiltersDialog(FilterState current, IReadOnlyList<Category> categories, IReadOnlyList<Tag> tags)
    {
        InitializeComponent();
        Result = current.Clone();

        TypeCombo.Items.Add(new Choice<FilterType>(FilterType.All, "Todos"));
        TypeCombo.Items.Add(new Choice<FilterType>(FilterType.Url, "Solo enlaces"));
        TypeCombo.Items.Add(new Choice<FilterType>(FilterType.Path, "Solo rutas"));
        TypeCombo.SelectedIndex = (int)current.Type;

        CategoryCombo.Items.Add(new IdChoice(null, "Todas"));
        foreach (var c in categories) CategoryCombo.Items.Add(new IdChoice(c.Id, c.Name));
        SelectId(CategoryCombo, current.CategoryId);

        TagCombo.Items.Add(new IdChoice(null, "Todas"));
        foreach (var t in tags) TagCombo.Items.Add(new IdChoice(t.Id, t.Name));
        SelectId(TagCombo, current.TagId);

        FavoritesOnly.IsOn = current.FavoritesOnly;

        SortCombo.Items.Add(new Choice<SortKey>(SortKey.AlphaAsc, "Alfabéticamente (A–Z)"));
        SortCombo.Items.Add(new Choice<SortKey>(SortKey.AlphaDesc, "Alfabéticamente (Z–A)"));
        SortCombo.Items.Add(new Choice<SortKey>(SortKey.DateDesc, "Más recientes primero"));
        SortCombo.Items.Add(new Choice<SortKey>(SortKey.DateAsc, "Más antiguos primero"));
        SortCombo.Items.Add(new Choice<SortKey>(SortKey.UsageDesc, "Más usados"));
        SortCombo.Items.Add(new Choice<SortKey>(SortKey.CategoryAsc, "Por categoría"));
        for (var i = 0; i < SortCombo.Items.Count; i++)
        {
            if (((Choice<SortKey>)SortCombo.Items[i]!).Value == current.Sort)
            {
                SortCombo.SelectedIndex = i; break;
            }
        }
        if (SortCombo.SelectedIndex < 0) SortCombo.SelectedIndex = 0;
    }

    private void OnApply(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result.Type = ((Choice<FilterType>)TypeCombo.SelectedItem!).Value;
        Result.CategoryId = ((IdChoice)CategoryCombo.SelectedItem!).Id;
        Result.TagId = ((IdChoice)TagCombo.SelectedItem!).Id;
        Result.FavoritesOnly = FavoritesOnly.IsOn;
        Result.Sort = ((Choice<SortKey>)SortCombo.SelectedItem!).Value;
    }

    private void OnClear(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result = new FilterState();
    }

    private static void SelectId(ComboBox combo, string? id)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (((IdChoice)combo.Items[i]!).Id == id) { combo.SelectedIndex = i; return; }
        }
        combo.SelectedIndex = 0;
    }

    private sealed record Choice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record IdChoice(string? Id, string Name)
    {
        public override string ToString() => Name;
    }
}
