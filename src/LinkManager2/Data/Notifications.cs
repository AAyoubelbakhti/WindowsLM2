using System;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace LinkManager2.Data;

/// <summary>
/// Thin wrapper over AppNotificationManager for the unpackaged app. Register() must run once
/// during startup before any toast is shown; Show() is a no-op when the user disabled toasts
/// or when registration failed. Toasts carry plain text so a screen reader announces them.
/// </summary>
public static class Notifications
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception ex) { Diagnostics.Log("notifications register", ex); }
    }

    public static void Unregister()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception ex) { Diagnostics.Log("notifications unregister", ex); }
        _registered = false;
    }

    public static void Show(string title, string? body = null)
    {
        if (!_registered || !LocalPreferences.Load().NotificationsEnabled) return;
        try
        {
            var builder = new AppNotificationBuilder().AddText(title);
            if (!string.IsNullOrWhiteSpace(body)) builder.AddText(body);
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex) { Diagnostics.Log("notifications show", ex); }
    }
}
