using System;
using LinkManager2.Data;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

public sealed partial class EmailShareDialog : ContentDialog
{
    private readonly string _itemId;

    public EmailShareDialog(string itemId, string itemTitle)
    {
        InitializeComponent();
        _itemId = itemId;
        ItemLabel.Text = Strings.Format("EmailShare_ItemLabel", itemTitle);
    }

    private async void OnSendClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var def = args.GetDeferral();
        try
        {
            var email = EmailBox.Text.Trim();
            if (email.Length == 0 || !email.Contains('@'))
            {
                Status(InfoBarSeverity.Error, Strings.Get("EmailShare_InvalidEmail"));
                return;
            }

            Status(InfoBarSeverity.Informational, Strings.Get("EmailShare_Sending"));

            var token =
                SupabaseClientHolder.Client.Auth.CurrentSession?.AccessToken
                ?? throw new InvalidOperationException(Strings.Get("EmailShare_NoSession"));

            var error = await WebApi.SendEmailAsync(_itemId, email, MessageBox.Text.Trim(), token);
            if (error is null)
            {
                args.Cancel = false;
                return;
            }
            Status(InfoBarSeverity.Error, error);
        }
        catch (Exception ex)
        {
            Status(InfoBarSeverity.Error, ex.Message);
        }
        finally
        {
            def.Complete();
        }
    }

    private void Status(InfoBarSeverity severity, string msg)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = msg;
        StatusBar.IsOpen = true;
    }
}
