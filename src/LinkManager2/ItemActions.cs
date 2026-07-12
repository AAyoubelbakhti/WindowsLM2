using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using LinkManager2.Data;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using WinRT.Interop;

namespace LinkManager2;

internal static class ItemActions
{

    public static void Open(Item item)
    {
        if (item.Type == ItemTypes.Path && Directory.Exists(item.Value))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{item.Value}\"",
                UseShellExecute = false,
            });
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = item.Value, UseShellExecute = true });
    }

    /// <summary>
    /// Copies text to the clipboard, retrying on the transient COMException WinUI raises
    /// when the clipboard is briefly locked by another process; the content still lands.
    /// </summary>
    public static void Copy(string text)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetContent(dp);
                Clipboard.Flush();
                return;
            }
            catch (Exception) when (attempt < 4)
            {
                Thread.Sleep(30);
            }
        }
    }

    public static void Share(Microsoft.UI.Xaml.Window window, Item item)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        var dtm = DataTransferManagerInterop.GetForWindow(hwnd);

        TypedEventHandler<DataTransferManager, DataRequestedEventArgs>? handler = null;
        handler = (_, args) =>
        {
            var data = args.Request.Data;
            data.Properties.Title = item.Title;
            if (item.Type == ItemTypes.Url && Uri.TryCreate(item.Value, UriKind.Absolute, out var uri))
                data.SetWebLink(uri);
            data.SetText(item.Value);
            if (handler is not null) dtm.DataRequested -= handler;
        };
        dtm.DataRequested += handler;
        DataTransferManagerInterop.ShowShareUIForWindow(hwnd);
    }
}
