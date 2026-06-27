using System;
using LinkManager2.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

public sealed partial class ShareLinkDialog : ContentDialog
{
    private const string PublicBase = "https://lm.aelbak.dev";

    private readonly ItemsRepository _repo;
    private readonly string _itemId;

    public ShareLinkDialog(ItemsRepository repo, string itemId, string itemTitle)
    {
        InitializeComponent();
        _repo = repo; _itemId = itemId;
        ItemLabel.Text = $"Item: {itemTitle}";

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
            var link = await _repo.CreateShareLinkAsync(_itemId, exp.Days);
            ResultBox.Text = $"{PublicBase}/s/{link.Token}";
            CopyButton.IsEnabled = true;
            SetStatus(InfoBarSeverity.Success, "Enlace generado. Copia y compártelo.");
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
