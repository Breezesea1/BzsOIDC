using System.Net.Http.Json;
using BzsOIDC.Idp.Client.Services.Session;

namespace BzsOIDC.Idp.Client.Services.Scopes;

public sealed class OidcScopeUpsertRequest { public string? Name { get; set; } public string? DisplayName { get; set; } public string? Description { get; set; } public string[] Resources { get; set; } = []; }
public sealed class OidcScopeResponse { public string Name { get; init; } = string.Empty; public string? DisplayName { get; init; } public string? Description { get; init; } public string[] Resources { get; init; } = []; public string[] Clients { get; init; } = []; public string[] Permissions { get; init; } = []; public bool IsReserved { get; init; } public string ETag { get; init; } = string.Empty; }
public sealed class OidcScopeListResponse { public IReadOnlyList<OidcScopeResponse> Items { get; init; } = []; public int TotalCount { get; init; } public int Page { get; init; } public int PageSize { get; init; } }

public interface IScopesClient
{
    Task<OidcScopeListResponse?> ListAsync(string? search = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> CreateAsync(OidcScopeUpsertRequest request, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> UpdateAsync(string name, OidcScopeUpsertRequest request, string etag, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> DeleteAsync(string name, string etag, CancellationToken cancellationToken = default);
}

internal sealed class ScopesClient(IApiHttpClientFactory factory) : IScopesClient
{
    public Task<OidcScopeListResponse?> ListAsync(string? search = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var query = $"api/oidc/scopes?page={Math.Max(1, page)}&pageSize={Math.Clamp(pageSize, 1, 100)}" + (string.IsNullOrWhiteSpace(search) ? string.Empty : $"&search={Uri.EscapeDataString(search)}");
        return factory.CreateClient().GetFromJsonAsync<OidcScopeListResponse>(query, cancellationToken);
    }

    public Task<HttpResponseMessage> CreateAsync(OidcScopeUpsertRequest request, CancellationToken cancellationToken = default) => factory.CreateClient().PostAsJsonAsync("api/oidc/scopes", request, cancellationToken);

    public Task<HttpResponseMessage> UpdateAsync(string name, OidcScopeUpsertRequest request, string etag, CancellationToken cancellationToken = default)
    {
        var message = new HttpRequestMessage(HttpMethod.Put, $"api/oidc/scopes/{Uri.EscapeDataString(name)}") { Content = JsonContent.Create(request) };
        message.Headers.TryAddWithoutValidation("If-Match", etag);
        return factory.CreateClient().SendAsync(message, cancellationToken);
    }

    public Task<HttpResponseMessage> DeleteAsync(string name, string etag, CancellationToken cancellationToken = default)
    {
        var message = new HttpRequestMessage(HttpMethod.Delete, $"api/oidc/scopes/{Uri.EscapeDataString(name)}");
        message.Headers.TryAddWithoutValidation("If-Match", etag);
        return factory.CreateClient().SendAsync(message, cancellationToken);
    }
}

public static class ScopesServiceExtensions
{
    public static IServiceCollection AddScopesClient(this IServiceCollection services) => services.AddScoped<IScopesClient, ScopesClient>();
}
