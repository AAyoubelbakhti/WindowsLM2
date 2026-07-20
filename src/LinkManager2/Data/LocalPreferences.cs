using System.Text.Json;

namespace LinkManager2.Data;

public sealed class LocalPreferences
{
    public bool MicaBackdrop { get; set; } = true;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool GlobalHotkeyEnabled { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Preferred UI theme: "system" (follow Windows), "light", or "dark".</summary>
    public string Theme { get; set; } = "system";
    public uint HotkeyModifiers { get; set; } = 0x0002 | 0x0004;
    public uint HotkeyVirtualKey { get; set; } = 0x4C;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowWidth { get; set; }
    public int? WindowHeight { get; set; }

    private static string Path
    {
        get
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LinkManager");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "preferences.json");
        }
    }

    /// <summary>Returns a stable per-device id, creating and persisting one on first use. Read/write
    /// failures are swallowed and fall back to an in-memory id so device registration never crashes.</summary>
    public static string GetOrCreateDeviceId()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinkManager");
        Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "device_id");
        try
        {
            if (File.Exists(file))
            {
                var existing = File.ReadAllText(file).Trim();
                if (!string.IsNullOrWhiteSpace(existing)) return existing;
            }
        }
        catch {  }

        var id = Guid.NewGuid().ToString();
        try { File.WriteAllText(file, id); } catch {  }
        return id;
    }

    public static LocalPreferences Load()
    {
        try
        {
            if (!File.Exists(Path)) return new LocalPreferences();
            var text = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<LocalPreferences>(text) ?? new LocalPreferences();
        }
        catch
        {
            return new LocalPreferences();
        }
    }

    public void Save()
    {
        try
        {
            var text = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path, text);
        }
        catch (Exception ex) { Diagnostics.Log("preferences save", ex); }
    }
}
