using System.ComponentModel;
using System.Runtime.CompilerServices;
using LinkManager2.Data;

namespace LinkManager2;

/// <summary>
/// Observable row view model over an <see cref="Item"/>. Raises change notifications so
/// per-row edits (favorite toggled, broken status) can update the UI without reloading the list.
/// </summary>
public sealed class ItemViewModel : INotifyPropertyChanged
{
    private Item _item;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Item Source => _item;

    public string Id => _item.Id;
    public string Title => _item.Title;
    public string Value => _item.Value;
    public string Type => _item.Type;
    public string? CategoryId => _item.CategoryId;
    public bool IsFavorite => _item.IsFavorite;
    public bool IsBroken => _item.LinkStatus == "broken";

    public string CategoryName { get; private set; }

    public string DisplayTitle
    {
        get
        {
            var prefix = _item.IsFavorite ? "★ " : string.Empty;
            if (IsBroken)
                prefix += "⚠ ";
            return prefix + _item.Title;
        }
    }

    public string AccessibleName
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (_item.IsFavorite)
                parts.Add("Favorito");
            if (IsBroken)
                parts.Add("Enlace roto");
            parts.Add(_item.Title);
            return string.Join(", ", parts);
        }
    }

    public string TypeLabel => _item.Type == ItemTypes.Url ? "Enlace" : "Ruta";

    public string Details
    {
        get
        {
            var status = IsBroken ? " · Roto" : string.Empty;
            return $"{TypeLabel} · {CategoryName} · {Value}{status}";
        }
    }

    private (string, string, string) _shown;

    public ItemViewModel(Item item, string? categoryName)
    {
        _item = item;
        CategoryName = categoryName ?? "Sin categoría";
        _shown = Shown();
    }

    /// <summary>
    /// Points the row at the latest copy of its item so it refreshes in place instead of being
    /// rebuilt. Notifies only when what the row shows or reads aloud changed, so an unchanged row
    /// stays silent for the screen reader.
    /// </summary>
    public void UpdateSource(Item item, string? categoryName)
    {
        _item = item;
        CategoryName = categoryName ?? "Sin categoría";
        var shown = Shown();
        if (shown == _shown)
            return;
        _shown = shown;
        RaiseAllChanged();
    }

    private (string, string, string) Shown() => (DisplayTitle, AccessibleName, Details);

    private void RaiseAllChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(CategoryId));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(IsBroken));
        OnPropertyChanged(nameof(CategoryName));
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(AccessibleName));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(Details));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString() => AccessibleName;
}
