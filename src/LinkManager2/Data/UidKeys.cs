using System.Reflection;

namespace LinkManager2.Data;

/// <summary>
/// How the .resw names the texts of an element: "&lt;uid&gt;.&lt;property&gt;", where the property is a plain one,
/// such as "Content", or one of the attached ones below.
/// </summary>
internal static class UidKeys
{
    private const BindingFlags DeclaredInstance =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    public const string AutomationName =
        "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name";
    public const string AutomationHelpText =
        "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.HelpText";
    public const string ToolTip = "[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip";

    /// <summary>Splits <paramref name="key"/> into the uid of an element and the property it sets; false for a key
    /// only code reads, which has no dot.</summary>
    public static bool TrySplit(string key, out string uid, out string property)
    {
        var dot = key.IndexOf('.');
        uid = dot > 0 ? key[..dot] : "";
        property = dot > 0 ? key[(dot + 1)..] : "";
        return property.Length > 0;
    }

    /// <summary>The plain property of <paramref name="type"/> that a text named <paramref name="property"/> sets:
    /// public, with a public setter and of a type that takes <paramref name="text"/>, the string type of the context
    /// <paramref name="type"/> comes from, so the tests can ask WinUI types they only read as metadata.</summary>
    public static PropertyInfo? TextProperty(Type type, string property, Type text) =>
        MostDerived(type, property) is { SetMethod.IsPublic: true } declared
        && declared.PropertyType.IsAssignableFrom(text)
            ? declared
            : null;

    /// <summary>The plain property named <paramref name="property"/> that <paramref name="type"/> or the nearest of
    /// its bases declares, asked type by type, so one that hides another cannot make the lookup ambiguous.</summary>
    private static PropertyInfo? MostDerived(Type type, string property)
    {
        for (var level = type; level is not null; level = level.BaseType)
        {
            var declared = level
                .GetProperties(DeclaredInstance)
                .FirstOrDefault(candidate =>
                    candidate.Name == property && candidate.GetIndexParameters().Length == 0
                );
            if (declared is not null)
                return declared;
        }
        return null;
    }

    /// <summary>The name of <paramref name="key"/> in the PRI, where each dot outside the brackets of an attached
    /// property is a slash, as in "Settings_About/[using:Microsoft.UI.Xaml.Automation]AutomationProperties/Name".</summary>
    public static string ResourcePath(string key)
    {
        var path = key.ToCharArray();
        var inBrackets = false;
        for (var i = 0; i < path.Length; i++)
        {
            inBrackets = path[i] switch
            {
                '[' => true,
                ']' => false,
                _ => inBrackets,
            };
            if (path[i] == '.' && !inBrackets)
                path[i] = '/';
        }
        return new string(path);
    }
}
