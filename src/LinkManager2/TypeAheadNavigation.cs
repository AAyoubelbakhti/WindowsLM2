using System;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace LinkManager2;

/// <summary>
/// First-letter type-ahead for a ListView: pressing a letter or digit selects the
/// next item whose title starts with that character, cycling through matches on repeats.
/// Self-contained: remove this file and the single call in MainPage.OnListKeyDown to drop the feature.
/// </summary>
internal sealed class TypeAheadNavigation
{
    private readonly ListView _list;
    private readonly Func<object, string> _keyOf;

    public TypeAheadNavigation(ListView list, Func<object, string> keyOf)
    {
        _list = list;
        _keyOf = keyOf;
    }

    /// <summary>Handles a key press; returns true when it moved selection to a matching item.</summary>
    public bool TrySelect(VirtualKey key)
    {
        var c = ToChar(key);
        if (c == '\0') return false;

        var items = _list.Items;
        if (items.Count == 0) return false;

        var start = _list.SelectedIndex < 0 ? 0 : _list.SelectedIndex;
        for (var offset = 1; offset <= items.Count; offset++)
        {
            var i = (start + offset) % items.Count;
            var title = _keyOf(items[i]);
            if (!string.IsNullOrEmpty(title) && char.ToUpperInvariant(title[0]) == c)
            {
                _list.SelectedIndex = i;
                _list.ScrollIntoView(items[i]);
                return true;
            }
        }
        return false;
    }

    private static char ToChar(VirtualKey key)
    {
        if (key >= VirtualKey.A && key <= VirtualKey.Z)
            return (char)('A' + (key - VirtualKey.A));
        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
            return (char)('0' + (key - VirtualKey.Number0));
        return '\0';
    }
}
