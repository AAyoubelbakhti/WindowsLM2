using System;
using System.Collections.Generic;
using System.Diagnostics;
using LinkManager2.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

public sealed partial class ShareCollectionDialog : ContentDialog
{
    private const string PublicBase = "https://lm.aelbak.dev";

    private readonly ItemsRepository _repo;
    private readonly IReadOnlyList<string> _itemIds;

    public ShareCollectionDialog(ItemsRepository repo, IReadOnlyList<string> itemIds)
    {
        InitializeComponent();
        _repo = repo;
        _itemIds = itemIds;
        CountLabel.Text = $"{itemIds.Count} enlaces se incluirán.";

        ExpiresCombo.Items.Add(new Expiry(null, "No caduca"));
        ExpiresCombo.Items.Add(new Expiry(1, "1 día"));
        ExpiresCombo.Items.Add(new Expiry(7, "7 días"));
        ExpiresCombo.Items.Add(new Expiry(30, "30 días"));
        ExpiresCombo.SelectedIndex = 2;
    }

    private async void OnGenerateClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var def = args.GetDeferral();
        try
        {
            SetStatus(InfoBarSeverity.Informational, "Generando…");
            var exp = (Expiry)ExpiresCombo.SelectedItem!;
            var token = await _repo.CreateShareCollectionAsync(NameBox.Text, _itemIds, exp.Days);
            ResultBox.Text = $"{PublicBase}/c/{token}";
            CopyButton.IsEnabled = true;
            OpenButton.IsEnabled = true;
            SetStatus(InfoBarSeverity.Success, "Colección creada. Copia o abre la URL.");
        }
        catch (Exception ex) { SetStatus(InfoBarSeverity.Error, $"{ex.GetType().Name}: {ex.Message}"); }
        finally { def.Complete(); }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ItemActions.Copy(ResultBox.Text);
            SetStatus(InfoBarSeverity.Success, "URL copiada al portapapeles.");
        }
        catch (Exception ex) { SetStatus(InfoBarSeverity.Error, ex.Message); }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = ResultBox.Text, UseShellExecute = true }); }
        catch (Exception ex) { SetStatus(InfoBarSeverity.Error, ex.Message); }
    }

    private void SetStatus(InfoBarSeverity severity, string msg)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = msg;
        StatusBar.IsOpen = true;
    }

    private sealed record Expiry(int? Days, string Label)
    {
        public override string ToString() => Label;
    }
}
