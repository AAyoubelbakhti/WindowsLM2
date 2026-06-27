using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LinkManager2.Data;
using LinkManager2.Dialogs;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2;

internal sealed class CommandLayerController
{
    private readonly MainWindow _window;

    public CommandLayerController(MainWindow window) => _window = window;

    public async void Show()
    {
        var foreground = GetForegroundWindow();
        _window.ShowFromTray();
        var root = _window.ContentXamlRoot;
        if (root is null) return;

        var dlg = new CommandLayerDialog { XamlRoot = root };
        await dlg.ShowGuardedAsync();
        Dispatch(dlg.Chosen, foreground);
    }

    private async void Dispatch(char key, IntPtr savedForeground)
    {
        switch (key)
        {
            case 'g':
                _window.ShowFromTray();
                break;
            case 'f':
                _window.ShowFromTray();
                if (_window.RootFrame.Content is MainPage searchPage) searchPage.FocusSearch();
                break;
            case 'a':
                await OpenClipboardTargetAsync();
                break;
            case 'h':
                _window.ShowFromTray();
                if (_window.ContentXamlRoot is { } root)
                    await DialogHelper.InfoAsync(root, Help.Title, Help.Body);
                break;
            case 'l':
                var cap = BrowserUrlReader.TryCapture(savedForeground);
                _window.ShowFromTray();
                if (cap is not null) await QuickAddAsync(cap.Title ?? string.Empty, cap.Url);
                else _window.SetStatus("No se pudo leer la URL del navegador.");
                break;
            case 'r':
                var path = ExplorerPathReader.GetSelectedPath(savedForeground);
                _window.ShowFromTray();
                if (!string.IsNullOrEmpty(path))
                {
                    var title = System.IO.Path.GetFileName(path);
                    await QuickAddAsync(string.IsNullOrEmpty(title) ? path : title, path);
                }
                else _window.SetStatus("Sin ventana del Explorador detrás de la capa.");
                break;
        }
    }

    private async Task QuickAddAsync(string title, string value)
    {
        if (_window.ContentXamlRoot is not { } root) return;
        await Task.Yield();

        var (ok, type) = Validation.DetectType(value);
        if (!ok) { _window.SetStatus("El valor capturado no es una URL ni ruta válida."); return; }
        var normalized = type == ItemTypes.Url ? Validation.NormalizeUrl(value) : value;

        var dlg = new AddEditDialog(App.State.Categories, null, title, normalized) { XamlRoot = root };
        if (await dlg.ShowGuardedAsync() != ContentDialogResult.Primary) return;

        (ok, type) = Validation.DetectType(dlg.ItemValue);
        if (!ok) return;
        normalized = type == ItemTypes.Url ? Validation.NormalizeUrl(dlg.ItemValue) : dlg.ItemValue;
        try
        {
            var result = await App.State.AddItemAsync(dlg.ItemTitle, normalized, type, dlg.ItemCategoryId);
            if (_window.RootFrame.Content is MainPage mp) await mp.RefreshAfterExternalChangeAsync();
            _window.SetStatus(result == AppState.AddResult.QueuedOffline
                ? $"Sin conexión, encolado: {dlg.ItemTitle}."
                : $"Añadido: {dlg.ItemTitle}");
        }
        catch (Exception ex) { _window.SetStatus($"Error al añadir: {ex.Message}"); }
    }

    private async Task OpenClipboardTargetAsync()
    {
        try
        {
            var pkg = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!pkg.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text)) return;
            var text = (await pkg.GetTextAsync())?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            var (ok, _) = Validation.DetectType(text);
            if (!ok) { _window.SetStatus("El portapapeles no contiene una URL ni ruta."); return; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            { FileName = text, UseShellExecute = true });
        }
        catch (Exception ex) { _window.SetStatus($"No se pudo abrir el portapapeles: {ex.Message}"); }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
