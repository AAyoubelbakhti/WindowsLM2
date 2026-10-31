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
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            return;
        UpdateSuggestions(sender.Text);
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void OnSuggestionChosen(
        AutoSuggestBox sender,
        AutoSuggestBoxSuggestionChosenEventArgs args
    )
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
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchBox.ItemsSource = null;
            return;
        }
        SearchBox.ItemsSource = App
            .State.Items.Where(i => i.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
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
        SetStatus(
            n == 1 ? Strings.Get("Main_Results_One") : Strings.Format("Main_Results_Many", n)
        );
    }

    private void UpdateFiltersStatus()
    {
        var count = _filters.ActiveCount;
        FiltersButton.Content =
            count > 0 ? Strings.Format("Main_FiltersCount", count) : Strings.Get("Main_Filters");

        if (count == 0 && _filters.Sort == SortKey.AlphaAsc)
        {
            SetFiltersStatusText(string.Empty);
            return;
        }

        var parts = new List<string>();
        if (_filters.Type != FilterType.All)
            parts.Add(
                Strings.Get(
                    _filters.Type == FilterType.Url ? "Main_FilterTypeUrls" : "Main_FilterTypePaths"
                )
            );
        if (_filters.CategoryId is not null)
            parts.Add(
                Strings.Format(
                    "Main_FilterCategory",
                    App.State.Categories.FirstOrDefault(c => c.Id == _filters.CategoryId)?.Name
                        ?? "?"
                )
            );
        if (_filters.TagId is not null)
            parts.Add(
                Strings.Format(
                    "Main_FilterTag",
                    App.State.Tags.FirstOrDefault(t => t.Id == _filters.TagId)?.Name ?? "?"
                )
            );
        if (_filters.FavoritesOnly)
            parts.Add(Strings.Get("Main_FilterFavorites"));
        if (_filters.BrokenOnly)
            parts.Add(Strings.Get("Main_FilterBroken"));
        if (_filters.Sort != SortKey.AlphaAsc)
            parts.Add(
                _filters.Sort switch
                {
                    SortKey.AlphaDesc => Strings.Get("Main_SortAlphaDesc"),
                    SortKey.DateDesc => Strings.Get("Main_SortDateDesc"),
                    SortKey.DateAsc => Strings.Get("Main_SortDateAsc"),
                    SortKey.UsageDesc => Strings.Get("Main_SortUsageDesc"),
                    SortKey.CategoryAsc => Strings.Get("Main_SortCategory"),
                    _ => "",
                }
            );
        SetFiltersStatusText(Strings.Format("Main_FiltersActive", string.Join(" · ", parts)));
    }

    /// <summary>Updates the filters live region and raises LiveRegionChanged only when the text actually changes.</summary>
    private void SetFiltersStatusText(string text)
    {
        if (FiltersStatus.Text == text)
            return;
        FiltersStatus.Text = text;
        if (text.Length == 0)
            return;
        Microsoft
            .UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(
                FiltersStatus
            )
            ?.RaiseAutomationEvent(
                Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged
            );
    }
}
