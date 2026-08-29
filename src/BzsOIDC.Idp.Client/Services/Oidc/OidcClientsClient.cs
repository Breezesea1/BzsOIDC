using System.Net.Http.Json;
using BzsOIDC.Idp.Client.Services.Session;

namespace BzsOIDC.Idp.Client.Services.Oidc;

public sealed class OidcClientListResponse
{
    public IReadOnlyList<OidcClientResponse> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed class OidcClientResponse
{
    public string ClientId { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public OidcClientAuthFlow AuthFlow { get; init; }
    public bool PublicClient { get; init; }
    public OidcClientConsentType ConsentType { get; init; }
    public string[] GrantTypes { get; init; } = [];
    public string[] Scopes { get; init; } = [];
    public string[] RedirectUris { get; init; } = [];
    public string[] PostLogoutRedirectUris { get; init; } = [];
    public string ETag { get; init; } = string.Empty;
}

public enum OidcClientAuthFlow
{
    AuthorizationCode,
    ClientCredentials,
}

public enum OidcClientConsentType
{
    Unknown = -1,
    Implicit,
    Explicit,
    External,
}

public sealed class OidcClientUpdateRequest
{
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public OidcClientAuthFlow AuthFlow { get; init; }
    public bool PublicClient { get; init; }
    public bool RequireProofKeyForCodeExchange { get; init; } = true;
    public OidcClientConsentType ConsentType { get; init; }
    public string[] GrantTypes { get; init; } = ["authorization_code"];
    public string[] Scopes { get; init; } = [];
    public string[] RedirectUris { get; init; } = [];
    public string[] PostLogoutRedirectUris { get; init; } = [];
}

public sealed class OidcClientSecretResponse
{
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
}

public sealed class OidcClientRegistrationResponse
{
    public string ClientId { get; init; } = string.Empty;
    public string? ClientSecret { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public OidcClientAuthFlow AuthFlow { get; init; }
    public string ETag { get; init; } = string.Empty;
}

public interface IOidcClientsClient
{
    Task<OidcClientListResponse?> ListAsync(string? search = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<OidcClientResponse?> GetAsync(string clientId, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> CreateAsync(OidcClientUpdateRequest request, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> UpdateAsync(string clientId, object request, string etag, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> DeleteAsync(string clientId, string etag, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> RotateSecretAsync(string clientId, string etag, CancellationToken cancellationToken = default);
}

internal sealed class OidcClientsClient(IApiHttpClientFactory factory) : IOidcClientsClient
{
    public Task<OidcClientListResponse?> ListAsync(string? search = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var query = $"api/oidc/clients?page={Math.Max(1, page)}&pageSize={Math.Clamp(pageSize, 1, 100)}" + (string.IsNullOrWhiteSpace(search) ? string.Empty : $"&search={Uri.EscapeDataString(search)}");
        return factory.CreateClient().GetFromJsonAsync<OidcClientListResponse>(query, cancellationToken);
    }

    public Task<OidcClientResponse?> GetAsync(string clientId, CancellationToken cancellationToken = default)
        => factory.CreateClient().GetFromJsonAsync<OidcClientResponse>($"api/oidc/clients/{Uri.EscapeDataString(clientId)}", cancellationToken);

    public Task<HttpResponseMessage> CreateAsync(OidcClientUpdateRequest request, CancellationToken cancellationToken = default)
        => factory.CreateClient().PostAsJsonAsync("api/oidc/clients", request, cancellationToken);

    public Task<HttpResponseMessage> UpdateAsync(string clientId, object request, string etag, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Put, $"api/oidc/clients/{Uri.EscapeDataString(clientId)}", request, etag, cancellationToken);

    public Task<HttpResponseMessage> DeleteAsync(string clientId, string etag, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Delete, $"api/oidc/clients/{Uri.EscapeDataString(clientId)}", null, etag, cancellationToken);

    public Task<HttpResponseMessage> RotateSecretAsync(string clientId, string etag, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"api/oidc/clients/{Uri.EscapeDataString(clientId)}/secret/rotate", null, etag, cancellationToken);

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, object? body, string etag, CancellationToken cancellationToken)
    {
        var message = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body) };
        message.Headers.TryAddWithoutValidation("If-Match", etag);
        return factory.CreateClient().SendAsync(message, cancellationToken);
    }
}

public static class OidcClientsServiceExtensions
{
    public static IServiceCollection AddOidcClientsClient(this IServiceCollection services)
        => services.AddScoped<IOidcClientsClient, OidcClientsClient>();
}
