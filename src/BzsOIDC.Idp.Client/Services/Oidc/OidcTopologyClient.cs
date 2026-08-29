using System.Net.Http.Json;
using BzsOIDC.Contracts;
using BzsOIDC.Idp.Client.Services.Session;

namespace BzsOIDC.Idp.Client.Services.Oidc;

public interface IOidcTopologyClient
{
    Task<OidcTopologyResponse?> GetAsync(CancellationToken cancellationToken = default);
    Task<OidcTopologyNodeResponse?> GetNodeAsync(string nodeType, string nodeId, CancellationToken cancellationToken = default);
}

internal sealed class OidcTopologyClient(IApiHttpClientFactory factory) : IOidcTopologyClient
{
    public Task<OidcTopologyResponse?> GetAsync(CancellationToken cancellationToken = default)
        => factory.CreateClient().GetFromJsonAsync<OidcTopologyResponse>("api/admin/oidc-topology", cancellationToken);

    public Task<OidcTopologyNodeResponse?> GetNodeAsync(string nodeType, string nodeId, CancellationToken cancellationToken = default)
        => factory.CreateClient().GetFromJsonAsync<OidcTopologyNodeResponse>($"api/admin/oidc-topology/{Uri.EscapeDataString(nodeType)}/{Uri.EscapeDataString(nodeId)}", cancellationToken);
}

public static class OidcTopologyClientExtensions
{
    public static IServiceCollection AddOidcTopologyClient(this IServiceCollection services)
        => services.AddScoped<IOidcTopologyClient, OidcTopologyClient>();
}
