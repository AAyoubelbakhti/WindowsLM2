using LinkManager2.Data;

namespace LinkManager2;

public sealed class ItemViewModel
{
    private readonly Item _item;
    public Item Source => _item;

    public string Id => _item.Id;
    public string Title => _item.Title;
    public string Value => _item.Value;
    public string Type => _item.Type;
    public string? CategoryId => _item.CategoryId;
    public bool IsFavorite => _item.IsFavorite;
    public bool IsBroken => _item.LinkStatus == "broken";

    public string CategoryName { get; }

    public string DisplayTitle
    {
        get
        {
            var prefix = _item.IsFavorite ? "★ " : string.Empty;
            if (IsBroken) prefix += "⚠ ";
            return prefix + _item.Title;
        }
    }

    public string AccessibleName
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (_item.IsFavorite) parts.Add("Favorito");
            if (IsBroken) parts.Add("Enlace roto");
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

    public ItemViewModel(Item item, string? categoryName)
    {
        _item = item;
        CategoryName = categoryName ?? "Sin categoría";
    }

    public override string ToString() => AccessibleName;
}
