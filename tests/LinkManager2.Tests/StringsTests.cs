using LinkManager2.Data;
using Xunit;

namespace LinkManager2.Tests;

public sealed class StringsTests : IDisposable
{
    private const string Save = "Settings_SaveButton.Content";
    private const string Back = "Settings_BackButton.Content";

    public void Dispose() => Strings.Load(new Dictionary<string, string>());

    [Fact]
    public void Without_a_translation_the_texts_are_the_apps_own()
    {
        Assert.Equal(Strings.Base[Save], Strings.Get(Save));
    }

    [Fact]
    public void A_loaded_translation_goes_first_and_what_it_lacks_stays_the_apps_own()
    {
        Strings.Load(new Dictionary<string, string> { [Save] = "Зберегти" });

        Assert.Equal("Зберегти", Strings.Get(Save));
        Assert.Equal(Strings.Base[Back], Strings.Get(Back));
    }

    [Fact]
    public void A_missing_key_shows_itself_with_or_without_a_translation()
    {
        Assert.Equal("Main_NoSuchText", Strings.Get("Main_NoSuchText"));
        Strings.Load(new Dictionary<string, string> { [Save] = "Зберегти" });
        Assert.Equal("Main_NoSuchText", Strings.Get("Main_NoSuchText"));
    }

    [Fact]
    public void Format_fills_in_the_loaded_text()
    {
        Strings.Load(
            new Dictionary<string, string> { ["Settings_AboutVersion"] = "LinkManager {0} (uk)" }
        );

        Assert.Equal("LinkManager 1.9.4 (uk)", Strings.Format("Settings_AboutVersion", "1.9.4"));
    }

    [Fact]
    public void An_element_takes_every_text_of_its_uid_and_nothing_else()
    {
        Assert.Equal(
            ["Settings_SaveButton.Content", "Settings_SaveButton." + UidKeys.AutomationName],
            Strings.KeysOf("Settings_SaveButton").Order(StringComparer.Ordinal)
        );
        Assert.Empty(Strings.KeysOf("Settings_Save"));
    }
}
