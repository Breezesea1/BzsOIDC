namespace BzsOIDC.Idp.Services.Identity;

public interface IPermissionTopology
{
    Task<IReadOnlyList<ProtectedResourceResponse>> GetResourcesAsync(CancellationToken cancellationToken = default);

    Task<ProtectedResourceResponse?> GetResourceAsync(string resourceKey, CancellationToken cancellationToken = default);

    Task<PermissionDefinitionResponse?> GetPermissionAsync(string permissionName, CancellationToken cancellationToken = default);

    Task<PermissionTopologyCommandResult<ProtectedResourceResponse>> UpsertResourceAsync(
        string resourceKey,
        ProtectedResourceUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<PermissionTopologyCommandResult<PermissionDefinitionResponse>> UpsertPermissionAsync(
        string resourceKey,
        string permissionName,
        PermissionDefinitionUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<PermissionTopologyCommandResult<PermissionDefinitionResponse>> SyncReleaseScopesAsync(
        string permissionName,
        IEnumerable<string> scopes,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string[]>> ResolveReleaseScopesAsync(
        IEnumerable<string> permissions,
        CancellationToken cancellationToken = default);

    Task<string[]> ValidateAssignablePermissionsAsync(
        IEnumerable<string> permissions,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleResponse>> GetAllRolesAsync(CancellationToken cancellationToken = default);

    Task<RoleResponse?> GetRoleByIdAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>?> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ResolveRolePermissionsAsync(
        IEnumerable<string> roleNames,
        CancellationToken cancellationToken = default);

    Task<PermissionTopologyCommandResult<RoleResponse>> CreateRoleAsync(
        RoleUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<PermissionTopologyCommandResult<RoleResponse>> UpdateRoleAsync(
        Guid roleId,
        RoleUpsertRequest request,
        CancellationToken cancellationToken = default);

    Task<PermissionTopologyCommandResult<RoleResponse>> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<PermissionTopologyCommandResult<IReadOnlyList<string>>> SyncRolePermissionsAsync(
        Guid roleId,
        IEnumerable<string> permissions,
        CancellationToken cancellationToken = default);

    Task InitializeDefaultsAsync(
        IEnumerable<PermissionTopologySeedResource> resources,
        IEnumerable<string> rolesToEnsure,
        IReadOnlyDictionary<string, string[]> rolePermissions,
        CancellationToken cancellationToken = default);
}
