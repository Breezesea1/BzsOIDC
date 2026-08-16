using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.Services.Oidc;

namespace BzsOIDC.Idp.Services.Admin;

public interface IAdminDashboardService
{
    Task<AdminDashboardSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default);
}

public sealed class AdminDashboardSummaryResponse
{
    public int TotalUsers { get; init; }
    public int AdminUsers { get; init; }
    public int StandardUsers { get; init; }
    public int TotalClients { get; init; }
    public int InteractiveClients { get; init; }
    public int MachineClients { get; init; }
    public int TotalPermissionMappings { get; init; }
    public int TotalConfiguredScopes { get; init; }
}

internal sealed class AdminDashboardService(
    IUserAdministration userAdministration,
    IOidcClientService clientService,
    IPermissionTopology permissionTopology) : IAdminDashboardService
{
    public async Task<AdminDashboardSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var users = await userAdministration.GetUsersAsync(cancellationToken);
        var clients = await clientService.GetAllAsync(cancellationToken);
        var resources = await permissionTopology.GetResourcesAsync(cancellationToken);
        var permissions = resources.SelectMany(static resource => resource.Permissions).ToArray();
        var adminUsers = users.Count(static user => user.IsAdmin);

        return new AdminDashboardSummaryResponse
        {
            TotalUsers = users.Count,
            AdminUsers = adminUsers,
            StandardUsers = Math.Max(0, users.Count - adminUsers),
            TotalClients = clients.Count,
            InteractiveClients = clients.Count(static client => client.AuthFlow == OidcClientAuthFlow.AuthorizationCode),
            MachineClients = clients.Count(static client => client.AuthFlow == OidcClientAuthFlow.ClientCredentials),
            TotalPermissionMappings = permissions.Length,
            TotalConfiguredScopes = permissions.Sum(static permission => permission.ReleaseScopes.Length),
        };
    }
}
