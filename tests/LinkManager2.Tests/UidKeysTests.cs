using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LinkManager2.Data;
using Xunit;

namespace LinkManager2.Tests;

public partial class UidKeysTests
{
    private const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string WinUINamespace = "Microsoft.UI.Xaml";

    private static readonly XName TextUid = XName.Get("Text.Uid", "using:LinkManager2");
    private static readonly XName XamlUid = XName.Get(
        "Uid",
        "http://schemas.microsoft.com/winfx/2006/xaml"
    );

    private static readonly string WinUIAssembly = Path.Combine(
        AppContext.BaseDirectory,
        "WinUI",
        "Microsoft.WinUI.dll"
    );

    private static readonly string[] Attached =
    [
        UidKeys.AutomationName,
        UidKeys.AutomationHelpText,
        UidKeys.ToolTip,
    ];

    [GeneratedRegex("^[A-Z][A-Za-z]*$")]
    private static partial Regex PlainProperty();

    private static IEnumerable<XElement> XamlElements() =>
        Directory
            .EnumerateFiles(
                Path.Combine(AppContext.BaseDirectory, "Xaml"),
                "*.xaml",
                SearchOption.AllDirectories
            )
            .SelectMany(file => XDocument.Load(file).Descendants());

    /// <summary>The WinUI type a XAML element of the default namespace stands for, or null when its name matches
    /// none or more than one.</summary>
    private static Type? WinUIType(XElement element, ILookup<string, Type> types) =>
        element.Name.NamespaceName == Presentation
        && types[element.Name.LocalName].ToList() is [var type]
            ? type
            : null;

    /// <summary>Whether an element of <paramref name="type"/> takes the text of <paramref name="key"/>: an attached
    /// text goes to any element, a plain one needs a property to set.</summary>
    private static bool Takes(Type? type, string key, Type text) =>
        UidKeys.TrySplit(key, out _, out var property)
        && (
            Attached.Contains(property)
            || type is not null && UidKeys.TextProperty(type, property, text) is not null
        );

    private class Labeled
    {
        public object? Content { get; set; }
        public string? Title { get; private set; }
        public string? Description { get; }
        public int Count { get; set; }
    }

    private sealed class Relabeled : Labeled
    {
        public new string? Content { get; set; }
    }

    [Fact]
    public void A_key_with_a_dot_names_the_uid_and_the_property_it_sets()
    {
        Assert.True(UidKeys.TrySplit("Settings_SaveButton.Content", out var uid, out var property));
        Assert.Equal("Settings_SaveButton", uid);
        Assert.Equal("Content", property);
    }

    [Fact]
    public void An_attached_property_keeps_its_namespace_and_owner()
    {
        Assert.True(
            UidKeys.TrySplit(
                "Settings_SaveButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
                out var uid,
                out var property
            )
        );
        Assert.Equal("Settings_SaveButton", uid);
        Assert.Equal(UidKeys.AutomationName, property);
    }

    [Fact]
    public void A_key_with_no_dot_is_only_for_code()
    {
        Assert.False(UidKeys.TrySplit("Settings_AboutVersion", out _, out _));
    }

    [Theory]
    [InlineData("Settings_AboutVersion", "Settings_AboutVersion")]
    [InlineData("Settings_SaveButton.Content", "Settings_SaveButton/Content")]
    [InlineData(
        "Settings_SaveButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
        "Settings_SaveButton/[using:Microsoft.UI.Xaml.Automation]AutomationProperties/Name"
    )]
    public void The_resource_path_has_slashes_for_the_dots_outside_brackets(string key, string path)
    {
        Assert.Equal(path, UidKeys.ResourcePath(key));
    }

    [Theory]
    [InlineData(nameof(Labeled.Content), true)]
    [InlineData(nameof(Labeled.Title), false)]
    [InlineData(nameof(Labeled.Description), false)]
    [InlineData(nameof(Labeled.Count), false)]
    [InlineData("Missing", false)]
    public void A_text_goes_only_to_a_public_settable_property_that_takes_a_string(
        string property,
        bool found
    )
    {
        Assert.Equal(
            found,
            UidKeys.TextProperty(typeof(Labeled), property, typeof(string)) is not null
        );
    }

    [Fact]
    public void A_property_that_hides_another_is_found_without_ambiguity()
    {
        Assert.Throws<AmbiguousMatchException>(() =>
            typeof(Relabeled).GetProperty(nameof(Relabeled.Content))
        );

        Assert.Equal(
            typeof(Relabeled),
            UidKeys
                .TextProperty(typeof(Relabeled), nameof(Relabeled.Content), typeof(string))
                ?.DeclaringType
        );
    }

    [Fact]
    public void Every_property_of_the_base_is_plain_or_one_of_the_attached_ones_text_uid_sets()
    {
        var unknown = Strings
            .Base.Keys.Where(key =>
                UidKeys.TrySplit(key, out _, out var property)
                && !Attached.Contains(property)
                && !PlainProperty().IsMatch(property)
            )
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void Every_text_of_a_uid_goes_to_a_property_the_winui_type_of_its_element_has()
    {
        var assemblies = Directory
            .GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll")
            .Append(WinUIAssembly);
        using var context = new MetadataLoadContext(new PathAssemblyResolver(assemblies));
        var types = context
            .LoadFromAssemblyPath(WinUIAssembly)
            .GetExportedTypes()
            .Where(type =>
                type.Namespace?.StartsWith(WinUINamespace, StringComparison.Ordinal) == true
            )
            .ToLookup(type => type.Name);
        var text = context.CoreAssembly!.GetType(typeof(string).FullName!, throwOnError: true)!;

        var missing = XamlElements()
            .Select(element => (Element: element, Uid: element.Attribute(TextUid)?.Value))
            .Where(entry => entry.Uid is not null)
            .SelectMany(entry =>
                Strings
                    .KeysOf(entry.Uid!)
                    .Where(key => !Takes(WinUIType(entry.Element, types), key, text))
                    .Select(key => $"{entry.Element.Name.LocalName} {key}")
            )
            .ToList();

        Assert.NotEmpty(types);
        Assert.Empty(missing);
    }

    [Fact]
    public void No_xaml_keeps_an_x_uid_which_would_skip_a_loaded_translation()
    {
        Assert.DoesNotContain(XamlElements(), element => element.Attribute(XamlUid) is not null);
    }

    [Fact]
    public void Every_uid_of_the_xaml_has_texts_and_every_text_of_an_element_has_its_uid()
    {
        var used = XamlElements()
            .Select(element => element.Attribute(TextUid)?.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();
        var withTexts = Strings
            .Base.Keys.Select(key => UidKeys.TrySplit(key, out var uid, out _) ? uid : null)
            .OfType<string>()
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(used);
        Assert.Equal(withTexts, used.Distinct());
    }
}
