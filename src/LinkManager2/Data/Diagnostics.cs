using System;
using System.IO;

namespace LinkManager2.Data;

internal static class Diagnostics
{
    private static readonly object _gate = new();

    private const long MaxLogBytes = 1024 * 1024;

    /// <summary>Appends a diagnostic line, rotating the file past a size cap. Fully best-effort: the
    /// logger swallows its own I/O failures because it has nowhere to report them.</summary>
    public static void Log(string context, Exception? ex = null)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LinkManager");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "diag.log");
            var line = ex is null
                ? $"{DateTime.Now:O}  {context}\n"
                : $"{DateTime.Now:O}  {context}: {ex.GetType().Name}: {ex.Message}\n";
            lock (_gate)
            {
                RotateIfTooLarge(path);
                File.AppendAllText(path, line);
            }
        }
        catch { }
    }

    /// <summary>Rotates the log to a single ".old" sibling once it grows past the size cap, so the
    /// append-only diagnostics file never grows without bound. Best-effort; failures are ignored.</summary>
    private static void RotateIfTooLarge(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxLogBytes) return;
            var old = path + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(path, old);
        }
        catch { }
    }
}
