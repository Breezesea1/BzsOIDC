namespace BzsOIDC.Idp.Components.Admin;

public sealed class AdminCollection<TItem, TKey>(Func<TItem, TKey> keySelector)
    where TKey : notnull
{
    private readonly Func<TItem, TKey> _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
    private readonly List<TItem> _rows = [];
    private readonly HashSet<TKey> _selectedKeys = [];

    public int CurrentPage { get; private set; } = 1;

    public int PageSize { get; private set; } = 10;

    public int TotalCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public int PageStartIndex { get; private set; }

    public int PageEndIndex { get; private set; }

    public IReadOnlyList<TItem> FilteredItems { get; private set; } = [];

    public IReadOnlyList<TItem> VisibleItems { get; private set; } = [];

    public IReadOnlyList<TItem> SelectedItems => _rows
        .Where(row => _selectedKeys.Contains(_keySelector(row)))
        .ToArray();

    public void ReplaceRows(IEnumerable<TItem> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        _rows.Clear();
        _rows.AddRange(rows);

        var rowKeys = _rows.Select(_keySelector).ToHashSet();
        _selectedKeys.RemoveWhere(key => !rowKeys.Contains(key));
    }

    public void ReplaceSelection(IEnumerable<TItem> selectedItems)
    {
        ArgumentNullException.ThrowIfNull(selectedItems);

        _selectedKeys.Clear();
        foreach (var selectedItem in selectedItems)
        {
            _selectedKeys.Add(_keySelector(selectedItem));
        }
    }

    public void SetPageSize(int pageSize)
    {
        PageSize = Math.Max(1, pageSize);
    }

    public AdminCollectionResult<TItem> Query(AdminCollectionQuery<TItem> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        PageSize = Math.Max(1, query.PageSize);

        IEnumerable<TItem> filtered = _rows;
        if (query.Filter is not null)
        {
            filtered = filtered.Where(query.Filter);
        }

        if (query.SearchScorer is not null)
        {
            filtered = filtered.Where(item => query.SearchScorer(item).HasValue);
        }

        var sorted = (query.Sort?.Invoke(filtered, query.SortName, query.SortDescending) ?? filtered).ToArray();

        TotalCount = sorted.Length;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        CurrentPage = Math.Clamp(Math.Max(1, query.PageIndex), 1, TotalPages);

        var visible = sorted
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToArray();

        FilteredItems = sorted;
        VisibleItems = visible;
        PageStartIndex = TotalCount == 0 ? 0 : ((CurrentPage - 1) * PageSize) + 1;
        PageEndIndex = Math.Min(TotalCount, ((CurrentPage - 1) * PageSize) + visible.Length);

        return new AdminCollectionResult<TItem>(
            VisibleItems,
            FilteredItems,
            SelectedItems,
            CurrentPage,
            PageSize,
            TotalCount,
            TotalPages,
            PageStartIndex,
            PageEndIndex);
    }
}

public sealed record AdminCollectionQuery<TItem>(
    int PageIndex,
    int PageSize,
    string? SortName,
    bool SortDescending,
    Func<TItem, bool>? Filter = null,
    Func<TItem, int?>? SearchScorer = null,
    Func<IEnumerable<TItem>, string?, bool, IEnumerable<TItem>>? Sort = null);

public sealed record AdminCollectionResult<TItem>(
    IReadOnlyList<TItem> Items,
    IReadOnlyList<TItem> FilteredItems,
    IReadOnlyList<TItem> SelectedItems,
    int CurrentPage,
    int PageSize,
    int TotalCount,
    int TotalPages,
    int PageStartIndex,
    int PageEndIndex);
