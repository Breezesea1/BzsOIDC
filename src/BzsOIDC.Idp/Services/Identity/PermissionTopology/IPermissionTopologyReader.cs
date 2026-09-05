namespace BzsOIDC.Idp.Services.Identity;

public interface IPermissionTopologyReader
{
    Task<IReadOnlyList<ProtectedResourceResponse>> GetResourcesAsync(CancellationToken cancellationToken = default);

    Task<ProtectedResourceResponse?> GetResourceAsync(string resourceKey, CancellationToken cancellationToken = default);

    Task<PermissionDefinitionResponse?> GetPermissionAsync(string permissionName, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string[]>> ResolveReleaseScopesAsync(
        IEnumerable<string> permissions,
        CancellationToken cancellationToken = default);

    Task<string[]> ValidateAssignablePermissionsAsync(
        IEnumerable<string> permissions,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleResponse>> GetAllRolesAsync(CancellationToken cancellationToken = default);

    Task<RoleListResponse> ListRolesAsync(RoleListQuery query, CancellationToken cancellationToken = default);

    Task<RoleResponse?> GetRoleByIdAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>?> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ResolveRolePermissionsAsync(
        IEnumerable<string> roleNames,
        CancellationToken cancellationToken = default);
}
