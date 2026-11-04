using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using LinkManager2.Data;
using Microsoft.UI.Xaml;

namespace LinkManager2;

/// <summary>
/// The UI language: the one saved in the local preferences or, with none, Windows'. It is set once, before any
/// text loads, because x:Uid texts resolve as each page loads; a new choice takes effect on the next start.
/// </summary>
internal static class AppLanguage
{
    private const string AfterArg = "--after";
    private const int WaitForPreviousMs = 15000;
    private const string LanguagesMetadataKey = "AppLanguages";
    private const uint MuiLanguageName = 0x8;

    private static readonly string[] Supported = typeof(AppLanguage)
        .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(metadata => metadata.Key == LanguagesMetadataKey)
        .Value!.Split(';');

    /// <summary>The preference applied when this process started: "" follows Windows.</summary>
    public static string AtStart { get; private set; } = "";

    /// <summary>The languages this build carries, as the project listed them from Strings/&lt;tag&gt;/Resources.resw.</summary>
    public static IReadOnlyList<(string Tag, string Name)> Offered =>
        LanguageNames.Autonyms(Supported);

    /// <summary>The direction of the language the texts came out in, chosen or following Windows. A bad tag in a
    /// text file gives left to right, so it cannot keep the window from opening.</summary>
    public static FlowDirection Direction
    {
        get
        {
            try
            {
                return CultureInfo.GetCultureInfo(Strings.Get("App_LanguageTag")).TextInfo.IsRightToLeft
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;
            }
            catch (CultureNotFoundException ex)
            {
                Diagnostics.Log("language direction", ex);
                return FlowDirection.LeftToRight;
            }
        }
    }

    /// <summary>The saved preference as one of the app's languages, or "" (follow Windows) for anything else.</summary>
    public static string Normalize(string? language) =>
        language is not null && Array.IndexOf(Supported, language) >= 0 ? language : "";

    /// <summary>Applies the saved UI language to resource lookups and to the texts WinUI brings for its controls,
    /// which come from .mui folders named with a region; call it before the first window.</summary>
    public static void Apply()
    {
        AtStart = Normalize(LocalPreferences.Load().Language);
        if (AtStart.Length == 0)
            return;
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = AtStart;
            var region = CultureInfo.CreateSpecificCulture(AtStart).Name;
            if (!SetProcessPreferredUILanguages(MuiLanguageName, region + '\0', out _))
                throw new Win32Exception();
        }
        catch (Exception ex)
        {
            Diagnostics.Log("language override", ex);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetProcessPreferredUILanguages(
        uint flags,
        string languages,
        out uint count
    );

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
