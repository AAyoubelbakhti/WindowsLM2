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

    public async void AddLink()
    {
        _window.ShowFromTray();
        await QuickAddAsync(string.Empty, string.Empty);
    }

    public async void SaveClipboard() => await SaveClipboardTargetAsync();

    public void Search()
    {
        _window.ShowFromTray();
        if (_window.RootFrame.Content is MainPage page) page.FocusSearch();
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
            case 'p':
                await SaveClipboardTargetAsync();
                break;
            case 'h':
                _window.ShowFromTray();
                if (_window.ContentXamlRoot is { } root)
                    await DialogHelper.InfoAsync(root, Help.Title, Help.Body);
                break;
            case 'l':
                var cap = await CaptureBrowserUrlAsync(savedForeground);
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

        string normalized = string.Empty;
        if (!string.IsNullOrWhiteSpace(value))
        {
            var (detected, type) = Validation.DetectType(value);
            if (!detected) { _window.SetStatus("El valor capturado no es una URL ni ruta válida."); return; }
            normalized = type == ItemTypes.Url ? Validation.NormalizeUrl(value) : value;
        }

        var dlg = new AddEditDialog(App.State.Categories, null, title, normalized) { XamlRoot = root };
        if (await dlg.ShowGuardedAsync() != ContentDialogResult.Primary) return;

        var (ok, finalType) = Validation.DetectType(dlg.ItemValue);
        if (!ok) return;
        normalized = finalType == ItemTypes.Url ? Validation.NormalizeUrl(dlg.ItemValue) : dlg.ItemValue;
        try
        {
            var result = await App.State.AddItemAsync(dlg.ItemTitle, normalized, finalType, dlg.ItemCategoryId, description: dlg.ItemDescription);
            if (_window.RootFrame.Content is MainPage mp) await mp.RefreshAfterExternalChangeAsync();
            var message = result == AppState.AddResult.QueuedOffline
                ? $"Sin conexión, encolado: {dlg.ItemTitle}."
                : $"Añadido: {dlg.ItemTitle}";
            _window.SetStatus(message);
            Notifications.Show(result == AppState.AddResult.QueuedOffline ? "Enlace en cola" : "Enlace guardado",
                message);
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

    private async Task SaveClipboardTargetAsync()
    {
        _window.ShowFromTray();
        string? text;
        try
        {
            var pkg = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!pkg.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                _window.SetStatus("El portapapeles no contiene texto.");
                return;
            }
            text = (await pkg.GetTextAsync())?.Trim();
        }
        catch (Exception ex) { _window.SetStatus($"No se pudo leer el portapapeles: {ex.Message}"); return; }

        if (string.IsNullOrEmpty(text)) { _window.SetStatus("El portapapeles está vacío."); return; }
        var (ok, type) = Validation.DetectType(text);
        if (!ok) { _window.SetStatus("El portapapeles no contiene una URL ni ruta."); return; }

        var title = type == ItemTypes.Path ? System.IO.Path.GetFileName(text) : string.Empty;
        await QuickAddAsync(title, text);
    }

    /// <summary>
    /// Reads the browser URL off the UI thread (the UIA tree walk can take seconds) and
    /// refuses windows of this process to avoid an in-proc UIA self-interrogation deadlock.
    /// </summary>
    private static async Task<BrowserUrlReader.Capture?> CaptureBrowserUrlAsync(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || IsOwnWindow(hwnd)) return null;
        return await Task.Run(() => BrowserUrlReader.TryCapture(hwnd));
    }

    private static bool IsOwnWindow(IntPtr hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
