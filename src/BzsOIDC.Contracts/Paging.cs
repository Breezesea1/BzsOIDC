namespace BzsOIDC.Contracts;

public enum SortDirection
{
    Ascending,
    Descending,
}

/// <summary>An allow-listed sort value; field names are interpreted by each endpoint.</summary>
public sealed record SortValue(string Field, SortDirection Direction = SortDirection.Ascending);

/// <summary>An explicit server-side filter value.</summary>
public sealed record FilterValue(string Field, string? Value);

public sealed record PageRequest
{
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 100;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;
    public string? Search { get; init; }
    public IReadOnlyList<SortValue> Sort { get; init; } = [];
    public IReadOnlyList<FilterValue> Filters { get; init; } = [];

    public PageRequest()
    {
    }

    public PageRequest(int page = 1, int pageSize = DefaultPageSize, string? search = null,
        IReadOnlyList<SortValue>? sort = null, IReadOnlyList<FilterValue>? filters = null)
    {
        Page = page;
        PageSize = pageSize;
        Search = search;
        Sort = sort ?? [];
        Filters = filters ?? [];
    }
}

public sealed record PageResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public PageResult()
    {
    }

    public PageResult(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }
}
