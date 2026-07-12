using System;
using System.Threading.Tasks;
using Supabase.Gotrue;

namespace LinkManager2.Data;

public sealed class AuthService
{
    private readonly Supabase.Client _client = SupabaseClientHolder.Client;

    public Session? CurrentSession => _client.Auth.CurrentSession;
    public User? CurrentUser => _client.Auth.CurrentUser;
    public string? UserEmail => CurrentUser?.Email;
    public bool IsAuthenticated => CurrentSession is not null && CurrentUser is not null;

    public async Task SignInWithEmailAsync(string email, string password)
    {
        var session = await _client.Auth.SignIn(email, password);
        if (session is null)
            throw new InvalidOperationException("Supabase devolvió sesión vacía.");
    }

    public Task SignUpWithEmailAsync(string email, string password) =>
        _client.Auth.SignUp(email, password);

    public Task SignOutAsync() => _client.Auth.SignOut();

    public Task SendPasswordResetEmailAsync(string email) =>
        _client.Auth.ResetPasswordForEmail(email);

    public Task SetSessionFromTokensAsync(string accessToken, string refreshToken) =>
        _client.Auth.SetSession(accessToken, refreshToken);

    public async Task<bool> TryRefreshSessionAsync()
    {
        try
        {
            await _client.Auth.RefreshSession();
            return CurrentSession is not null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log("refresh-session", ex);
            return false;
        }
    }

    public static bool IsAuthExpired(Exception? ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is Supabase.Postgrest.Exceptions.PostgrestException pex && pex.StatusCode == 401)
                return true;
            var msg = cur.Message ?? string.Empty;
            if (msg.Contains("JWT expired", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string DescribeError(Exception ex)
    {
        if (ex is OperationCanceledException) return "Operación cancelada.";
        var msg = ex.Message ?? string.Empty;
        if (msg.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("credential", StringComparison.OrdinalIgnoreCase))
            return "Credenciales inválidas. Revisa el correo y la contraseña.";
        if (msg.Contains("already registered", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("already been registered", StringComparison.OrdinalIgnoreCase))
            return "Ese correo ya tiene cuenta. Inicia sesión.";
        if (msg.Contains("rate", StringComparison.OrdinalIgnoreCase))
            return "Demasiados intentos. Espera unos minutos.";
        if (msg.Contains("not confirmed", StringComparison.OrdinalIgnoreCase))
            return "Confirma tu correo antes de iniciar sesión.";
        return msg.Length > 0 ? msg : ex.GetType().Name;
    }
}
