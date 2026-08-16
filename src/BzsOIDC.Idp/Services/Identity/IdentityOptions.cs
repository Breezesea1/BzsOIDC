using BzsOIDC.Shared.Infrastructure.Authorization;

namespace BzsOIDC.Idp.Services.Identity;

public sealed class IdentitySeedOptions
{
    /// <summary>
    /// 执行new。
    /// </summary>
    /// <returns>执行结果。</returns>
    public SeedAdminOptions Admin { get; init; } = new();
    public string[] InitialRoles { get; init; } = [IdentitySeedConstants.UserRoleName];

    public Dictionary<string, string[]> RolePermissions { get; init; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [IdentitySeedConstants.UserRoleName] = [PermissionConstants.UsersReadSelf],
        };

    // Preserve the existing configuration key while using topology terminology in code.
    public PermissionTopologySeedResource[] PermissionCatalog { get; init; } =
    [
        new()
        {
            ResourceKey = PermissionConstants.ScopeApi,
            DisplayName = "BzsOIDC Admin API",
            Permissions =
            [
                new() { Name = PermissionConstants.UsersReadSelf, ReleaseScopes = [PermissionConstants.ScopeApi] },
                new() { Name = PermissionConstants.UsersReadAll, ReleaseScopes = [PermissionConstants.ScopeApi] },
                new() { Name = PermissionConstants.UsersWrite, ReleaseScopes = [PermissionConstants.ScopeApi] },
                new() { Name = PermissionConstants.RolesRead, ReleaseScopes = [PermissionConstants.ScopeApi] },
                new() { Name = PermissionConstants.RolesWrite, ReleaseScopes = [PermissionConstants.ScopeApi] },
                new() { Name = PermissionConstants.ClientsRead, ReleaseScopes = [PermissionConstants.ScopeApi] },
                new() { Name = PermissionConstants.ClientsWrite, ReleaseScopes = [PermissionConstants.ScopeApi] },
                new() { Name = PermissionConstants.ScopesRead, ReleaseScopes = [PermissionConstants.ScopeApi] },
                new() { Name = PermissionConstants.ScopesWrite, ReleaseScopes = [PermissionConstants.ScopeApi] },
                new() { Name = PermissionConstants.PermissionsRead, ReleaseScopes = [PermissionConstants.ScopeApi] },
                new() { Name = PermissionConstants.PermissionsWrite, ReleaseScopes = [PermissionConstants.ScopeApi] },
            ],
        },
    ];

    public string[] AdditionalScopes { get; init; } = [PermissionConstants.ScopeApi];
}

public sealed class SeedAdminOptions
{
    public string UserName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
