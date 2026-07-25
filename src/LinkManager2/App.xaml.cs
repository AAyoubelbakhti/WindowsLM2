using System;
using LinkManager2.Data;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace LinkManager2;

public partial class App : Application
{
    public MainWindow? Window { get; private set; }

    public const int Build = 21;

    /// <summary>Semantic app version reported to the backend (devices.app_version); keep in sync with Build and Velopack packVersion.</summary>
    public const string Version = "1.7.0";

    public static AppState State { get; private set; } = null!;

    public static bool SupabaseReady { get; private set; }
    public static string? BootstrapError { get; private set; }

    private static App? _current;

    public App()
    {
        _current = this;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            LogCrash(e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            LogCrash(e.ExceptionObject as Exception);
        };
        Bootstrap();
    }

    private const int MaxCrashLogChars = 1024 * 1024;

    private static void LogCrash(Exception? ex)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkManager");
            System.IO.Directory.CreateDirectory(dir);
            var text = $"{DateTime.Now:O}\n{ex}";
            if (text.Length > MaxCrashLogChars) text = text[..MaxCrashLogChars];
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "crash.log"), text);
        }
        catch {  }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Notifications.Register();
        TaskbarJumpList.Configure();

        var request = Startup.Parse(Environment.GetCommandLineArgs());
        var hasLocalSession = App.SupabaseReady && Startup.HasLocalSession();

        Window = new MainWindow();
        Window.PrepareStartup(hasLocalSession);

        if (request.StartInTray && hasLocalSession)
        {
            Window.StartHiddenInTray();
        }
        else
        {
            Window.Activate();
        }

        if (request.Action != StartupAction.None) Window.RouteStartupAction(request.Action);
    }

    /// <summary>Handles activation redirected from a secondary instance: surfaces the window and routes the CLI arg.</summary>
    public static void OnActivatedFromOtherInstance(object? sender, AppActivationArguments e)
    {
        var app = _current;
        if (app is null) return;
        var request = Startup.Parse(ExtractArgs(e));
        app.Window?.DispatcherQueue.TryEnqueue(() =>
        {
            app.Window.ShowFromTray();
            if (request.Action != StartupAction.None) app.Window.RouteStartupAction(request.Action);
        });
    }

    private static string[]? ExtractArgs(AppActivationArguments e)
    {
        try
        {
            if (e.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch)
                return SplitArgs(launch.Arguments);
        }
        catch (Exception ex) { Diagnostics.Log("extract activation args", ex); }
        return null;
    }

    private static string[] SplitArgs(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return Array.Empty<string>();
        return commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void Bootstrap()
    {
        var (url, key) = AppState.LoadConfig();
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            BootstrapError =
                "Falta configuración de Supabase. Define LM_SUPABASE_URL y LM_SUPABASE_ANON_KEY " +
                "o coloca appsettings.Local.json junto al ejecutable.";
            return;
        }
        try
        {

            SupabaseClientHolder.Init(url, key);
            State = AppState.Create();
            SupabaseReady = true;
        }
        catch (Exception ex)
        {
            BootstrapError = $"No se pudo iniciar Supabase: {ex.Message}";
        }
    }
}
