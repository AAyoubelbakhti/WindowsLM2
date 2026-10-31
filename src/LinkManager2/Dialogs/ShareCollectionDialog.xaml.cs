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
        CountLabel.Text = Strings.Format("ShareCollection_Count", itemIds.Count);

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
            var token = await _repo.CreateShareCollectionAsync(NameBox.Text, _itemIds, exp.Days);
            ResultBox.Text = $"{PublicBase}/c/{token}";
            CopyButton.IsEnabled = true;
            OpenButton.IsEnabled = true;
            SetStatus(InfoBarSeverity.Success, Strings.Get("ShareCollection_Created"));
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

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo { FileName = ResultBox.Text, UseShellExecute = true }
            );
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
