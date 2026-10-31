using System.Collections.Generic;
using LinkManager2.Data;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

public sealed partial class HistoryDialog : ContentDialog
{
    private static readonly Dictionary<string, string> _labels = new()
    {
        ["create"] = Strings.Get("History_Created"),
        ["update"] = Strings.Get("History_Updated"),
        ["delete"] = Strings.Get("History_Deleted"),
        ["archive"] = Strings.Get("History_Archived"),
        ["unarchive"] = Strings.Get("History_Restored"),
    };

    public HistoryDialog(string itemTitle, IReadOnlyList<ItemHistoryEntry> entries)
    {
        InitializeComponent();
        ItemLabel.Text = Strings.Format("History_ItemLabel", itemTitle);

        var rows = new List<EntryRow>();
        if (entries.Count == 0)
        {
            rows.Add(new EntryRow(Strings.Get("History_Empty"), ""));
        }
        else
        {
            foreach (var e in entries)
            {
                var label = _labels.TryGetValue(e.ChangeType, out var l) ? l : e.ChangeType;
                rows.Add(new EntryRow(label, e.ChangedAt.ToLocalTime().ToString("g")));
            }
        }
        EntriesList.ItemsSource = rows;
    }

    public sealed record EntryRow(string Label, string When);
}
