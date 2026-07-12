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
            : $"¿Revocar \"{Selected.Title}\"? El enlace público dejará de funcionar.";
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
                var name = _itemTitles.TryGetValue(l.ItemId, out var t) ? t : "(enlace)";
                _entries.Add(new ShareEntry(
                    IsCollection: false,
                    Token: l.Token,
                    Title: $"Enlace: {name}",
                    Subtitle: $"{l.ViewCount} vistas · {ExpiryLabel(l.ExpiresAt)}",
                    Url: $"{WebBase}/s/{l.Token}"));
            }
            foreach (var c in collections)
            {
                _entries.Add(new ShareEntry(
                    IsCollection: true,
                    Token: c.Token,
                    Title: $"Colección: {c.Name}",
                    Subtitle: $"{c.ViewCount} vistas · {ExpiryLabel(c.ExpiresAt)}",
                    Url: $"{WebBase}/c/{c.Token}"));
            }
            ItemsList.SelectedItem = null;
            Status(InfoBarSeverity.Informational,
                _entries.Count == 1 ? "1 compartido." : $"{_entries.Count} compartidos.");
        }
        catch (Exception ex) { Status(InfoBarSeverity.Error, ex.Message); }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        ItemActions.Copy(entry.Url);
        Status(InfoBarSeverity.Success, "Enlace copiado al portapapeles.");
    }

    private async void OnRevokeConfirmClick(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        RevokeFlyout.Hide();
        try
        {
            if (entry.IsCollection) await _repo.RevokeShareCollectionAsync(entry.Token);
            else await _repo.RevokeShareLinkAsync(entry.Token);
            Status(InfoBarSeverity.Success, "Revocado.");
            await ReloadAsync();
        }
        catch (Exception ex) { Status(InfoBarSeverity.Error, ex.Message); }
    }

    private static string ExpiryLabel(DateTime? expiresAt)
    {
        if (expiresAt is null) return "sin caducidad";
        return expiresAt.Value <= DateTime.UtcNow
            ? "caducado"
            : $"caduca {expiresAt.Value.ToLocalTime():d}";
    }

    private void Status(InfoBarSeverity severity, string msg)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = msg;
        StatusBar.IsOpen = true;
    }

    private sealed record ShareEntry(bool IsCollection, string Token, string Title, string Subtitle, string Url)
    {
        public string AccessibleName => $"{Title}. {Subtitle}";
    }
}
