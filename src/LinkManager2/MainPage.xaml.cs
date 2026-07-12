using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using LinkManager2.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2;

/// <summary>
/// Thin orchestrator for the main list page. Shared state, the visible collection and the
/// core refresh/filter/status plumbing live here; area-specific handlers are split across
/// the MainPage.Startup/Search/Selection/ItemActions/Undo partials.
/// </summary>
public sealed partial class MainPage : Page
{
    public ObservableCollection<ItemViewModel> Visible { get; } = new();

    private readonly TypeAheadNavigation TypeAhead;

    private FilterState _filters = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _searchDebounce;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _realtimeDebounce;
    private bool _loading;
    private int _lastSelectionCount;
    private UndoAction? _lastUndo;

    public MainPage()
    {
        InitializeComponent();
        TypeAhead = new TypeAheadNavigation(ItemsList, o => (o as ItemViewModel)?.Title ?? "");
        _searchDebounce = DispatcherQueue.CreateTimer();
        _searchDebounce.Interval = TimeSpan.FromMilliseconds(160);
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            _filters.Search = SearchBox.Text;
            RefreshVisible();
            AnnounceResultCount();
        };
        _realtimeDebounce = DispatcherQueue.CreateTimer();
        _realtimeDebounce.Interval = TimeSpan.FromMilliseconds(1200);
        _realtimeDebounce.Tick += async (_, _) =>
        {
            _realtimeDebounce.Stop();
            try
            {
                await App.State.FlushPendingAsync();
                await App.State.ReloadAllAsync();
                RefreshVisible();
            }
            catch (Exception ex) { Diagnostics.Log("realtime-reconcile", ex); }
        };
        Loaded += OnLoaded;
    }

    private void RefreshVisible()
    {
        var selectedId = (ItemsList.SelectedItem as ItemViewModel)?.Id;
        Visible.Clear();
        var byId = App.State.Categories.ToDictionary(c => c.Id, c => c.Name);
        foreach (var item in ApplyFilters(App.State.Items))
        {
            var catName = item.CategoryId is not null && byId.TryGetValue(item.CategoryId, out var n) ? n : null;
            Visible.Add(new ItemViewModel(item, catName));
        }

        if (selectedId is not null)
        {
            var match = Visible.FirstOrDefault(v => v.Id == selectedId);
            if (match is not null) ItemsList.SelectedItem = match;
        }
        UpdateFiltersStatus();
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var empty = Visible.Count == 0 && !_loading;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty) return;
        var filtering = _filters.ActiveCount > 0 || !string.IsNullOrWhiteSpace(_filters.Search);
        EmptyStateText.Text = filtering
            ? "Ningún resultado para la búsqueda o los filtros actuales."
            : "Aún no tienes enlaces. Pulsa «Añadir enlace» para empezar.";
    }

    public async Task RefreshAfterExternalChangeAsync()
    {
        try { await App.State.ReloadItemsAsync(); RefreshVisible(); }
        catch (Exception ex) { SetStatus($"Error recargando: {ex.Message}"); }
    }

    public void SetExternalStatus(string text) => SetStatus(text);

    public void FocusSearch() => SearchBox.Focus(FocusState.Programmatic);

    private IEnumerable<Item> ApplyFilters(IEnumerable<Item> items)
    {
        var q = items;
        if (!string.IsNullOrWhiteSpace(_filters.Search))
        {
            var s = _filters.Search.Trim();
            q = q.Where(i =>
                i.Title.Contains(s, StringComparison.OrdinalIgnoreCase)
                || i.Value.Contains(s, StringComparison.OrdinalIgnoreCase)
                || (i.Description is not null && i.Description.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }
        if (_filters.Type != FilterType.All)
        {
            var t = _filters.Type == FilterType.Url ? ItemTypes.Url : ItemTypes.Path;
            q = q.Where(i => i.Type == t);
        }
        if (_filters.CategoryId is not null) q = q.Where(i => i.CategoryId == _filters.CategoryId);
        if (_filters.TagId is not null) q = q.Where(i => App.State.ItemHasTag(i.Id, _filters.TagId));
        if (_filters.FavoritesOnly) q = q.Where(i => i.IsFavorite);
        if (_filters.BrokenOnly) q = q.Where(i => i.LinkStatus == "broken");

        return _filters.Sort switch
        {
            SortKey.AlphaAsc => q.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase),
            SortKey.AlphaDesc => q.OrderByDescending(i => i.Title, StringComparer.OrdinalIgnoreCase),
            SortKey.DateDesc => q.OrderByDescending(i => i.CreatedAt),
            SortKey.DateAsc => q.OrderBy(i => i.CreatedAt),
            SortKey.UsageDesc => q.OrderByDescending(i => i.UsageCount),
            SortKey.CategoryAsc => q.OrderBy(i => i.CategoryId ?? string.Empty).ThenBy(i => i.Title),
            _ => q,
        };
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;

        Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer
            .CreatePeerForElement(StatusText)
            ?.RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
}
