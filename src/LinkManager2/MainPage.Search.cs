using System;
using System.Collections.Generic;
using System.Linq;
using LinkManager2.Data;
using LinkManager2.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LinkManager2;

/// <summary>Search box, suggestions, filters dialog and the filters/results live regions.</summary>
public sealed partial class MainPage : Page
{
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
            AnnounceResultCount();
        }
    }

    private void AnnounceResultCount()
    {
        var n = Visible.Count;
        SetStatus(n == 1 ? "1 resultado" : $"{n} resultados");
    }

    private void UpdateFiltersStatus()
    {
        var count = _filters.ActiveCount;
        FiltersButton.Content = count > 0 ? $"Filtros ({count})" : "Filtros";

        if (count == 0 && _filters.Sort == SortKey.AlphaAsc) { SetFiltersStatusText(string.Empty); return; }

        var parts = new List<string>();
        if (_filters.Type != FilterType.All)
            parts.Add(_filters.Type == FilterType.Url ? "Tipo: enlaces" : "Tipo: rutas");
        if (_filters.CategoryId is not null)
            parts.Add($"Categoría: {App.State.Categories.FirstOrDefault(c => c.Id == _filters.CategoryId)?.Name ?? "?"}");
        if (_filters.TagId is not null)
            parts.Add($"Etiqueta: {App.State.Tags.FirstOrDefault(t => t.Id == _filters.TagId)?.Name ?? "?"}");
        if (_filters.FavoritesOnly) parts.Add("Solo favoritos");
        if (_filters.BrokenOnly) parts.Add("Solo rotos");
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
        SetFiltersStatusText("Activos: " + string.Join(" · ", parts));
    }

    /// <summary>Updates the filters live region and raises LiveRegionChanged only when the text actually changes.</summary>
    private void SetFiltersStatusText(string text)
    {
        if (FiltersStatus.Text == text) return;
        FiltersStatus.Text = text;
        if (text.Length == 0) return;
        Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer
            .CreatePeerForElement(FiltersStatus)
            ?.RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
}
