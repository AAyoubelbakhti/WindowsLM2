using System.Globalization;
using LinkManager2.Data;

namespace LinkManager2;

/// <summary>
/// User-facing texts: those of a loaded translation first, then the app's own from
/// Strings/{language}/Resources.resw. A missing key returns the key itself and is logged, so a typo shows on screen
/// instead of closing the app.
/// </summary>
internal static partial class Strings
{
    private const string BaseResource = "Base.resw";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> BaseTexts = new(ReadBase);

    private static readonly Lazy<ILookup<string, string>> KeysByUid = new(IndexUids);

    private static IReadOnlyDictionary<string, string> _loaded = new Dictionary<string, string>();

    /// <summary>The app's texts in its default language, as the build carries them; a translation is checked
    /// against them.</summary>
    public static IReadOnlyDictionary<string, string> Base => BaseTexts.Value;

    /// <summary>The keys of the texts an element with <paramref name="uid"/> takes, such as
    /// "Settings_About.Content".</summary>
    public static IEnumerable<string> KeysOf(string uid) => KeysByUid.Value[uid];

    /// <summary>Puts <paramref name="texts"/> before the app's own; call it before any text loads.</summary>
    public static void Load(IReadOnlyDictionary<string, string> texts) => _loaded = texts;

    public static string Get(string key) =>
        _loaded.TryGetValue(key, out var text) ? text : Shipped(key) ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>The app's own text of <paramref name="key"/> in the UI language, or null, logged, when it has
    /// none.</summary>
    private static partial string? Shipped(string key);

    private static IReadOnlyDictionary<string, string> ReadBase()
    {
        using var resw = typeof(Strings).Assembly.GetManifestResourceStream(BaseResource)!;
        return TranslationFile.ReadTexts(resw);
    }

    private static ILookup<string, string> IndexUids() =>
        Base
            .Keys.Select(key =>
                (Key: key, Uid: UidKeys.TrySplit(key, out var uid, out _) ? uid : null)
            )
            .Where(entry => entry.Uid is not null)
            .ToLookup(entry => entry.Uid!, entry => entry.Key);
}
