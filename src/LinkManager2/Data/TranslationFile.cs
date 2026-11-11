using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace LinkManager2.Data;

/// <summary>Why a file was not taken as a translation.</summary>
internal enum TranslationProblem
{
    NotATranslation,
    TooLarge,
    Unreadable,
    NoUsableText,
}

/// <summary>A file refused as a translation of the app.</summary>
internal sealed class TranslationFileException(TranslationProblem problem, Exception? cause = null)
    : Exception(problem.ToString(), cause)
{
    public TranslationProblem Problem { get; } = problem;
}

/// <summary>A translation the app can show: its language, its texts by .resw name, when it was loaded and the
/// version of the app it was checked against, empty until it is saved.</summary>
internal sealed record Translation(
    string Language,
    IReadOnlyDictionary<string, string> Texts,
    DateTimeOffset Loaded,
    string AppVersion = ""
);

/// <summary>
/// The file the translators portal gives to try a language in the app before it ships: a .resw like
/// Strings/&lt;language&gt;/Resources.resw with three more headers, language, project and format.
/// </summary>
internal static partial class TranslationFile
{
    /// <summary>Several times the size of a whole translation, so only a file that is not one goes over it.</summary>
    public const int MaxBytes = 1024 * 1024;

    private const string Project = "linkmanager";
    private const string Format = "1";

    /// <summary>Reads <paramref name="file"/> as a translation of this app, keeping each text whose key
    /// <paramref name="baseTexts"/> has with the same placeholders; the rest count as discarded. The file is read up
    /// to the size limit without asking its length, which some streams cannot tell.</summary>
    /// <exception cref="TranslationFileException">Too large, not XML, not a translation of this app or with no text
    /// the app can use.</exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static (Translation Translation, int Discarded) Read(
        Stream file,
        IReadOnlyDictionary<string, string> baseTexts
    )
    {
        var root = Parse(Capped(file));
        var headers = Values(root, "resheader");
        if (
            headers.GetValueOrDefault("project") != Project
            || headers.GetValueOrDefault("format") != Format
            || !TryLanguage(headers.GetValueOrDefault("language"), out var language)
        )
            throw new TranslationFileException(TranslationProblem.NotATranslation);
        var (texts, discarded) = Accept(Entries(root, "data"), baseTexts);
        if (texts.Count == 0)
            throw new TranslationFileException(TranslationProblem.NoUsableText);
        return (new Translation(language, texts, DateTimeOffset.Now), discarded);
    }

    /// <summary>The texts of a .resw by name, such as the base file the app carries.</summary>
    public static Dictionary<string, string> ReadTexts(Stream resw) => Values(Parse(resw), "data");

    /// <summary>The texts of <paramref name="texts"/> that fit the app: <paramref name="baseTexts"/> has the key,
    /// the placeholders are the same and a text is blank only where the base one is. The rest count as discarded, as
    /// does a missing text, which a saved file can hold, and each repeat of a key, which keeps its first
    /// text.</summary>
    public static (Dictionary<string, string> Texts, int Discarded) Accept(
        IEnumerable<KeyValuePair<string, string>> texts,
        IReadOnlyDictionary<string, string> baseTexts
    )
    {
        var accepted = new Dictionary<string, string>();
        var seen = new HashSet<string>();
        var discarded = 0;
        foreach (var (key, text) in texts)
        {
            if (
                seen.Add(key)
                && text is not null
                && baseTexts.TryGetValue(key, out var baseText)
                && Fits(text, baseText)
            )
                accepted.Add(key, text);
            else
                discarded++;
        }
        return (accepted, discarded);
    }

    /// <summary>The bytes of <paramref name="file"/>, read to one past the size limit to tell a file over it.</summary>
    /// <exception cref="TranslationFileException">The file is over the size limit.</exception>
    private static MemoryStream Capped(Stream file)
    {
        var bytes = new byte[MaxBytes + 1];
        var length = file.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (length > MaxBytes)
            throw new TranslationFileException(TranslationProblem.TooLarge);
        return new MemoryStream(bytes, 0, length, writable: false);
    }

    /// <summary>The root of an XML file read with no DTD, so it can neither define entities nor fetch any.</summary>
    private static XElement Parse(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxBytes,
        };
        try
        {
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root!;
        }
        catch (XmlException ex)
        {
            throw new TranslationFileException(TranslationProblem.Unreadable, ex);
        }
    }

    /// <summary>The name attribute and value of each element of the root named <paramref name="element"/>, as the
    /// resheader and data elements of a .resw keep them, in file order and with any repeat.</summary>
    private static IEnumerable<KeyValuePair<string, string>> Entries(XElement root, string element)
    {
        foreach (var entry in root.Elements(element))
            if (entry.Attribute("name")?.Value is { } name && entry.Element("value") is { } value)
                yield return KeyValuePair.Create(name, value.Value);
    }

    /// <summary>The value of each element of the root named <paramref name="element"/> by its name, the last one
    /// standing for a repeated name.</summary>
    private static Dictionary<string, string> Values(XElement root, string element)
    {
        var values = new Dictionary<string, string>();
        foreach (var (name, value) in Entries(root, element))
            values[name] = value;
        return values;
    }

    /// <summary>The tag as .NET names it, when it is a well-formed language tag .NET can make a culture of.</summary>
    public static bool TryLanguage(string? tag, out string language)
    {
        language = "";
        if (tag is null || !LanguageTag().IsMatch(tag))
            return false;
        try
        {
            language = CultureInfo.GetCultureInfo(tag).Name;
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Whether <paramref name="text"/> can stand for <paramref name="baseText"/>: blank only where the base
    /// is, with the same format items, and no brace that string.Format would choke on unless the base has it.</summary>
    private static bool Fits(string text, string baseText)
    {
        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(baseText))
            return false;
        if (!Arguments(text).SetEquals(Arguments(baseText)))
            return false;
        var count = FormatItem()
            .Matches(baseText)
            .Where(item => item.Groups["index"].Success)
            .Select(item => int.Parse(item.Groups["index"].Value, CultureInfo.InvariantCulture) + 1)
            .DefaultIfEmpty(0)
            .Max();
        return Formats(text, count) || !Formats(baseText, count);
    }

    /// <summary>Each format item of <paramref name="text"/> as written, index, alignment and format alike, so
    /// "{0}" and "{0:d}" are different arguments, as the portal checks them.</summary>
    private static HashSet<string> Arguments(string text) =>
        FormatItem()
            .Matches(text)
            .Where(item => item.Groups["index"].Success)
            .Select(item =>
                $"{{{item.Groups["index"].Value}{Spaces().Replace(item.Groups["alignment"].Value, "")}{item.Groups["format"].Value}}}"
            )
            .ToHashSet();

    private static bool Formats(string text, int argumentCount)
    {
        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, text, new object?[argumentCount]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [GeneratedRegex(
        @"\{\{|\}\}|\{(?<index>\d+)\s*(?<alignment>,\s*-?\d+\s*)?(?<format>:[^{}]*)?\}"
    )]
    private static partial Regex FormatItem();

    [GeneratedRegex(@"\s")]
    private static partial Regex Spaces();

    [GeneratedRegex("^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*$")]
    private static partial Regex LanguageTag();
}
