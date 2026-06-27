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
        SubtitleText.Text = "Conectando con Supabase…";
        try { await App.State.WaitReadyAsync(); }
        catch (Exception ex) { ShowError($"Error conectando: {ex.Message}"); SetBusy(false); return; }

        SubtitleText.Text = "Inicia sesión para sincronizar tus enlaces.";
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

        if (SubmitButton is null || SignUpMode is null) return;
        SubmitButton.Content = SignUpMode.IsChecked == true ? "Crear cuenta" : "Iniciar sesión";
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
        if (_busy) return;
        if (!EnsureReady()) return;

        var email = EmailBox.Text.Trim();
        var pass = PasswordBox.Password;
        if (!IsValidEmail(email)) { ShowError("Introduce un correo válido."); EmailBox.Focus(FocusState.Programmatic); return; }
        if (pass.Length < 6) { ShowError("La contraseña tiene al menos 6 caracteres."); PasswordBox.Focus(FocusState.Programmatic); return; }

        await RunAsync(async () =>
        {
            if (SignUpMode.IsChecked == true)
            {
                await App.State.Auth.SignUpWithEmailAsync(email, pass);
                if (!App.State.Auth.IsAuthenticated)
                {
                    ShowInfo("Cuenta creada. Revisa tu correo para confirmarla y luego inicia sesión.");
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
        if (_busy || !EnsureReady()) return;
        var email = EmailBox.Text.Trim();
        if (!IsValidEmail(email))
        {
            ShowError("Escribe tu correo arriba antes de pedir el restablecimiento.");
            EmailBox.Focus(FocusState.Programmatic);
            return;
        }
        await RunAsync(async () =>
        {
            await App.State.Auth.SendPasswordResetEmailAsync(email);
            ShowInfo($"Te hemos enviado un correo a {email}.");
        });
    }

    private async void OnGitHubSignIn(object sender, RoutedEventArgs e) =>
        await RunWebOAuthAsync("github", "GitHub");

    private async void OnDiscordSignIn(object sender, RoutedEventArgs e) =>
        await RunWebOAuthAsync("discord", "Discord");

    private async Task RunWebOAuthAsync(string provider, string label)
    {
        if (_busy || !EnsureReady()) return;
        ShowInfoTransient($"Abriendo {label} en el navegador…");
        await RunAsync(async () =>
        {
            var ok = await OAuthListener.RunWebAssistedAsync(App.State.Auth, provider);
            if (!ok) { ShowError($"No se completó el inicio con {label}."); return; }
            GoToMain();
        });
    }

    private void OnSignUpWeb(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = WebSignUpUrl, UseShellExecute = true });
            ShowInfo("Abrimos la web para crear tu cuenta. Vuelve aquí cuando termines.");
        }
        catch (Exception ex) { ShowError($"No se pudo abrir el navegador: {ex.Message}"); }
    }

    private async Task RunAsync(Func<Task> body)
    {
        SetBusy(true);
        ErrorBar.IsOpen = false;
        try { await body(); }
        catch (Exception ex) { ShowError(AuthService.DescribeError(ex)); }
        finally { SetBusy(false); }
    }

    private void GoToMain()
    {
        var w = ((App)Application.Current).Window!;
        w.InstallSystemHooks();
        w.NavigateTo(typeof(MainPage));
    }

    private bool EnsureReady()
    {
        if (App.SupabaseReady) return true;
        ShowError(App.BootstrapError ?? "Supabase no está disponible.");
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
        if (string.IsNullOrWhiteSpace(email)) return false;
        try { _ = new MailAddress(email); return true; }
        catch { return false; }
    }
}
