using System.Text.Json;

namespace LinkManager2.Data;

public sealed class LocalPreferences
{
    public bool MicaBackdrop { get; set; } = true;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool GlobalHotkeyEnabled { get; set; } = true;

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
        catch {  }
    }
}
