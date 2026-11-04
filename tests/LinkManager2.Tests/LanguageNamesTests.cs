using LinkManager2.Data;
using Xunit;

namespace LinkManager2.Tests;

public class LanguageNamesTests
{
    [Fact]
    public void Each_language_is_said_in_itself_capitalized_and_in_alphabetical_order()
    {
        var options = LanguageNames.Autonyms(["uk-UA", "es-ES", "en-US"]);

        Assert.Equal(
            [("en-US", "English"), ("es-ES", "Español"), ("uk-UA", "Українська")],
            options
        );
    }

    [Fact]
    public void A_tag_with_no_region_works_as_well()
    {
        Assert.Equal([("uk", "Українська")], LanguageNames.Autonyms(["uk"]));
    }

    [Fact]
    public void Georgian_keeps_its_letters_as_it_has_no_capitals_in_a_name()
    {
        Assert.Equal([("ka", "ქართული")], LanguageNames.Autonyms(["ka"]));
    }

    [Fact]
    public void The_region_shows_only_when_two_tags_share_a_language()
    {
        var options = LanguageNames.Autonyms(["pt-PT", "pt-BR", "es-ES"]);

        Assert.Equal(
            [
                ("es-ES", "Español"),
                ("pt-BR", "Português (Brasil)"),
                ("pt-PT", "Português (Portugal)"),
            ],
            options
        );
    }
}
