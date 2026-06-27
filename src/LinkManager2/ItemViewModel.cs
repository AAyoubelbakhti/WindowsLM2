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

    public string CategoryName { get; }

    public string DisplayTitle => _item.IsFavorite ? $"★ {_item.Title}" : _item.Title;

    public string AccessibleName => _item.IsFavorite ? $"Favorito, {_item.Title}" : _item.Title;

    public string TypeLabel => _item.Type == ItemTypes.Url ? "Enlace" : "Ruta";

    public string Details => $"{TypeLabel} · {CategoryName} · {Value}";

    public ItemViewModel(Item item, string? categoryName)
    {
        _item = item;
        CategoryName = categoryName ?? "Sin categoría";
    }

    public override string ToString() => AccessibleName;
}
