using System.Globalization;

namespace LinkManager2.Data;

/// <summary>The names of the languages in the language setting, with no WinUI so the tests can link this file.</summary>
internal static class LanguageNames
{
    /// <summary>Each tag with its language said in that language, in alphabetical order. The region shows only when
    /// two tags share a language, as pt-BR and pt-PT do.</summary>
    public static IReadOnlyList<(string Tag, string Name)> Autonyms(IEnumerable<string> tags) =>
        tags.GroupBy(tag => CultureInfo.GetCultureInfo(tag).TwoLetterISOLanguageName)
            .SelectMany(language =>
                language.Select(tag =>
                    (Tag: tag, Name: OwnName(language.Count() == 1 ? language.Key : tag))
                )
            )
            .OrderBy(option => option.Name, StringComparer.InvariantCulture)
            .ToList();

    private static string OwnName(string tag)
    {
        var culture = CultureInfo.GetCultureInfo(tag);
        var name = culture.NativeName;
        var first = culture.TextInfo.ToUpper(name[0]);
        return IsMtavruli(first) ? name : first + name[1..];
    }

    /// <summary>Georgian capitals are only for all-caps text, so a name never starts with one.</summary>
    private static bool IsMtavruli(char letter) => letter is >= '\u1C90' and <= '\u1CBF';
}
