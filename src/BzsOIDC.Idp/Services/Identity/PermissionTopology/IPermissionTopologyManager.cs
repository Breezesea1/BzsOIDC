namespace BzsOIDC.Idp.Services.Identity;

public interface IPermissionTopologyManager
{
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
