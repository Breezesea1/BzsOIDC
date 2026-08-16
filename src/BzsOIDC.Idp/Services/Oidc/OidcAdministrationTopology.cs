using BzsOIDC.Idp.Services.Identity;

namespace BzsOIDC.Idp.Services.Oidc;

public interface IOidcAdministrationTopology
{
    Task<OidcAdministrationTopologySnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

public sealed record OidcAdministrationTopologySnapshot(
    IReadOnlyList<OidcAdministrationClient> Clients,
    IReadOnlyList<OidcAdministrationScope> Scopes,
    int PermissionCount);

public sealed record OidcAdministrationClient(
    string ClientId,
    string? DisplayName,
    OidcClientAuthFlow AuthFlow,
    IReadOnlyList<string> Scopes);

public sealed record OidcAdministrationScope(
    string Name,
    string? DisplayName,
    string? Description,
    IReadOnlyList<string> Resources,
    IReadOnlyList<string> Clients,
    IReadOnlyList<string> Permissions);

internal sealed class OidcAdministrationTopology(
    IOidcClientService clientService,
    IOidcScopeService scopeService,
    IPermissionTopology permissionTopology) : IOidcAdministrationTopology
{
    public async Task<OidcAdministrationTopologySnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        // These remain sequential, uncached source reads and do not promise an atomic snapshot.
        var clients = await clientService.GetAllAsync(cancellationToken);
        var scopes = await scopeService.GetAllAsync(cancellationToken);
        var permissionDefinitions = (await permissionTopology.GetResourcesAsync(cancellationToken))
            .SelectMany(static resource => resource.Permissions)
            .ToArray();

        var clientProjections = clients
            .Select(static client => new OidcAdministrationClient(
                client.ClientId,
                client.DisplayName,
                client.AuthFlow,
                client.Scopes))
            .ToArray();

        var scopeProjections = scopes
            .Select(scope => new OidcAdministrationScope(
                scope.Name,
                scope.DisplayName,
                scope.Description,
                scope.Resources,
                clients
                    .Where(client => client.Scopes.Contains(scope.Name, StringComparer.OrdinalIgnoreCase))
                    .Select(static client => string.IsNullOrWhiteSpace(client.DisplayName)
                        ? client.ClientId
                        : client.DisplayName)
                    .OrderBy(static client => client, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                permissionDefinitions
                    .Where(permission => permission.ReleaseScopes.Contains(scope.Name, StringComparer.OrdinalIgnoreCase))
                    .Select(static permission => permission.Name)
                    .OrderBy(static permission => permission, StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();

        return new OidcAdministrationTopologySnapshot(
            clientProjections,
            scopeProjections,
            permissionDefinitions.Length);
    }
}
