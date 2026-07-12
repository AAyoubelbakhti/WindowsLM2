namespace LinkManager2;

public enum FilterType { All, Url, Path }

public enum SortKey
{
    AlphaAsc,
    AlphaDesc,
    DateDesc,
    DateAsc,
    UsageDesc,
    CategoryAsc,
}

public sealed class FilterState
{
    public string Search { get; set; } = string.Empty;
    public FilterType Type { get; set; } = FilterType.All;
    public string? CategoryId { get; set; }
    public string? TagId { get; set; }
    public bool FavoritesOnly { get; set; }
    public bool BrokenOnly { get; set; }
    public SortKey Sort { get; set; } = SortKey.AlphaAsc;

    public int ActiveCount
    {
        get
        {
            var n = 0;
            if (Type != FilterType.All) n++;
            if (CategoryId is not null) n++;
            if (TagId is not null) n++;
            if (FavoritesOnly) n++;
            if (BrokenOnly) n++;
            return n;
        }
    }

    public FilterState Clone() => new()
    {
        Search = Search,
        Type = Type,
        CategoryId = CategoryId,
        TagId = TagId,
        FavoritesOnly = FavoritesOnly,
        BrokenOnly = BrokenOnly,
        Sort = Sort,
    };
}
