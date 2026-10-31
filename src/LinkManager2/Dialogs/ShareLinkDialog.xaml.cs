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
        _repo = repo;
        _itemId = itemId;
        ItemLabel.Text = Strings.Format("ShareLink_Item", itemTitle);

        ExpiresCombo.Items.Add(new Expiry(null, Strings.Get("Dialog_ExpiryNever")));
        ExpiresCombo.Items.Add(new Expiry(1, Strings.Get("Dialog_ExpiryOneDay")));
        ExpiresCombo.Items.Add(new Expiry(7, Strings.Get("Dialog_ExpirySevenDays")));
        ExpiresCombo.Items.Add(new Expiry(30, Strings.Get("Dialog_ExpiryThirtyDays")));
        ExpiresCombo.SelectedIndex = 2;
    }

    private async void OnGenerateClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var def = args.GetDeferral();
        try
        {
            SetStatus(InfoBarSeverity.Informational, Strings.Get("Dialog_Generating"));
            var exp = (Expiry)ExpiresCombo.SelectedItem!;
            var link = await _repo.CreateShareLinkAsync(_itemId, exp.Days);
            ResultBox.Text = $"{PublicBase}/s/{link.Token}";
            CopyButton.IsEnabled = true;
            SetStatus(InfoBarSeverity.Success, Strings.Get("ShareLink_Generated"));
        }
        catch (Exception ex)
        {
            SetStatus(
                InfoBarSeverity.Error,
                Strings.Format("Dialog_ErrorDetail", ex.GetType().Name, ex.Message)
            );
        }
        finally
        {
            def.Complete();
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ItemActions.Copy(ResultBox.Text);
            SetStatus(InfoBarSeverity.Success, Strings.Get("Dialog_UrlCopied"));
        }
        catch (Exception ex)
        {
            SetStatus(InfoBarSeverity.Error, ex.Message);
        }
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
