using System.Collections.Generic;
using LinkManager2.Data;
using Microsoft.UI.Xaml.Controls;

namespace LinkManager2.Dialogs;

public sealed partial class HistoryDialog : ContentDialog
{
    private static readonly Dictionary<string, string> _labels = new()
    {
        ["create"] = "Creado",
        ["update"] = "Modificado",
        ["delete"] = "Borrado",
        ["archive"] = "Archivado",
        ["unarchive"] = "Restaurado",
    };

    public HistoryDialog(string itemTitle, IReadOnlyList<ItemHistoryEntry> entries)
    {
        InitializeComponent();
        ItemLabel.Text = $"Eventos de: {itemTitle}";

        var rows = new List<EntryRow>();
        if (entries.Count == 0)
        {
            rows.Add(new EntryRow("Sin eventos", ""));
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
