using LinkManager2.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2;

/// <summary>
/// Gives an element the texts of its uid, as x:Uid does with each "&lt;uid&gt;.&lt;property&gt;" of the .resw, but
/// through <see cref="Strings"/>, so the texts of a loaded translation reach the XAML too.
/// </summary>
public static class Text
{
    public static readonly DependencyProperty UidProperty = DependencyProperty.RegisterAttached(
        "Uid",
        typeof(string),
        typeof(Text),
        new PropertyMetadata(null, OnUidChanged)
    );

    public static string GetUid(DependencyObject element) => (string)element.GetValue(UidProperty);

    public static void SetUid(DependencyObject element, string uid) =>
        element.SetValue(UidProperty, uid);

    private static void OnUidChanged(
        DependencyObject element,
        DependencyPropertyChangedEventArgs args
    )
    {
        foreach (var key in Strings.KeysOf((string)args.NewValue))
            if (UidKeys.TrySplit(key, out _, out var property))
                Apply(element, property, Strings.Get(key));
    }

    /// <summary>Sets one text, logging instead of failing when the element has no such property, where x:Uid
    /// would close the page.</summary>
    private static void Apply(DependencyObject element, string property, string text)
    {
        switch (property)
        {
            case UidKeys.AutomationName:
                AutomationProperties.SetName(element, text);
                return;
            case UidKeys.AutomationHelpText:
                AutomationProperties.SetHelpText(element, text);
                return;
            case UidKeys.ToolTip:
                ToolTipService.SetToolTip(element, text);
                return;
        }
        if (UidKeys.TextProperty(element.GetType(), property, typeof(string)) is { } setter)
            setter.SetValue(element, text);
        else
            Diagnostics.Log($"{element.GetType().Name} has no text property {property}");
    }
}
