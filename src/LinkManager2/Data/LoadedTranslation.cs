using System.Text.Json;

namespace LinkManager2.Data;

/// <summary>
/// The translation loaded from a file to try a language before it ships. It stays in the data folder until it is
/// removed and applies from the next start. It was checked against the version of the app that loaded it, so it is
/// dropped when the app is updated, and a marker keeps that until the window has said it once.
/// </summary>
internal static class LoadedTranslation
{
    private const string FileName = "translation.json";
    private const string TemporaryName = "translation.json.tmp";
    private const string RemovedMarkerName = "translation-removed";

    private static string? _folder;

    /// <summary>The data folder holding the translation; tests point it elsewhere.</summary>
    internal static string Folder
    {
        get => _folder ?? LocalPreferences.Folder;
        set => _folder = value;
    }

    private static string Path => System.IO.Path.Combine(Folder, FileName);

    private static string TemporaryPath => System.IO.Path.Combine(Folder, TemporaryName);

    private static string RemovedMarker => System.IO.Path.Combine(Folder, RemovedMarkerName);

    /// <summary>The saved translation, as read at start or saved since; null with none.</summary>
    public static Translation? Current { get; private set; }

    /// <summary>Reads the saved translation, keeping only the texts that still fit this version of the app. One
    /// saved by another version is removed and left pending to be told once. One that cannot be read, or that keeps
    /// no text, is logged and removed, so the app goes on without it and nothing is left that cannot be
    /// removed.</summary>
    public static void ReadSaved(string appVersion)
    {
        Current = null;
        try
        {
            if (!File.Exists(Path))
                return;
            var saved = Deserialize(File.ReadAllText(Path));
            if (saved is not null && saved.AppVersion != appVersion)
            {
                File.WriteAllText(RemovedMarker, appVersion);
                File.Delete(Path);
                return;
            }
            Current = saved is null ? null : Refiltered(saved);
            if (Current is not null)
                return;
            Diagnostics.Log("saved translation unusable");
            File.Delete(Path);
        }
        catch (Exception ex)
        {
            Diagnostics.Log("saved translation", ex);
        }
    }

    /// <summary>Saves <paramref name="translation"/> with the version of the app it was checked against. It is
    /// written aside and moved over the saved one, so a failure leaves that one whole. Any pending notice about a
    /// removed one is then forgotten, and a marker that cannot be deleted is only logged, as the notice is not told
    /// while a translation is loaded.</summary>
    /// <exception cref="IOException">The data folder could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The data folder could not be written.</exception>
    public static void Save(Translation translation, string appVersion)
    {
        var stamped = translation with { AppVersion = appVersion };
        File.WriteAllText(TemporaryPath, JsonSerializer.Serialize(stamped));
        File.Move(TemporaryPath, Path, overwrite: true);
        Current = stamped;
        try
        {
            File.Delete(RemovedMarker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log("translation notice", ex);
        }
    }

    /// <summary>True once after an update removed the translation, then false until it removes another. A marker
    /// found while a translation is loaded is a stale one and is only cleared.</summary>
    public static bool TakeRemovedNotice()
    {
        try
        {
            if (!File.Exists(RemovedMarker))
                return false;
            File.Delete(RemovedMarker);
            return Current is null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log("translation notice", ex);
            return false;
        }
    }

    /// <summary>Removes the loaded translation, so the app takes its own texts from the next start.</summary>
    /// <exception cref="IOException">The data folder could not be written.</exception>
    public static void Remove()
    {
        File.Delete(Path);
        Current = null;
    }

    /// <summary>The translation in <paramref name="json"/>, or null when it is not one: not JSON, or with no
    /// language, texts or version.</summary>
    private static Translation? Deserialize(string json)
    {
        Translation? saved;
        try
        {
            saved = JsonSerializer.Deserialize<Translation>(json);
        }
        catch (JsonException)
        {
            return null;
        }
        return saved is { Language: not null, Texts: not null, AppVersion: not null }
            ? saved
            : null;
    }

    /// <summary><paramref name="saved"/> with its language as .NET names it and only the texts that still fit the
    /// app, or null when the language is not one or no text fits.</summary>
    private static Translation? Refiltered(Translation saved)
    {
        if (!TranslationFile.TryLanguage(saved.Language, out var language))
            return null;
        var (texts, _) = TranslationFile.Accept(saved.Texts, Strings.Base);
        return texts.Count == 0 ? null : saved with { Language = language, Texts = texts };
    }
}
