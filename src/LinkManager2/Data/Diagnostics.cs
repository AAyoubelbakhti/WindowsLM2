using System;
using System.IO;

namespace LinkManager2.Data;

internal static class Diagnostics
{
    private static readonly object _gate = new();

    public static void Log(string context, Exception? ex = null)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkManager");
            Directory.CreateDirectory(dir);
            var line = ex is null
                ? $"{DateTime.Now:O}  {context}\n"
                : $"{DateTime.Now:O}  {context}: {ex.GetType().Name}: {ex.Message}\n";
            lock (_gate) File.AppendAllText(Path.Combine(dir, "diag.log"), line);
        }
        catch { }
    }
}
