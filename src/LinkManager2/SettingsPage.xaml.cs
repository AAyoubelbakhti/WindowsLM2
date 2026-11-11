using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using LinkManager2.Data;
using LinkManager2.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

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
        foreach (var (tag, name) in AppLanguage.Offered)
            LanguageRadios.Items.Add(
                new RadioButton
                {
                    Content = name,
                    Tag = tag,
                    Language = tag,
                }
            );
        ShowLanguage(AppLanguage.Normalize(LocalPreferences.Load().Language));
        ShowTranslation();
        ShowRestartBar();
        ShowAbout();
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

    /// <summary>Selects the radio of the saved UI language.</summary>
    private void ShowLanguage(string language)
    {
        foreach (var item in LanguageRadios.Items)
            if (item is RadioButton rb && (string)rb.Tag == language)
                LanguageRadios.SelectedItem = rb;
        if (LanguageRadios.SelectedItem is null)
            LanguageRadios.SelectedIndex = 0;
        _languageShown = true;
    }

    /// <summary>Whether the saved language or the loaded translation changed since this process started, which
    /// only a restart applies.</summary>
    private static bool RestartPending =>
        AppLanguage.Normalize(LocalPreferences.Load().Language) != AppLanguage.AtStart
        || !ReferenceEquals(LoadedTranslation.Current, AppLanguage.TranslationAtStart);

    /// <summary>Shows the restart bar while a restart is pending. It goes by its visibility, with IsOpen always set,
    /// because an InfoBar says itself each time IsOpen changes, which would be one more announcement beside the one of
    /// the change. Returns the bar's message when the bar has just come up, for the change to say it, else
    /// null.</summary>
    private string? ShowRestartBar()
    {
        var wasShown = LanguageRestartBar.Visibility == Visibility.Visible;
        var pending = RestartPending;
        LanguageRestartBar.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
        return pending && !wasShown ? LanguageRestartBar.Message : null;
    }

    /// <summary>Says which translation is loaded, if any: its language, when and how many texts.</summary>
    private void ShowTranslation()
    {
        var translation = LoadedTranslation.Current;
        TranslationText.Visibility = RemoveTranslationButton.Visibility = translation is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (translation is not null)
            TranslationText.Text = Strings.Format(
                "Settings_TranslationInfo",
                LanguageName(translation.Language),
                translation.Loaded.LocalDateTime,
                translation.Texts.Count
            );
    }

    /// <summary>Loads a translation file from the portal and says how many of its texts the app took. Whatever goes
    /// wrong is said instead, as an exception out of this handler would close the app.</summary>
    private async void OnLoadTranslation(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await PickTranslationAsync() is not { } file)
                return;
            var (translation, discarded) = await ReadTranslationAsync(file);
            LoadedTranslation.Save(translation, App.Version);
            ShowTranslation();
            Announce(WithRestartHint(LoadedMessage(translation, discarded), ShowRestartBar()));
        }
        catch (TranslationFileException ex)
        {
            if (ex.InnerException is { } cause)
                Diagnostics.Log("read translation", cause);
            Announce(Strings.Get(ProblemKey(ex.Problem)));
        }
        catch (Exception ex)
        {
            Diagnostics.Log("load translation", ex);
            Announce(Strings.Format("Common_Error", ex.Message));
        }
    }

    private static async Task<StorageFile?> PickTranslationAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(
            picker,
            WindowNative.GetWindowHandle(((App)Application.Current).Window!)
        );
        picker.FileTypeFilter.Add(".resw");
        picker.FileTypeFilter.Add(".xml");
        return await picker.PickSingleFileAsync();
    }

    /// <summary>Reads <paramref name="file"/> as a translation off the UI thread; a file that cannot be read at all
    /// counts as unreadable.</summary>
    /// <exception cref="TranslationFileException">The file is not a translation the app can take.</exception>
    private static async Task<(Translation Translation, int Discarded)> ReadTranslationAsync(
        StorageFile file
    )
    {
        try
        {
            await using var stream = await file.OpenStreamForReadAsync();
            return await Task.Run(() => TranslationFile.Read(stream, Strings.Base));
        }
        catch (Exception ex) when (ex is not TranslationFileException)
        {
            throw new TranslationFileException(TranslationProblem.Unreadable, ex);
        }
    }

    private static string LoadedMessage(Translation translation, int discarded) =>
        discarded == 0
            ? Strings.Format(
                "Settings_TranslationLoaded",
                LanguageName(translation.Language),
                translation.Texts.Count
            )
            : Strings.Format(
                "Settings_TranslationLoadedDiscarded",
                LanguageName(translation.Language),
                translation.Texts.Count,
                discarded
            );

    private static string ProblemKey(TranslationProblem problem) =>
        problem switch
        {
            TranslationProblem.TooLarge => "Settings_TranslationTooLarge",
            TranslationProblem.Unreadable => "Settings_TranslationUnreadable",
            TranslationProblem.NoUsableText => "Settings_TranslationNoUsableText",
            _ => "Settings_TranslationNotOurs",
        };

    /// <summary>Removes the loaded translation. Its button goes away, so the focus moves first to the message, which
    /// the screen reader says as the one announcement of the change.</summary>
    private void OnRemoveTranslation(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadedTranslation.Remove();
        }
        catch (Exception ex)
        {
            Diagnostics.Log("remove translation", ex);
            Announce(Strings.Format("Common_Error", ex.Message));
            return;
        }
        Announce(
            WithRestartHint(Strings.Get("Settings_TranslationRemoved"), ShowRestartBar()),
            atFocus: true
        );
        ShowTranslation();
    }

    /// <summary>The name of a language in the UI language, as "ucraniano" in Spanish.</summary>
    private static string LanguageName(string language) =>
        CultureInfo.GetCultureInfo(language).DisplayName;

    /// <summary><paramref name="message"/> followed by the message of the restart bar when the change has just
    /// brought it up, so a single announcement tells both.</summary>
    private static string WithRestartHint(string message, string? restartHint) =>
        restartHint is null ? message : $"{message} {restartHint}";

    /// <summary>Shows <paramref name="text"/> under the translation buttons and has the screen reader say it once:
    /// by moving the focus there when <paramref name="atFocus"/>, for when the focused button goes away, else as a
    /// live region.</summary>
    private void Announce(string text, bool atFocus = false)
    {
        TranslationStatusText.Text = text;
        TranslationStatusText.Visibility = Visibility.Visible;
        if (atFocus)
            TranslationStatusText.Focus(FocusState.Programmatic);
        else
            FrameworkElementAutomationPeer
                .CreatePeerForElement(TranslationStatusText)
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    /// <summary>Fills the about section: the version and, when this language credits anyone, its translators.</summary>
    private void ShowAbout()
    {
        AboutVersionText.Text = Strings.Format("Settings_AboutVersion", App.Version);
        var translators = Strings.Get("translator_credits");
        if (translators.Length == 0)
            return;
        AboutTranslationText.Text = Strings.Format("Settings_AboutTranslation", translators);
        AboutTranslationText.Visibility = Visibility.Visible;
    }

    /// <summary>Saves the chosen UI language at once, as the theme does; it takes effect on the next start. When the
    /// restart bar comes up with it, the screen reader says the bar's message beside the chosen language, as the bar
    /// does not say itself.</summary>
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
        if (ShowRestartBar() is { } restartHint)
            FrameworkElementAutomationPeer
                .CreatePeerForElement(LanguageRadios)
                ?.RaiseNotificationEvent(
                    AutomationNotificationKind.Other,
                    AutomationNotificationProcessing.CurrentThenMostRecent,
                    restartHint,
                    nameof(LanguageRestartBar)
                );
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
