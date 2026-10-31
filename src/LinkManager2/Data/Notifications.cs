using Windows.UI.Notifications;

namespace LinkManager2.Data;

/// <summary>
/// Toasts through the Windows toast API under the app id Velopack gives the Start Menu shortcut.
/// AppNotificationManager cannot register a self-contained unpackaged app (WindowsAppSDK #6774).
/// Show() is a no-op when the user disabled toasts; toasts carry plain text so a screen reader
/// announces them.
/// </summary>
public static class Notifications
{
    private const string AppId = "velopack.LinkManager";

    public static void Show(string title, string? body = null)
    {
        if (!LocalPreferences.Load().NotificationsEnabled)
            return;
        try
        {
            var hasBody = !string.IsNullOrWhiteSpace(body);
            var xml = ToastNotificationManager.GetTemplateContent(
                hasBody ? ToastTemplateType.ToastText02 : ToastTemplateType.ToastText01
            );
            var lines = xml.GetElementsByTagName("text");
            lines[0].AppendChild(xml.CreateTextNode(title));
            if (hasBody)
                lines[1].AppendChild(xml.CreateTextNode(body!));
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
        }
        catch (Exception ex)
        {
            Diagnostics.Log("notifications show", ex);
        }
    }
}
