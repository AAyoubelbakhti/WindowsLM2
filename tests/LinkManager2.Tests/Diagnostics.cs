namespace LinkManager2.Data;

/// <summary>Stands in for the app's log, which writes to the data folder of the installed app.</summary>
internal static class Diagnostics
{
    public static void Log(string context, Exception? ex = null) { }
}
