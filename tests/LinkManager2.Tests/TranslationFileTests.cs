using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using LinkManager2.Data;
using Xunit;

namespace LinkManager2.Tests;

public class TranslationFileTests
{
    private static readonly Dictionary<string, string> BaseTexts = new()
    {
        ["Main_Title"] = "Links",
        ["App_LanguageTag"] = "en-US",
        ["Main_Count"] = "{0} of {1}",
        ["Main_Expires"] = "expires on {0:d}",
        ["Settings_SaveButton.Content"] = "Save",
        ["translator_credits"] = "",
    };

    private static readonly (string Name, string Value)[] Headers =
    [
        ("resmimetype", "text/microsoft-resx"),
        ("language", "uk"),
        ("project", "linkmanager"),
        ("format", "1"),
    ];

    private static MemoryStream Resw(
        IEnumerable<(string Name, string Value)> headers,
        params (string Key, string Text)[] texts
    )
    {
        var root = new XElement(
            "root",
            headers.Select(header => new XElement(
                "resheader",
                new XAttribute("name", header.Name),
                new XElement("value", header.Value)
            )),
            texts.Select(text => new XElement(
                "data",
                new XAttribute("name", text.Key),
                new XAttribute(XNamespace.Xml + "space", "preserve"),
                new XElement("value", text.Text)
            ))
        );
        return new MemoryStream(Encoding.UTF8.GetBytes(root.ToString()));
    }

    private static MemoryStream Resw(params (string Key, string Text)[] texts) =>
        Resw(Headers, texts);

    private static MemoryStream Raw(string content) => new(Encoding.UTF8.GetBytes(content));

    /// <summary><paramref name="bytes"/> as a stream that cannot tell its length, as a decompressing one.</summary>
    private static GZipStream WithoutLength(byte[] bytes)
    {
        var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
            gzip.Write(bytes);
        compressed.Position = 0;
        return new GZipStream(compressed, CompressionMode.Decompress);
    }

    private static (Translation Translation, int Discarded) Read(Stream file) =>
        TranslationFile.Read(file, BaseTexts);

    private static TranslationProblem Problem(Stream file) =>
        Assert.Throws<TranslationFileException>(() => Read(file)).Problem;

    private static string[] Pairs(IReadOnlyDictionary<string, string> texts) =>
        texts.Select(text => $"{text.Key}={text.Value}").Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void A_file_with_the_three_headers_loads_in_its_language()
    {
        var (translation, discarded) = Read(
            Resw(("Main_Title", "Посилання"), ("Settings_SaveButton.Content", "Зберегти"))
        );

        Assert.Equal("uk", translation.Language);
        Assert.Equal(
            ["Main_Title=Посилання", "Settings_SaveButton.Content=Зберегти"],
            Pairs(translation.Texts)
        );
        Assert.Equal(0, discarded);
    }

    [Theory]
    [InlineData("language")]
    [InlineData("project")]
    [InlineData("format")]
    public void A_file_without_one_of_the_three_headers_is_not_a_translation(string missing)
    {
        var file = Resw(
            Headers.Where(header => header.Name != missing),
            ("Main_Title", "Посилання")
        );

        Assert.Equal(TranslationProblem.NotATranslation, Problem(file));
    }

    [Theory]
    [InlineData("project", "bombiteclado")]
    [InlineData("project", "LinkManager")]
    [InlineData("format", "2")]
    [InlineData("language", "")]
    [InlineData("language", "es_ES")]
    [InlineData("language", "x")]
    [InlineData("language", "abc-DEF-ghi")]
    public void A_file_of_another_project_or_format_or_with_no_real_language_is_not_a_translation(
        string header,
        string value
    )
    {
        var headers = Headers.Select(h => h.Name == header ? (h.Name, value) : h);

        Assert.Equal(
            TranslationProblem.NotATranslation,
            Problem(Resw(headers, ("Main_Title", "Посилання")))
        );
    }

    [Fact]
    public void The_language_is_spelled_as_dotnet_names_it()
    {
        var headers = Headers.Select(h => h.Name == "language" ? (h.Name, "pt-br") : h);

        Assert.Equal("pt-BR", Read(Resw(headers, ("Main_Title", "Links"))).Translation.Language);
    }

    [Theory]
    [InlineData("Українська")]
    [InlineData("uk\nX")]
    [InlineData("")]
    public void The_language_of_the_app_is_kept_only_as_a_language_tag(string tag)
    {
        var (translation, discarded) = Read(
            Resw(("Main_Title", "Посилання"), ("App_LanguageTag", tag))
        );

        Assert.Equal(["Main_Title=Посилання"], Pairs(translation.Texts));
        Assert.Equal(1, discarded);
    }

    [Fact]
    public void The_language_of_the_app_is_spelled_as_dotnet_names_it()
    {
        var (translation, _) = Read(
            Resw(("Main_Title", "Посилання"), ("App_LanguageTag", "pt-br"))
        );

        Assert.Equal("pt-BR", translation.Texts["App_LanguageTag"]);
    }

    [Fact]
    public void Texts_the_app_does_not_have_are_discarded_and_counted()
    {
        var (translation, discarded) = Read(
            Resw(
                ("Main_Title", "Посилання"),
                ("Main_Gone", "Зникло"),
                ("Settings_SaveButton.Header", "Зберегти")
            )
        );

        Assert.Equal(["Main_Title=Посилання"], Pairs(translation.Texts));
        Assert.Equal(2, discarded);
    }

    [Theory]
    [InlineData("{1} з {0}", true)]
    [InlineData("{0} з {1}, {1}", true)]
    [InlineData("{0, 3} з {1}", false)]
    [InlineData("{0}", false)]
    [InlineData("{0} з {1} {2}", false)]
    [InlineData("{0:N0} з {1}", false)]
    [InlineData("{0} з {1", false)]
    [InlineData("{0} з {1} }", false)]
    [InlineData("{{0}} з {1}", false)]
    public void A_text_is_kept_only_with_the_placeholders_of_the_base(string text, bool kept)
    {
        var (translation, discarded) = Read(
            Resw(("Main_Title", "Посилання"), ("Main_Count", text))
        );

        Assert.Equal(kept, translation.Texts.ContainsKey("Main_Count"));
        Assert.Equal(kept ? 0 : 1, discarded);
    }

    [Theory]
    [InlineData("до {0:d}", true)]
    [InlineData("до {0}", false)]
    public void The_format_of_a_placeholder_is_part_of_it(string text, bool kept)
    {
        Assert.Equal(
            kept,
            Read(Resw(("Main_Title", "Посилання"), ("Main_Expires", text)))
                .Translation.Texts.ContainsKey("Main_Expires")
        );
    }

    [Fact]
    public void A_repeated_key_keeps_its_first_text_and_counts_the_rest_as_discarded()
    {
        var (translation, discarded) = Read(
            Resw(("Main_Title", "Посилання"), ("Main_Title", "Лінки"), ("Main_Title", "{0}"))
        );

        Assert.Equal(["Main_Title=Посилання"], Pairs(translation.Texts));
        Assert.Equal(2, discarded);
    }

    [Fact]
    public void A_file_with_no_text_the_app_can_use_is_refused()
    {
        Assert.Equal(TranslationProblem.NoUsableText, Problem(Resw()));
        Assert.Equal(
            TranslationProblem.NoUsableText,
            Problem(Resw(("Main_Gone", "Зникло"), ("Main_Count", "{0}")))
        );
    }

    [Fact]
    public void A_stream_that_cannot_tell_its_length_is_read_up_to_the_limit()
    {
        var resw = Resw(("Main_Title", "Посилання")).ToArray();

        Assert.Equal("uk", Read(WithoutLength(resw)).Translation.Language);
        Assert.Equal(
            TranslationProblem.TooLarge,
            Problem(WithoutLength(new byte[TranslationFile.MaxBytes + 1]))
        );
    }

    [Fact]
    public void A_blank_text_is_kept_only_where_the_base_is_blank()
    {
        var (translation, discarded) = Read(Resw(("Main_Title", "  "), ("translator_credits", "")));

        Assert.Equal(["translator_credits="], Pairs(translation.Texts));
        Assert.Equal(1, discarded);
    }

    [Fact]
    public void A_file_over_the_size_limit_is_too_large()
    {
        Assert.Equal(
            TranslationProblem.TooLarge,
            Problem(new MemoryStream(new byte[TranslationFile.MaxBytes + 1]))
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml at all")]
    [InlineData("<root><data name=\"Main_Title\"><value>Посилання")]
    public void A_file_that_is_not_xml_is_unreadable(string content)
    {
        Assert.Equal(TranslationProblem.Unreadable, Problem(Raw(content)));
    }

    [Theory]
    [InlineData("<!DOCTYPE root [<!ENTITY big \"Посилання\">]>")]
    [InlineData("<!DOCTYPE root [<!ENTITY file SYSTEM \"file:///C:/Windows/win.ini\">]>")]
    public void A_file_with_a_dtd_is_refused_before_any_entity_is_read(string doctype)
    {
        var file = Raw(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            {doctype}
            <root>
              <resheader name="language"><value>uk</value></resheader>
              <resheader name="project"><value>linkmanager</value></resheader>
              <resheader name="format"><value>1</value></resheader>
              <data name="Main_Title"><value>&big;</value></data>
            </root>
            """
        );

        Assert.Equal(TranslationProblem.Unreadable, Problem(file));
    }

    [Fact]
    public void A_saved_text_that_no_longer_fits_the_app_is_dropped()
    {
        var (texts, discarded) = TranslationFile.Accept(
            new Dictionary<string, string> { ["Main_Title"] = "Посилання", ["Main_Count"] = "{0}" },
            BaseTexts
        );

        Assert.Equal(["Main_Title=Посилання"], Pairs(texts));
        Assert.Equal(1, discarded);
    }

    [Fact]
    public void Every_language_the_app_ships_loads_whole_as_a_translation()
    {
        var assembly = typeof(TranslationFileTests).Assembly;
        var shipped = assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith("Strings/"))
            .ToList();
        Assert.NotEmpty(shipped);
        foreach (var name in shipped)
        {
            var language = name.Split('/', '\\')[1];
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            var resw = reader.ReadToEnd();
            var file = resw.Replace(
                "<root>",
                "<root>"
                    + $"<resheader name=\"language\"><value>{language}</value></resheader>"
                    + "<resheader name=\"project\"><value>linkmanager</value></resheader>"
                    + "<resheader name=\"format\"><value>1</value></resheader>"
            );

            var (translation, discarded) = TranslationFile.Read(Raw(file), Strings.Base);

            Assert.Equal(0, discarded);
            Assert.Equal(TranslationFile.ReadTexts(Raw(resw)).Count, translation.Texts.Count);
        }
    }
}
