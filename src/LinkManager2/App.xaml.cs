using System;
using LinkManager2.Data;
using Microsoft.UI.Xaml;

namespace LinkManager2;

public partial class App : Application
{
    public MainWindow? Window { get; private set; }

    public const int Build = 10;

    public static AppState State { get; private set; } = null!;

    public static bool SupabaseReady { get; private set; }
    public static string? BootstrapError { get; private set; }

    public App()
    {
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

    private static void LogCrash(Exception? ex)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkManager");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "crash.log"),
                $"{DateTime.Now:O}\n{ex}");
        }
        catch {  }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
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
