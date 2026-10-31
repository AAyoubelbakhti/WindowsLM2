using System.Diagnostics;
using LinkManager2.Data;

namespace LinkManager2;

/// <summary>
/// The UI language: the one saved in the local preferences or, with none, Windows'. It is set once, before any
/// text loads, because x:Uid texts resolve as each page loads; a new choice takes effect on the next start.
/// </summary>
internal static class AppLanguage
{
    private const string AfterArg = "--after";
    private const int WaitForPreviousMs = 15000;
    private static readonly string[] Supported = { "es-ES", "en-US" };

    /// <summary>The preference applied when this process started: "" follows Windows.</summary>
    public static string AtStart { get; private set; } = "";

    /// <summary>The saved preference as one of the app's languages, or "" (follow Windows) for anything else.</summary>
    public static string Normalize(string? language) =>
        language is not null && Array.IndexOf(Supported, language) >= 0 ? language : "";

    /// <summary>Applies the saved UI language to resource lookups; call it before the first window.</summary>
    public static void Apply()
    {
        AtStart = Normalize(LocalPreferences.Load().Language);
        if (AtStart.Length == 0)
            return;
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = AtStart;
        }
        catch (Exception ex)
        {
            Diagnostics.Log("language override", ex);
        }
    }

    /// <summary>Starts a new instance that waits for this one to close, so it opens in the saved language as the
    /// primary instance. False when it could not start, and then this one should keep running.</summary>
    public static bool StartAfterThisOne()
    {
        try
        {
            using var next = Process.Start(
                new ProcessStartInfo(Environment.ProcessPath!)
                {
                    UseShellExecute = false,
                    ArgumentList = { AfterArg, Environment.ProcessId.ToString() },
                }
            );
            return next is not null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log("restart", ex);
            return false;
        }
    }

    /// <summary>Gives up the single-instance key just before a restart exits, as an exit without it can leave the
    /// key pointing at the closed instance and the new one would redirect to it.</summary>
    public static void ReleaseInstanceKey()
    {
        try
        {
            Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().UnregisterKey();
        }
        catch (Exception ex)
        {
            Diagnostics.Log("restart key", ex);
        }
    }

    /// <summary>When a restart started this process, waits for the instance that started it to close, so the
    /// single-instance key is free; any other start goes on at once.</summary>
    public static void WaitForPrevious(string[] args)
    {
        var index = Array.IndexOf(args, AfterArg);
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var pid))
            return;
        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(WaitForPreviousMs);
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (Exception ex)
        {
            Diagnostics.Log("restart wait", ex);
        }
    }
}
