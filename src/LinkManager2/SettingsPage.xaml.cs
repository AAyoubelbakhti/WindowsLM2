using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LinkManager2.Data;
using LinkManager2.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2;

public sealed partial class SettingsPage : Page
{
    private static readonly (string Key, string Label)[] ActionKeys =
    {
        ("copy", "Copiar URL"),
        ("share", "Compartir"),
        ("email", "Enviar por email"),
        ("tags", "Editar etiquetas"),
        ("history", "Ver historial"),
        ("publink", "Crear enlace público"),
        ("edit", "Editar elemento"),
        ("archive", "Archivar"),
    };

    private readonly Dictionary<string, CheckBox> _checks = new();

    private uint _hotkeyModifiers;
    private uint _hotkeyVirtualKey;

    public SettingsPage()
    {
        InitializeComponent();
        foreach (var (key, label) in ActionKeys)
        {
            var cb = new CheckBox { Content = label, IsChecked = true };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cb, label);
            _checks[key] = cb;
            ActionsPanel.Children.Add(cb);
        }
        Loaded += async (_, _) =>
        {
            HeadingText.Focus(FocusState.Programmatic);
            await LoadAsync();
        };
    }

    private void OnEscapeBack(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OnBack(this, new RoutedEventArgs());
    }

    private void UpdateHotkeyButtonLabel()
    {
        HotkeyCaptureButton.Content = HotkeyCaptureButton.IsChecked == true
            ? "Pulsa la combinación…"
            : $"Cambiar atajo (actual: {GlobalHotkey.Describe(_hotkeyModifiers, _hotkeyVirtualKey)})";
    }

    private void OnHotkeyCaptureClick(object sender, RoutedEventArgs e) => UpdateHotkeyButtonLabel();

    private void OnHotkeyCaptureKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (HotkeyCaptureButton.IsChecked != true) return;

        var key = e.Key;
        if (key is Windows.System.VirtualKey.Control or Windows.System.VirtualKey.Shift
            or Windows.System.VirtualKey.Menu or Windows.System.VirtualKey.LeftWindows
            or Windows.System.VirtualKey.RightWindows)
            return;

        var isLetterOrDigit = (key >= Windows.System.VirtualKey.Number0 && key <= Windows.System.VirtualKey.Z);
        if (!isLetterOrDigit) return;

        uint modifiers = 0;
        if (IsDown(Windows.System.VirtualKey.Control)) modifiers |= 0x0002;
        if (IsDown(Windows.System.VirtualKey.Shift)) modifiers |= 0x0004;
        if (IsDown(Windows.System.VirtualKey.Menu)) modifiers |= 0x0001;
        if (modifiers == 0)
        {
            Status(InfoBarSeverity.Warning, "Usa al menos Ctrl, Mayús o Alt junto a la tecla.");
            return;
        }

        _hotkeyModifiers = modifiers;
        _hotkeyVirtualKey = (uint)key;
        HotkeyCaptureButton.IsChecked = false;
        UpdateHotkeyButtonLabel();
        e.Handled = true;
        Status(InfoBarSeverity.Informational,
            $"Atajo: {GlobalHotkey.Describe(_hotkeyModifiers, _hotkeyVirtualKey)}. Guarda para aplicarlo.");
    }

    private static bool IsDown(Windows.System.VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private async Task LoadAsync()
    {
        Status(InfoBarSeverity.Informational, "Cargando…");
        try
        {
            var profile = await App.State.Repo.GetProfileAsync();
            DisplayNameBox.Text = profile?.DisplayName ?? string.Empty;

            var settings = await App.State.Repo.GetSettingsAsync();
            HistoryToggle.IsOn = settings?.HistoryEnabled ?? true;
            ShareCategoriesToggle.IsOn = settings?.ShareCategories ?? false;
            ShareTagsToggle.IsOn = settings?.ShareTags ?? false;
            foreach (var (key, _) in ActionKeys)
                _checks[key].IsChecked = settings?.IsActionVisible(key) ?? true;

            var prefs = LocalPreferences.Load();
            MicaToggle.IsOn = prefs.MicaBackdrop;
            TrayToggle.IsOn = prefs.MinimizeToTrayOnClose;
            HotkeyToggle.IsOn = prefs.GlobalHotkeyEnabled;
            NotificationsToggle.IsOn = prefs.NotificationsEnabled;
            _hotkeyModifiers = prefs.HotkeyModifiers;
            _hotkeyVirtualKey = prefs.HotkeyVirtualKey;
            UpdateHotkeyButtonLabel();
            StartupToggle.IsOn = StartupManager.IsEnabled;

            StatusBar.IsOpen = false;
        }
        catch (Exception ex) { Status(InfoBarSeverity.Error, $"Error cargando: {ex.Message}"); }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        Status(InfoBarSeverity.Informational, "Guardando…");
        try
        {
            var name = DisplayNameBox.Text.Trim();
            if (name.Length > 0) await App.State.Repo.UpdateDisplayNameAsync(name);

            var actionsVisible = new Dictionary<string, object?>();
            foreach (var (key, _) in ActionKeys)
                actionsVisible[key] = _checks[key].IsChecked == true;

            var patch = new Dictionary<string, object?>
            {
                ["history_enabled"] = HistoryToggle.IsOn,
                ["actions_visible"] = actionsVisible,
                ["share_categories"] = ShareCategoriesToggle.IsOn,
                ["share_tags"] = ShareTagsToggle.IsOn,
            };
            await App.State.Repo.SaveSettingsPatchAsync(patch);
            await App.State.ReloadSettingsAsync();

            var prefs = LocalPreferences.Load();
            prefs.MicaBackdrop = MicaToggle.IsOn;
            prefs.MinimizeToTrayOnClose = TrayToggle.IsOn;
            prefs.GlobalHotkeyEnabled = HotkeyToggle.IsOn;
            prefs.NotificationsEnabled = NotificationsToggle.IsOn;
            prefs.HotkeyModifiers = _hotkeyModifiers;
            prefs.HotkeyVirtualKey = _hotkeyVirtualKey;
            prefs.Save();

            StartupManager.SetEnabled(StartupToggle.IsOn);

            ((App)Application.Current).Window?.ReloadPreferences();

            Status(InfoBarSeverity.Success, "Ajustes guardados.");
        }
        catch (Exception ex) { Status(InfoBarSeverity.Error, $"Error al guardar: {ex.Message}"); }
    }

    private async void OnDeleteAccount(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "¿Eliminar tu cuenta?",
            Content = "Se borrarán de forma permanente tu cuenta y todos tus datos " +
                      "(enlaces, categorías, etiquetas, ajustes y enlaces compartidos). " +
                      "Esta acción no se puede deshacer.",
            PrimaryButtonText = "Eliminar cuenta",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowGuardedAsync() != ContentDialogResult.Primary) return;

        Status(InfoBarSeverity.Informational, "Eliminando tu cuenta…");
        try
        {
            var token = App.State.Auth.CurrentSession?.AccessToken;
            if (string.IsNullOrEmpty(token)) { Status(InfoBarSeverity.Error, "Sesión no válida."); return; }

            var status = await WebApi.DeleteAccountAsync(token);
            if (status is >= 200 and < 300)
            {
                try { await App.State.Auth.SignOutAsync(); }
                catch (Exception ex) { Diagnostics.Log("delete-account sign-out", ex); }
                ((App)Application.Current).Window!.NavigateTo(typeof(LoginPage));
            }
            else
            {
                Status(InfoBarSeverity.Error, $"No se pudo eliminar la cuenta ({status}).");
            }
        }
        catch (Exception ex) { Status(InfoBarSeverity.Error, $"Error al eliminar: {ex.Message}"); }
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        SetUpdateBar(InfoBarSeverity.Informational, "Buscando actualizaciones…");
        try
        {
            var (status, version) = await UpdateService.CheckAndDownloadAsync();
            switch (status)
            {
                case UpdateStatus.UpToDate:
                    SetUpdateBar(InfoBarSeverity.Success, "Ya tienes la última versión.");
                    break;
                case UpdateStatus.NotInstalled:
                    SetUpdateBar(InfoBarSeverity.Informational,
                        "Las actualizaciones automáticas requieren instalar la app con el instalador (no la versión portable).");
                    break;
                case UpdateStatus.Downloaded:
                    SetUpdateBar(InfoBarSeverity.Success, $"Descargada la versión {version}.");
                    var dlg = new ContentDialog
                    {
                        XamlRoot = XamlRoot,
                        Title = "Actualización lista",
                        Content = $"Se ha descargado la versión {version}. ¿Reiniciar ahora para aplicarla?",
                        PrimaryButtonText = "Reiniciar y actualizar",
                        CloseButtonText = "Más tarde",
                        DefaultButton = ContentDialogButton.Primary,
                    };
                    if (await dlg.ShowGuardedAsync() == ContentDialogResult.Primary)
                    {
                        SetUpdateBar(InfoBarSeverity.Informational, "Aplicando actualización…");
                        if (!await UpdateService.ApplyAndRestartAsync())
                            SetUpdateBar(InfoBarSeverity.Error, "No se pudo aplicar la actualización. Inténtalo de nuevo.");
                    }
                    break;
                default:
                    SetUpdateBar(InfoBarSeverity.Error, "No se pudo comprobar. Revisa la conexión.");
                    break;
            }
        }
        finally { CheckUpdatesButton.IsEnabled = true; }
    }

    private void SetUpdateBar(InfoBarSeverity severity, string msg)
    {
        UpdateBar.Severity = severity;
        UpdateBar.Title = msg;
        UpdateBar.IsOpen = true;
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        var frame = ((App)Application.Current).Window!.RootFrame;
        if (frame.CanGoBack) frame.GoBack();
        else frame.Navigate(typeof(MainPage));
    }

    private void Status(InfoBarSeverity severity, string msg)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = msg;
        StatusBar.IsOpen = true;
    }
}
