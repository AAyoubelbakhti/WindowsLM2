using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using LinkManager2.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace LinkManager2.Dialogs;

/// <summary>
/// Lists the user's public share links and collections with their view counts and
/// expiry, and lets them copy the public URL or revoke each one, without leaving the app.
/// </summary>
public sealed partial class SharedDialog : ContentDialog
{
    private const string WebBase = "https://lm.aelbak.dev";

    private readonly ItemsRepository _repo;
    private readonly IReadOnlyDictionary<string, string> _itemTitles;
    private readonly ObservableCollection<ShareEntry> _entries = new();

    public SharedDialog(ItemsRepository repo, IReadOnlyDictionary<string, string> itemTitles)
    {
        InitializeComponent();
        _repo = repo;
        _itemTitles = itemTitles;
        ItemsList.ItemsSource = _entries;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private ShareEntry? Selected => ItemsList.SelectedItem as ShareEntry;

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var on = Selected is not null;
        CopyButton.IsEnabled = on;
        RevokeButton.IsEnabled = on;
        RevokeConfirmText.Text = Selected is null
            ? string.Empty
            : Strings.Format("Shared_RevokeConfirm", Selected.Title);
    }

    private async Task ReloadAsync()
    {
        try
        {
            var links = await _repo.ListShareLinksAsync();
            var collections = await _repo.ListShareCollectionsAsync();
            _entries.Clear();
            foreach (var l in links)
            {
                var name = _itemTitles.TryGetValue(l.ItemId, out var t)
                    ? t
                    : Strings.Get("Shared_LinkFallback");
                _entries.Add(
                    new ShareEntry(
                        IsCollection: false,
                        Token: l.Token,
                        Title: Strings.Format("Shared_LinkTitle", name),
                        Subtitle: Strings.Format(
                            "Shared_Subtitle",
                            l.ViewCount,
                            ExpiryLabel(l.ExpiresAt)
                        ),
                        Url: $"{WebBase}/s/{l.Token}"
                    )
                );
            }
            foreach (var c in collections)
            {
                _entries.Add(
                    new ShareEntry(
                        IsCollection: true,
                        Token: c.Token,
                        Title: Strings.Format("Shared_CollectionTitle", c.Name),
                        Subtitle: Strings.Format(
                            "Shared_Subtitle",
                            c.ViewCount,
                            ExpiryLabel(c.ExpiresAt)
                        ),
                        Url: $"{WebBase}/c/{c.Token}"
                    )
                );
            }
            ItemsList.SelectedItem = null;
            Status(
                InfoBarSeverity.Informational,
                _entries.Count == 1
                    ? Strings.Get("Shared_Count_One")
                    : Strings.Format("Shared_Count_Many", _entries.Count)
            );
        }
        catch (Exception ex)
        {
            Status(InfoBarSeverity.Error, ex.Message);
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry)
            return;
        try
        {
            ItemActions.Copy(entry.Url);
            Status(InfoBarSeverity.Success, Strings.Get("Shared_Copied"));
        }
        catch (Exception ex)
        {
            Status(InfoBarSeverity.Error, ex.Message);
        }
    }

    private async void OnRevokeConfirmClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry)
            return;
        RevokeFlyout.Hide();
        try
        {
            if (entry.IsCollection)
                await _repo.RevokeShareCollectionAsync(entry.Token);
            else
                await _repo.RevokeShareLinkAsync(entry.Token);
            Status(InfoBarSeverity.Success, Strings.Get("Shared_Revoked"));
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Status(InfoBarSeverity.Error, ex.Message);
        }
    }

    private static string ExpiryLabel(DateTime? expiresAt)
    {
        if (expiresAt is null)
            return Strings.Get("Shared_NoExpiry");
        return expiresAt.Value <= DateTime.UtcNow
            ? Strings.Get("Shared_Expired")
            : Strings.Format("Shared_Expires", expiresAt.Value.ToLocalTime());
    }

    private void Status(InfoBarSeverity severity, string msg)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = msg;
        StatusBar.IsOpen = true;
    }

    private sealed record ShareEntry(
        bool IsCollection,
        string Token,
        string Title,
        string Subtitle,
        string Url
    )
    {
        public string AccessibleName => $"{Title}. {Subtitle}";
    }
}
