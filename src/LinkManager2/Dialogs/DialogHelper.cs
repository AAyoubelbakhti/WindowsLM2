using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

internal static class DialogHelper
{

    private static bool _open;

    public static async Task<ContentDialogResult> ShowGuardedAsync(this ContentDialog dialog)
    {
        var (_, result) = await dialog.TryShowGuardedAsync();
        return result;
    }

    /// <summary>
    /// Like ShowGuardedAsync but reports whether the dialog was actually shown, so callers
    /// can tell "another dialog was open" apart from "the user dismissed it".
    /// </summary>
    public static async Task<(bool Shown, ContentDialogResult Result)> TryShowGuardedAsync(this ContentDialog dialog)
    {
        if (_open) return (false, ContentDialogResult.None);
        _open = true;
        try { return (true, await dialog.ShowAsync()); }
        finally { _open = false; }
    }

    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message,
        string primary = "Aceptar", string cancel = "Cancelar")
    {
        var dlg = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            PrimaryButtonText = primary,
            CloseButtonText = cancel,
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dlg.ShowGuardedAsync() == ContentDialogResult.Primary;
    }

    public static Task InfoAsync(XamlRoot root, string title, string message)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            CloseButtonText = "Cerrar",
        };
        return dlg.ShowGuardedAsync();
    }
}
