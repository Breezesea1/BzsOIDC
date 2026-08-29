namespace BzsOIDC.Contracts;

/// <summary>Browser-safe protected-resource summary used by administration clients.</summary>
public sealed record ProtectedResourceSummary
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public string ETag { get; init; } = string.Empty;
    public int PermissionCount { get; init; }
}

public sealed record PermissionDefinitionSummary
{
    public string ResourceKey { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public string ETag { get; init; } = string.Empty;
    public string[] ReleaseScopes { get; init; } = [];
    public RolePermissionAssignment[] AssignedRoles { get; init; } = [];
}

public sealed record RolePermissionAssignment
{
    public Guid RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public bool Assigned { get; init; }
}

public sealed record ProtectedResourceDetail
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public string ETag { get; init; } = string.Empty;
    public PermissionDefinitionSummary[] Permissions { get; init; } = [];
}

public sealed record ProtectedResourceUpsertContract
{
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record PermissionDefinitionUpsertContract
{
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record PermissionReleaseScopesContract
{
    public string[] Scopes { get; init; } = [];
}
