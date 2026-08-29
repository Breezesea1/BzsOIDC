namespace BzsOIDC.Idp.Services.Oidc;

public sealed class OidcScopeUpsertRequest
{
    public string? Name { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string[] Resources { get; init; } = [];
}

public sealed class OidcScopeResponse
{
    public string Name { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string[] Resources { get; init; } = [];
    public string[] Clients { get; init; } = [];
    public string[] Permissions { get; init; } = [];
    public bool IsReserved { get; init; }
    public string ETag { get; init; } = string.Empty;
}

public sealed class OidcScopeListQuery
{
    public string? Search { get; init; }
    public string? Sort { get; init; }
    public bool Descending { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed class OidcScopeListResponse
{
    public IReadOnlyList<OidcScopeResponse> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public enum OidcScopeCommandStatus
{
    Success,
    ValidationFailed,
    Conflict,
    NotFound,
}

public sealed class OidcScopeCommandResult<T>
{
    public OidcScopeCommandStatus Status { get; init; }
    public T? Value { get; init; }
    public string[] Errors { get; init; } = [];
}
