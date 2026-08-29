using System.Net.Http.Json;
using BzsOIDC.Contracts;
using BzsOIDC.Idp.Client.Services.Session;

namespace BzsOIDC.Idp.Client.Services.Permissions;

public interface IPermissionTopologyClient
{
    Task<IReadOnlyList<ProtectedResourceDetail>?> GetResourcesAsync(CancellationToken cancellationToken = default);
    Task<ProtectedResourceDetail?> GetResourceAsync(string resourceKey, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> UpsertResourceAsync(string resourceKey, ProtectedResourceUpsertContract request, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> UpsertResourceAsync(string resourceKey, ProtectedResourceUpsertContract request, string? etag, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> UpsertPermissionAsync(string resourceKey, string permissionName, PermissionDefinitionUpsertContract request, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> UpsertPermissionAsync(string resourceKey, string permissionName, PermissionDefinitionUpsertContract request, string? etag, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> SyncReleaseScopesAsync(string permissionName, PermissionReleaseScopesContract request, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> SyncReleaseScopesAsync(string permissionName, PermissionReleaseScopesContract request, string? etag, CancellationToken cancellationToken = default);
    Task<RoleListResponse?> ListRolesAsync(string? search = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<RoleResponse?> GetRoleAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>?> GetRolePermissionsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> CreateRoleAsync(RoleUpsertRequest request, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> UpdateRoleAsync(Guid id, RoleUpsertRequest request, string etag, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> DeleteRoleAsync(Guid id, string etag, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> SyncRolePermissionsAsync(Guid id, RolePermissionSyncRequest request, string etag, CancellationToken cancellationToken = default);
}

public sealed record RoleResponse(Guid Id, string Name, string NormalizedName, bool IsProtected, int PermissionCount, string[] Permissions, string ETag);
public sealed record RoleListResponse(IReadOnlyList<RoleResponse> Items, int TotalCount, int Page, int PageSize);
public sealed record RoleUpsertRequest(string Name);
public sealed record RolePermissionSyncRequest(string[] Permissions);

internal sealed class PermissionTopologyClient(IApiHttpClientFactory factory) : IPermissionTopologyClient
{
    // The catalog path is the established public contract; the server also exposes
    // the topology aliases for new callers.
    private const string BasePath = "api/permission-catalog";

    public Task<IReadOnlyList<ProtectedResourceDetail>?> GetResourcesAsync(CancellationToken cancellationToken = default)
        => factory.CreateClient().GetFromJsonAsync<IReadOnlyList<ProtectedResourceDetail>>($"{BasePath}/resources", cancellationToken);

    public Task<ProtectedResourceDetail?> GetResourceAsync(string resourceKey, CancellationToken cancellationToken = default)
        => factory.CreateClient().GetFromJsonAsync<ProtectedResourceDetail>($"{BasePath}/resources/{Uri.EscapeDataString(resourceKey)}", cancellationToken);

    public Task<HttpResponseMessage> UpsertResourceAsync(string resourceKey, ProtectedResourceUpsertContract request, CancellationToken cancellationToken = default)
        => UpsertResourceAsync(resourceKey, request, null, cancellationToken);

    public Task<HttpResponseMessage> UpsertResourceAsync(string resourceKey, ProtectedResourceUpsertContract request, string? etag, CancellationToken cancellationToken = default)
        => SendWithEtag(HttpMethod.Put, $"{BasePath}/resources/{Uri.EscapeDataString(resourceKey)}", request, etag, cancellationToken);

    public Task<HttpResponseMessage> UpsertPermissionAsync(string resourceKey, string permissionName, PermissionDefinitionUpsertContract request, CancellationToken cancellationToken = default)
        => UpsertPermissionAsync(resourceKey, permissionName, request, null, cancellationToken);

    public Task<HttpResponseMessage> UpsertPermissionAsync(string resourceKey, string permissionName, PermissionDefinitionUpsertContract request, string? etag, CancellationToken cancellationToken = default)
        => SendWithEtag(HttpMethod.Put, $"{BasePath}/resources/{Uri.EscapeDataString(resourceKey)}/permissions/{Uri.EscapeDataString(permissionName)}", request, etag, cancellationToken);

    public Task<HttpResponseMessage> SyncReleaseScopesAsync(string permissionName, PermissionReleaseScopesContract request, CancellationToken cancellationToken = default)
        => SyncReleaseScopesAsync(permissionName, request, null, cancellationToken);

    public Task<HttpResponseMessage> SyncReleaseScopesAsync(string permissionName, PermissionReleaseScopesContract request, string? etag, CancellationToken cancellationToken = default)
        => SendWithEtag(HttpMethod.Put, $"{BasePath}/permissions/{Uri.EscapeDataString(permissionName)}/release-scopes", request, etag, cancellationToken);

    public async Task<RoleListResponse?> ListRolesAsync(string? search = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        using var response = await factory.CreateClient().GetAsync($"api/roles?page={page}&pageSize={Math.Clamp(pageSize, 1, 100)}" + (string.IsNullOrWhiteSpace(search) ? string.Empty : $"&search={Uri.EscapeDataString(search)}"), cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<RoleListResponse>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException)
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<IReadOnlyList<RoleResponse>>(json) ?? [];
            return new RoleListResponse(items, items.Count, page, pageSize);
        }
    }

    public Task<RoleResponse?> GetRoleAsync(Guid id, CancellationToken cancellationToken = default)
        => factory.CreateClient().GetFromJsonAsync<RoleResponse>($"api/roles/{id}", cancellationToken);

    public Task<IReadOnlyList<string>?> GetRolePermissionsAsync(Guid id, CancellationToken cancellationToken = default)
        => factory.CreateClient().GetFromJsonAsync<IReadOnlyList<string>>($"{BasePath}/roles/{id}/permissions", cancellationToken);

    public Task<HttpResponseMessage> CreateRoleAsync(RoleUpsertRequest request, CancellationToken cancellationToken = default)
        => factory.CreateClient().PostAsJsonAsync("api/roles", request, cancellationToken);

    public Task<HttpResponseMessage> UpdateRoleAsync(Guid id, RoleUpsertRequest request, string etag, CancellationToken cancellationToken = default)
        => SendWithEtag(HttpMethod.Put, $"api/roles/{id}", request, etag, cancellationToken);

    public Task<HttpResponseMessage> DeleteRoleAsync(Guid id, string etag, CancellationToken cancellationToken = default)
        => SendWithEtag<object?>(HttpMethod.Delete, $"api/roles/{id}", null, etag, cancellationToken);

    public Task<HttpResponseMessage> SyncRolePermissionsAsync(Guid id, RolePermissionSyncRequest request, string etag, CancellationToken cancellationToken = default)
        => SendWithEtag(HttpMethod.Put, $"api/roles/{id}/permissions", request, etag, cancellationToken);

    private Task<HttpResponseMessage> SendWithEtag<T>(HttpMethod method, string path, T? body, string? etag, CancellationToken cancellationToken)
    {
        var message = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        if (!string.IsNullOrWhiteSpace(etag))
        {
            message.Headers.TryAddWithoutValidation("If-Match", etag);
        }
        return factory.CreateClient().SendAsync(message, cancellationToken);
    }
}

public static class PermissionTopologyServiceExtensions
{
    public static IServiceCollection AddPermissionTopologyClient(this IServiceCollection services)
        => services.AddScoped<IPermissionTopologyClient, PermissionTopologyClient>();
}
