using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.UI.Xaml;
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
            if (!string.IsNullOrEmpty(title) && NormalizeFirst(title) == c)
            {
                _list.SelectedIndex = i;
                _list.ScrollIntoView(items[i]);
                FocusItem(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Moves keyboard focus onto the matched row's container. In a multi-select ListView
    /// (SelectionMode Extended) setting SelectedIndex changes selection but not focus, so
    /// without this the UI Automation focus never moves and a screen reader announces nothing.
    /// The container may be unrealized until layout catches up after ScrollIntoView, so this
    /// retries once on the next dispatcher tick.
    /// </summary>
    private void FocusItem(int index)
    {
        if (_list.ContainerFromIndex(index) is Control container)
        {
            container.Focus(FocusState.Keyboard);
            return;
        }
        _list.DispatcherQueue?.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => (_list.ContainerFromIndex(index) as Control)?.Focus(FocusState.Keyboard));
    }

    /// <summary>
    /// Uppercases the first character of a title and strips diacritics (FormD decomposition
    /// minus non-spacing marks) so accented initials and Ñ match their base letter key.
    /// </summary>
    private static char NormalizeFirst(string title)
    {
        var upper = char.ToUpperInvariant(title[0]);
        if (upper < 0x80) return upper;
        foreach (var ch in upper.ToString().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                return char.ToUpperInvariant(ch);
        return upper;
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
