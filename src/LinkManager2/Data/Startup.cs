using System;
using System.Linq;

namespace LinkManager2.Data;

/// <summary>
/// Startup command-line contract shared by the single-instance redirector, the jump list
/// and the tray. Parses the raw args into a strongly typed request and answers whether a
/// persisted session exists locally without touching the network.
/// </summary>
public enum StartupAction { None, Add, Search, SaveClipboard }

public readonly record struct StartupRequest(bool StartInTray, StartupAction Action)
{
    public static readonly StartupRequest Normal = new(false, StartupAction.None);
}

public static class Startup
{
    public const string TrayArg = "--tray";
    public const string AddArg = "--add";
    public const string SearchArg = "--search";
    public const string SaveClipboardArg = "--save-clipboard";

    /// <summary>Parses process/activation args, ignoring the leading executable path when present.</summary>
    public static StartupRequest Parse(string[]? args)
    {
        if (args is null || args.Length == 0) return StartupRequest.Normal;

        var startInTray = false;
        var action = StartupAction.None;
        foreach (var raw in args)
        {
            var arg = raw?.Trim();
            if (string.IsNullOrEmpty(arg)) continue;
            switch (arg.ToLowerInvariant())
            {
                case TrayArg: startInTray = true; break;
                case AddArg: action = StartupAction.Add; break;
                case SearchArg: action = StartupAction.Search; break;
                case SaveClipboardArg: action = StartupAction.SaveClipboard; break;
            }
        }
        return new StartupRequest(startInTray, action);
    }

    /// <summary>True when a persisted session file exists on this device (no network probe).</summary>
    public static bool HasLocalSession()
    {
        try { return new LocalFileSessionHandler().LoadSession() is { AccessToken.Length: > 0 }; }
        catch { return false; }
    }
}
