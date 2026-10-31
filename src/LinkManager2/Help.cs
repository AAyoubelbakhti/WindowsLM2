using LinkManager2.Data;

namespace LinkManager2;

internal static class Help
{
    public static string Title => Strings.Get("Help_Title");

    /// <summary>Shortcut help naming the global shortcut currently configured in the settings.</summary>
    public static string Body
    {
        get
        {
            var prefs = LocalPreferences.Load();
            return Strings.Format(
                "Help_Body",
                GlobalHotkey.Describe(prefs.HotkeyModifiers, prefs.HotkeyVirtualKey)
            );
        }
    }
}
