namespace BzsOIDC.Idp.Services.Identity;

public sealed record PermissionTopologySeedResource
{
    public string ResourceKey { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public PermissionTopologySeedPermission[] Permissions { get; init; } = [];
}

public sealed record PermissionTopologySeedPermission
{
    public string Name { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string[] ReleaseScopes { get; init; } = [];
}

public sealed record ProtectedResourceUpsertRequest
{
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record PermissionDefinitionUpsertRequest
{
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record PermissionReleaseScopesUpsertRequest
{
    public string[] Scopes { get; init; } = [];
}

public sealed record ProtectedResourceResponse
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public string ETag { get; init; } = string.Empty;
    public PermissionDefinitionResponse[] Permissions { get; init; } = [];
}

public sealed record PermissionDefinitionResponse
{
    public string ResourceKey { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public string ETag { get; init; } = string.Empty;
    public string[] ReleaseScopes { get; init; } = [];
    public RolePermissionAssignmentResponse[] AssignedRoles { get; init; } = [];
}

public sealed record RolePermissionAssignmentResponse
{
    public Guid RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public bool Assigned { get; init; }
}

public sealed record RoleResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string NormalizedName { get; init; } = string.Empty;
    public bool IsProtected { get; init; }
    public int PermissionCount { get; init; }
    public string[] Permissions { get; init; } = [];
    public string ETag { get; init; } = string.Empty;
}

public sealed record RoleListQuery
{
    public string? Search { get; init; }
    public string? Sort { get; init; }
    public bool Descending { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed record RoleListResponse
{
    public IReadOnlyList<RoleResponse> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed record RoleUpsertRequest
{
    public string Name { get; init; } = string.Empty;
}

public sealed record RolePermissionSyncRequest
{
    public string[] Permissions { get; init; } = [];
}

public enum PermissionTopologyCommandStatus
{
    Success,
    ValidationFailed,
    NotFound,
    Conflict,
    Protected,
}

public sealed record PermissionTopologyCommandResult<T>
{
    public PermissionTopologyCommandStatus Status { get; init; }
    public T? Value { get; init; }
    public string[] Errors { get; init; } = [];
    public static PermissionTopologyCommandResult<T> Success(T value)
    {
        return new PermissionTopologyCommandResult<T>
        {
            Status = PermissionTopologyCommandStatus.Success,
            Value = value,
        };
    }

    public static PermissionTopologyCommandResult<T> Failure(
        PermissionTopologyCommandStatus status,
        params string[] errors)
    {
        return new PermissionTopologyCommandResult<T>
        {
            Status = status,
            Errors = errors,
        };
    }
}
