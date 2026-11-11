using LinkManager2.Data;
using Xunit;

namespace LinkManager2.Tests;

public sealed class LoadedTranslationTests : IDisposable
{
    private const string Save = "Settings_SaveButton.Content";
    private const string Version = "1.9.4";
    private const string NextVersion = "1.9.5";

    private readonly string _folder = Directory.CreateTempSubdirectory("lm-translation").FullName;

    public LoadedTranslationTests()
    {
        LoadedTranslation.Folder = _folder;
        LoadedTranslation.Remove();
    }

    public void Dispose() => Directory.Delete(_folder, true);

    private string Saved => Path.Combine(_folder, "translation.json");

    private string Marker => Path.Combine(_folder, "translation-removed");

    private static Translation Ukrainian() =>
        new("uk", new Dictionary<string, string> { [Save] = "Зберегти" }, DateTimeOffset.Now);

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"Language\":\"uk\",\"Texts\":{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData(
        "{\"Language\":null,\"Texts\":{\"Settings_SaveButton.Content\":\"Зберегти\"},\"AppVersion\":\"1.9.4\"}"
    )]
    [InlineData("{\"Language\":\"uk\",\"Texts\":null,\"AppVersion\":\"1.9.4\"}")]
    [InlineData(
        "{\"Language\":\"uk\",\"Texts\":{\"Settings_SaveButton.Content\":null,\"translator_credits\":null},\"AppVersion\":\"1.9.4\"}"
    )]
    [InlineData(
        "{\"Language\":\"uk\",\"Texts\":{\"Settings_SaveButton.Content\":\"Зберегти\"},\"AppVersion\":null}"
    )]
    [InlineData(
        "{\"Language\":\"x\",\"Texts\":{\"Settings_SaveButton.Content\":\"Зберегти\"},\"AppVersion\":\"1.9.4\"}"
    )]
    public void A_saved_translation_that_cannot_be_read_is_removed_without_a_notice(string json)
    {
        File.WriteAllText(Saved, json);

        LoadedTranslation.ReadSaved(Version);

        Assert.Null(LoadedTranslation.Current);
        Assert.False(File.Exists(Saved));
        Assert.False(LoadedTranslation.TakeRemovedNotice());
    }

    [Fact]
    public void A_saved_translation_of_this_version_keeps_only_the_texts_that_still_fit()
    {
        LoadedTranslation.Save(
            Ukrainian() with
            {
                Texts = new Dictionary<string, string>
                {
                    [Save] = "Зберегти",
                    ["Main_NoSuchText"] = "Немає",
                    ["Settings_AboutVersion"] = "LinkManager",
                },
            },
            Version
        );

        LoadedTranslation.ReadSaved(Version);

        Assert.Equal([Save], LoadedTranslation.Current!.Texts.Keys);
    }

    [Fact]
    public void A_saved_translation_with_no_text_that_fits_is_removed()
    {
        LoadedTranslation.Save(
            Ukrainian() with
            {
                Texts = new Dictionary<string, string> { ["Main_NoSuchText"] = "Немає" },
            },
            Version
        );

        LoadedTranslation.ReadSaved(Version);

        Assert.Null(LoadedTranslation.Current);
        Assert.False(File.Exists(Saved));
    }

    [Fact]
    public void Saving_over_a_translation_replaces_it_and_leaves_no_temporary_file()
    {
        LoadedTranslation.Save(Ukrainian(), Version);
        LoadedTranslation.Save(
            Ukrainian() with
            {
                Texts = new Dictionary<string, string> { [Save] = "Записати" },
            },
            Version
        );

        LoadedTranslation.ReadSaved(Version);

        Assert.Equal("Записати", LoadedTranslation.Current!.Texts[Save]);
        Assert.Equal([Saved], Directory.GetFiles(_folder));
    }

    [Fact]
    public void A_marker_that_cannot_be_deleted_on_saving_keeps_the_translation_and_is_never_told()
    {
        using (File.Open(Marker, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            LoadedTranslation.Save(Ukrainian(), Version);

        Assert.NotNull(LoadedTranslation.Current);
        Assert.True(File.Exists(Marker));

        LoadedTranslation.ReadSaved(Version);

        Assert.Equal("Зберегти", LoadedTranslation.Current!.Texts[Save]);
        Assert.False(LoadedTranslation.TakeRemovedNotice());
        Assert.False(File.Exists(Marker));
    }

    [Fact]
    public void The_same_version_keeps_the_translation_without_a_notice()
    {
        LoadedTranslation.Save(Ukrainian(), Version);

        LoadedTranslation.ReadSaved(Version);

        Assert.Equal("Зберегти", LoadedTranslation.Current!.Texts[Save]);
        Assert.False(LoadedTranslation.TakeRemovedNotice());
    }

    [Fact]
    public void Another_version_removes_the_translation_and_leaves_a_notice_pending()
    {
        LoadedTranslation.Save(Ukrainian(), Version);

        LoadedTranslation.ReadSaved(NextVersion);

        Assert.Null(LoadedTranslation.Current);
        Assert.False(File.Exists(Saved));
        Assert.True(LoadedTranslation.TakeRemovedNotice());
    }

    [Fact]
    public void The_notice_is_taken_once()
    {
        LoadedTranslation.Save(Ukrainian(), Version);
        LoadedTranslation.ReadSaved(NextVersion);

        Assert.True(LoadedTranslation.TakeRemovedNotice());
        Assert.False(LoadedTranslation.TakeRemovedNotice());
    }

    [Fact]
    public void Loading_another_translation_forgets_the_pending_notice()
    {
        LoadedTranslation.Save(Ukrainian(), Version);
        LoadedTranslation.ReadSaved(NextVersion);

        LoadedTranslation.Save(Ukrainian(), NextVersion);

        Assert.False(LoadedTranslation.TakeRemovedNotice());
    }

    [Fact]
    public void Without_a_saved_translation_there_is_no_notice()
    {
        LoadedTranslation.ReadSaved(Version);

        Assert.Null(LoadedTranslation.Current);
        Assert.False(LoadedTranslation.TakeRemovedNotice());
    }
}
