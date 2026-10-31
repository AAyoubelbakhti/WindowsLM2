using System;
using System.Diagnostics;
using System.Net.Mail;
using System.Threading.Tasks;
using LinkManager2.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Supabase.Gotrue;

namespace LinkManager2;

public sealed partial class LoginPage : Page
{
    private const string WebSignUpUrl = "https://lm.aelbak.dev/login?signup=1";

    private bool _busy;

    public LoginPage()
    {
        InitializeComponent();
        EmailBox.KeyDown += SubmitOnEnter;
        PasswordBox.KeyDown += SubmitOnEnter;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (App.BootstrapError is not null)
        {
            ShowError(App.BootstrapError);
            return;
        }

        SetBusy(true);
        SubtitleText.Text = Strings.Get("Login_Connecting");
        try
        {
            await App.State.WaitReadyAsync();
        }
        catch (Exception ex)
        {
            ShowError(Strings.Format("Login_ConnectError", ex.Message));
            SetBusy(false);
            return;
        }

        SubtitleText.Text = Strings.Get("Login_SignInPrompt");
        SetBusy(false);

        if (App.State.Auth.IsAuthenticated)
        {
            GoToMain();
            return;
        }

        EmailBox.Focus(FocusState.Programmatic);
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (SubmitButton is null || SignUpMode is null)
            return;
        SubmitButton.Content =
            SignUpMode.IsChecked == true
                ? Strings.Get("Login_SubmitSignUp")
                : Strings.Get("Login_SubmitSignIn");
    }

    private void SubmitOnEnter(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && !_busy)
        {
            e.Handled = true;
            OnSubmit(sender, new RoutedEventArgs());
        }
    }

    private async void OnSubmit(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        if (!EnsureReady())
            return;

        var email = EmailBox.Text.Trim();
        var pass = PasswordBox.Password;
        if (!IsValidEmail(email))
        {
            ShowError(Strings.Get("Login_InvalidEmail"));
            EmailBox.Focus(FocusState.Programmatic);
            return;
        }
        if (pass.Length < 6)
        {
            ShowError(Strings.Get("Login_PasswordTooShort"));
            PasswordBox.Focus(FocusState.Programmatic);
            return;
        }

        await RunAsync(async () =>
        {
            if (SignUpMode.IsChecked == true)
            {
                await App.State.Auth.SignUpWithEmailAsync(email, pass);
                if (!App.State.Auth.IsAuthenticated)
                {
                    ShowInfo(Strings.Get("Login_AccountCreated"));
                    SignInMode.IsChecked = true;
                    return;
                }
            }
            else
            {
                await App.State.Auth.SignInWithEmailAsync(email, pass);
            }
            GoToMain();
        });
    }

    private async void OnForgotPassword(object sender, RoutedEventArgs e)
    {
        if (_busy || !EnsureReady())
            return;
        var email = EmailBox.Text.Trim();
        if (!IsValidEmail(email))
        {
            ShowError(Strings.Get("Login_EmailRequiredForReset"));
            EmailBox.Focus(FocusState.Programmatic);
            return;
        }
        await RunAsync(async () =>
        {
            await App.State.Auth.SendPasswordResetEmailAsync(email);
            ShowInfo(Strings.Format("Login_ResetEmailSent", email));
        });
    }

    private async void OnGitHubSignIn(object sender, RoutedEventArgs e) =>
        await RunWebOAuthAsync("github", "GitHub");

    private async void OnDiscordSignIn(object sender, RoutedEventArgs e) =>
        await RunWebOAuthAsync("discord", "Discord");

    private async Task RunWebOAuthAsync(string provider, string label)
    {
        if (_busy || !EnsureReady())
            return;
        ShowInfoTransient(Strings.Format("Login_OpeningProvider", label));
        await RunAsync(async () =>
        {
            var ok = await OAuthListener.RunWebAssistedAsync(App.State.Auth, provider);
            if (!ok)
            {
                ShowError(Strings.Format("Login_OAuthIncomplete", label));
                return;
            }
            GoToMain();
        });
    }

    private void OnSignUpWeb(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = WebSignUpUrl, UseShellExecute = true });
            ShowInfo(Strings.Get("Login_WebSignUpOpened"));
        }
        catch (Exception ex)
        {
            ShowError(Strings.Format("Login_BrowserOpenError", ex.Message));
        }
    }

    private async Task RunAsync(Func<Task> body)
    {
        SetBusy(true);
        ErrorBar.IsOpen = false;
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            ShowError(AuthService.DescribeError(ex));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void GoToMain()
    {
        var w = ((App)Application.Current).Window!;
        w.InstallSystemHooks();
        w.NavigateTo(typeof(MainPage));
    }

    private bool EnsureReady()
    {
        if (App.SupabaseReady)
            return true;
        ShowError(App.BootstrapError ?? Strings.Get("Login_SupabaseUnavailable"));
        return false;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        SubmitButton.IsEnabled = !busy;
        GitHubButton.IsEnabled = !busy;
        DiscordButton.IsEnabled = !busy;
        ForgotButton.IsEnabled = !busy;
    }

    private void ShowError(string msg)
    {
        InfoBarMsg.IsOpen = false;
        ErrorBar.Title = msg;
        ErrorBar.IsOpen = true;
    }

    private void ShowInfo(string msg)
    {
        ErrorBar.IsOpen = false;
        InfoBarMsg.Severity = InfoBarSeverity.Success;
        InfoBarMsg.Title = msg;
        InfoBarMsg.IsOpen = true;
    }

    private void ShowInfoTransient(string msg)
    {
        ErrorBar.IsOpen = false;
        InfoBarMsg.Severity = InfoBarSeverity.Informational;
        InfoBarMsg.Title = msg;
        InfoBarMsg.IsOpen = true;
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
