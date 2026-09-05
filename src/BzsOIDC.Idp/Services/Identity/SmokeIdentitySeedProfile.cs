using BzsOIDC.Shared.Infrastructure.Authorization;

namespace BzsOIDC.Idp.Services.Identity;

public static class SmokeIdentitySeedProfile
{
    public static IdentitySeedOptions Resolve(bool smokeEnabled, IdentitySeedOptions baseOptions)
    {
        ArgumentNullException.ThrowIfNull(baseOptions);

        if (!smokeEnabled)
        {
            return baseOptions;
        }

        return new IdentitySeedOptions
        {
            Admin = baseOptions.Admin,
            InitialRoles = [],
            RolePermissions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
            PermissionCatalog =
            [
                new()
                {
                    ResourceKey = PermissionConstants.ScopeApi,
                    DisplayName = "Smoke API",
                    Permissions =
                    [
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
            ],
            AdditionalScopes = [PermissionConstants.ScopeApi],
        };
    }
}
