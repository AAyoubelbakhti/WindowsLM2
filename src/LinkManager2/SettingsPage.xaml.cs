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
    private static readonly (string Key, string LabelKey)[] ActionKeys =
    {
        ("copy", "Settings_ActionCopy"),
        ("share", "Settings_ActionShare"),
        ("email", "Settings_ActionEmail"),
        ("tags", "Settings_ActionTags"),
        ("history", "Settings_ActionHistory"),
        ("publink", "Settings_ActionPublink"),
        ("edit", "Settings_ActionEdit"),
        ("archive", "Settings_ActionArchive"),
    };

    private readonly Dictionary<string, CheckBox> _checks = new();

    private uint _hotkeyModifiers;
    private uint _hotkeyVirtualKey;
    private bool _loaded;
    private bool _languageShown;

    public SettingsPage()
    {
        InitializeComponent();
        ShowLanguage(AppLanguage.Normalize(LocalPreferences.Load().Language));
        foreach (var (key, labelKey) in ActionKeys)
        {
            var label = Strings.Get(labelKey);
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

    private void OnEscapeBack(
        Microsoft.UI.Xaml.Input.KeyboardAccelerator sender,
        Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args
    )
    {
        args.Handled = true;
        OnBack(this, new RoutedEventArgs());
    }

    private void UpdateHotkeyButtonLabel()
    {
        HotkeyCaptureButton.Content =
            HotkeyCaptureButton.IsChecked == true
                ? Strings.Get("Settings_HotkeyCapturing")
                : Strings.Format(
                    "Settings_HotkeyChange",
                    GlobalHotkey.Describe(_hotkeyModifiers, _hotkeyVirtualKey)
                );
    }

    private void OnHotkeyCaptureClick(object sender, RoutedEventArgs e) =>
        UpdateHotkeyButtonLabel();

    private void OnHotkeyCaptureKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (HotkeyCaptureButton.IsChecked != true)
            return;

        var key = e.Key;
        if (
            key
            is Windows.System.VirtualKey.Control
                or Windows.System.VirtualKey.Shift
                or Windows.System.VirtualKey.Menu
                or Windows.System.VirtualKey.LeftWindows
                or Windows.System.VirtualKey.RightWindows
        )
            return;

        var isLetterOrDigit = (
            key >= Windows.System.VirtualKey.Number0 && key <= Windows.System.VirtualKey.Z
        );
        if (!isLetterOrDigit)
            return;

        uint modifiers = 0;
        if (IsDown(Windows.System.VirtualKey.Control))
            modifiers |= 0x0002;
        if (IsDown(Windows.System.VirtualKey.Shift))
            modifiers |= 0x0004;
        if (IsDown(Windows.System.VirtualKey.Menu))
            modifiers |= 0x0001;
        if (modifiers == 0)
        {
            Status(InfoBarSeverity.Warning, Strings.Get("Settings_HotkeyNeedsModifier"));
            return;
        }

        _hotkeyModifiers = modifiers;
        _hotkeyVirtualKey = (uint)key;
        HotkeyCaptureButton.IsChecked = false;
        UpdateHotkeyButtonLabel();
        e.Handled = true;
        Status(
            InfoBarSeverity.Informational,
            Strings.Format(
                "Settings_HotkeySet",
                GlobalHotkey.Describe(_hotkeyModifiers, _hotkeyVirtualKey)
            )
        );
    }

    private static bool IsDown(Windows.System.VirtualKey key) =>
        Microsoft
            .UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private async Task LoadAsync()
    {
        Status(InfoBarSeverity.Informational, Strings.Get("Settings_Loading"));
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
            SelectThemeRadio(prefs.Theme);

            StatusBar.IsOpen = false;
            _loaded = true;
        }
        catch (Exception ex)
        {
            Status(InfoBarSeverity.Error, Strings.Format("Settings_LoadError", ex.Message));
        }
    }

    private void SelectThemeRadio(string theme)
    {
        foreach (var item in ThemeRadios.Items)
            if (item is RadioButton rb && (string)rb.Tag == theme)
            {
                ThemeRadios.SelectedItem = rb;
                return;
            }
        if (ThemeRadios.Items.Count > 0)
            ThemeRadios.SelectedIndex = 0;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded)
            return;
        if (ThemeRadios.SelectedItem is not RadioButton rb || rb.Tag is not string theme)
            return;

        var prefs = LocalPreferences.Load();
        prefs.Theme = theme;
        prefs.Save();
        ((App)Application.Current).Window?.ApplyTheme();
    }

    /// <summary>Selects the radio of the saved UI language and says whether a restart is pending to apply it.</summary>
    private void ShowLanguage(string language)
    {
        foreach (var item in LanguageRadios.Items)
            if (item is RadioButton rb && (string)rb.Tag == language)
                LanguageRadios.SelectedItem = rb;
        if (LanguageRadios.SelectedItem is null)
            LanguageRadios.SelectedIndex = 0;
        LanguageRestartBar.IsOpen = language != AppLanguage.AtStart;
        _languageShown = true;
    }

    /// <summary>Saves the chosen UI language at once, as the theme does; it takes effect on the next start.</summary>
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (
            !_languageShown
            || LanguageRadios.SelectedItem is not RadioButton rb
            || rb.Tag is not string language
        )
            return;

        var prefs = LocalPreferences.Load();
        prefs.Language = language;
        prefs.Save();
        LanguageRestartBar.IsOpen = language != AppLanguage.AtStart;
    }

    /// <summary>Saves the page first, when it could load, so a restart for the new language loses no change.</summary>
    private async void OnRestartForLanguage(object sender, RoutedEventArgs e)
    {
        if (_loaded && !await SaveAsync())
            return;
        if (((App)Application.Current).Window?.Restart() != true)
            Status(InfoBarSeverity.Error, Strings.Get("Settings_LanguageRestartFailed"));
    }

    private async void OnSave(object sender, RoutedEventArgs e) => await SaveAsync();

    /// <summary>Saves the account settings and this device's preferences, saying how it went in the status bar.</summary>
    private async Task<bool> SaveAsync()
    {
        Status(InfoBarSeverity.Informational, Strings.Get("Settings_Saving"));
        try
        {
            var name = DisplayNameBox.Text.Trim();
            if (name.Length > 0)
                await App.State.Repo.UpdateDisplayNameAsync(name);

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

            Status(InfoBarSeverity.Success, Strings.Get("Settings_Saved"));
            return true;
        }
        catch (Exception ex)
        {
            Status(InfoBarSeverity.Error, Strings.Format("Settings_SaveError", ex.Message));
            return false;
        }
    }

    private async void OnDeleteAccount(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Strings.Get("Settings_DeleteDialogTitle"),
            Content = Strings.Get("Settings_DeleteDialogBody"),
            PrimaryButtonText = Strings.Get("Settings_DeleteDialogConfirm"),
            CloseButtonText = Strings.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowGuardedAsync() != ContentDialogResult.Primary)
            return;

        Status(InfoBarSeverity.Informational, Strings.Get("Settings_Deleting"));
        try
        {
            var token = App.State.Auth.CurrentSession?.AccessToken;
            if (string.IsNullOrEmpty(token))
            {
                Status(InfoBarSeverity.Error, Strings.Get("Settings_InvalidSession"));
                return;
            }

            var status = await WebApi.DeleteAccountAsync(token);
            if (status is >= 200 and < 300)
            {
                await RealtimeSync.StopAsync();
                try
                {
                    await App.State.Auth.SignOutAsync();
                }
                catch (Exception ex)
                {
                    Diagnostics.Log("delete-account sign-out", ex);
                }
                ((App)Application.Current).Window!.NavigateTo(typeof(LoginPage));
            }
            else
            {
                Status(InfoBarSeverity.Error, Strings.Format("Settings_DeleteFailed", status));
            }
        }
        catch (Exception ex)
        {
            Status(InfoBarSeverity.Error, Strings.Format("Settings_DeleteError", ex.Message));
        }
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        SetUpdateBar(InfoBarSeverity.Informational, Strings.Get("Settings_UpdateChecking"));
        try
        {
            var (status, version) = await UpdateService.CheckAndDownloadAsync();
            switch (status)
            {
                case UpdateStatus.UpToDate:
                    SetUpdateBar(InfoBarSeverity.Success, Strings.Get("Settings_UpdateUpToDate"));
                    break;
                case UpdateStatus.NotInstalled:
                    SetUpdateBar(
                        InfoBarSeverity.Informational,
                        Strings.Get("Settings_UpdateNotInstalled")
                    );
                    break;
                case UpdateStatus.Downloaded:
                    SetUpdateBar(
                        InfoBarSeverity.Success,
                        Strings.Format("Settings_UpdateDownloaded", version)
                    );
                    var dlg = new ContentDialog
                    {
                        XamlRoot = XamlRoot,
                        Title = Strings.Get("Settings_UpdateReadyTitle"),
                        Content = Strings.Format("Settings_UpdateReadyBody", version),
                        PrimaryButtonText = Strings.Get("Settings_UpdateRestart"),
                        CloseButtonText = Strings.Get("Settings_UpdateLater"),
                        DefaultButton = ContentDialogButton.Primary,
                    };
                    if (await dlg.ShowGuardedAsync() == ContentDialogResult.Primary)
                    {
                        SetUpdateBar(
                            InfoBarSeverity.Informational,
                            Strings.Get("Settings_UpdateApplying")
                        );
                        if (!await UpdateService.ApplyAndRestartAsync())
                            SetUpdateBar(
                                InfoBarSeverity.Error,
                                Strings.Get("Settings_UpdateApplyFailed")
                            );
                    }
                    break;
                default:
                    SetUpdateBar(InfoBarSeverity.Error, Strings.Get("Settings_UpdateCheckFailed"));
                    break;
            }
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
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
        if (frame.CanGoBack)
            frame.GoBack();
        else
            frame.Navigate(typeof(MainPage));
    }

    private void Status(InfoBarSeverity severity, string msg)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = msg;
        StatusBar.IsOpen = true;
    }
}
