using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

internal static class DialogHelper
{

    private static bool _open;

    public static async Task<ContentDialogResult> ShowGuardedAsync(this ContentDialog dialog)
    {
        if (_open) return ContentDialogResult.None;
        _open = true;
        try { return await dialog.ShowAsync(); }
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
