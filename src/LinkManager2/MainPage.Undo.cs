using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LinkManager2;

/// <summary>Ctrl+Z undo of the last archive/delete and the recorded undo action.</summary>
public sealed partial class MainPage : Page
{
    private sealed record UndoAction(string Description, Func<Task> RevertAsync);

    /// <summary>
    /// Reverts the last archive or delete. Leaves the accelerator unhandled while a text
    /// input has focus so the native text-undo of the editing control keeps working.
    /// </summary>
    private async void OnUndoInvoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args
    )
    {
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox or PasswordBox or RichEditBox)
        {
            args.Handled = false;
            return;
        }
        args.Handled = true;

        var undo = _lastUndo;
        if (undo is null)
        {
            SetStatus(Strings.Get("Main_UndoNothing"));
            return;
        }
        _lastUndo = null;
        SetStatus(Strings.Get("Main_Undoing"));
        try
        {
            await undo.RevertAsync();
            await App.State.ReloadItemsAsync();
            RefreshVisible();
            SetStatus(Strings.Format("Main_UndoDone", undo.Description));
        }
        catch (Exception ex)
        {
            SetStatus(Strings.Format("Main_UndoFailed", ex.Message));
        }
    }
}
