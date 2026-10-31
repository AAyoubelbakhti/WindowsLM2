using System.Collections.Generic;
using LinkManager2.Data;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

public sealed partial class FiltersDialog : ContentDialog
{
    public FilterState Result { get; private set; }

    public FiltersDialog(
        FilterState current,
        IReadOnlyList<Category> categories,
        IReadOnlyList<Tag> tags
    )
    {
        InitializeComponent();
        Result = current.Clone();

        TypeCombo.Items.Add(new Choice<FilterType>(FilterType.All, Strings.Get("Filters_TypeAll")));
        TypeCombo.Items.Add(
            new Choice<FilterType>(FilterType.Url, Strings.Get("Filters_TypeLinks"))
        );
        TypeCombo.Items.Add(
            new Choice<FilterType>(FilterType.Path, Strings.Get("Filters_TypePaths"))
        );
        TypeCombo.SelectedIndex = (int)current.Type;

        CategoryCombo.Items.Add(new IdChoice(null, Strings.Get("Filters_AllFeminine")));
        foreach (var c in categories)
            CategoryCombo.Items.Add(new IdChoice(c.Id, c.Name));
        SelectId(CategoryCombo, current.CategoryId);

        TagCombo.Items.Add(new IdChoice(null, Strings.Get("Filters_AllFeminine")));
        foreach (var t in tags)
            TagCombo.Items.Add(new IdChoice(t.Id, t.Name));
        SelectId(TagCombo, current.TagId);

        FavoritesOnly.IsOn = current.FavoritesOnly;
        BrokenOnly.IsOn = current.BrokenOnly;

        SortCombo.Items.Add(
            new Choice<SortKey>(SortKey.AlphaAsc, Strings.Get("Filters_SortAlphaAsc"))
        );
        SortCombo.Items.Add(
            new Choice<SortKey>(SortKey.AlphaDesc, Strings.Get("Filters_SortAlphaDesc"))
        );
        SortCombo.Items.Add(
            new Choice<SortKey>(SortKey.DateDesc, Strings.Get("Filters_SortDateDesc"))
        );
        SortCombo.Items.Add(
            new Choice<SortKey>(SortKey.DateAsc, Strings.Get("Filters_SortDateAsc"))
        );
        SortCombo.Items.Add(
            new Choice<SortKey>(SortKey.UsageDesc, Strings.Get("Filters_SortUsageDesc"))
        );
        SortCombo.Items.Add(
            new Choice<SortKey>(SortKey.CategoryAsc, Strings.Get("Filters_SortCategoryAsc"))
        );
        for (var i = 0; i < SortCombo.Items.Count; i++)
        {
            if (((Choice<SortKey>)SortCombo.Items[i]!).Value == current.Sort)
            {
                SortCombo.SelectedIndex = i;
                break;
            }
        }
        if (SortCombo.SelectedIndex < 0)
            SortCombo.SelectedIndex = 0;
    }

    private void OnApply(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Result.Type = ((Choice<FilterType>)TypeCombo.SelectedItem!).Value;
        Result.CategoryId = ((IdChoice)CategoryCombo.SelectedItem!).Id;
        Result.TagId = ((IdChoice)TagCombo.SelectedItem!).Id;
        Result.FavoritesOnly = FavoritesOnly.IsOn;
        Result.BrokenOnly = BrokenOnly.IsOn;
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
            if (((IdChoice)combo.Items[i]!).Id == id)
            {
                combo.SelectedIndex = i;
                return;
            }
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
