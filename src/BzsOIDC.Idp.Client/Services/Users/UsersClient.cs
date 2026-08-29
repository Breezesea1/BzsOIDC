using System.Net;
using System.Net.Http.Json;
using BzsOIDC.Contracts;
using BzsOIDC.Idp.Client.Services.Session;

namespace BzsOIDC.Idp.Client.Services.Users;

public interface IUsersClient
{
    Task<UserListResponse?> ListAsync(string? search = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> CreateAsync(UserCreateRequest request, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> UpdateAsync(Guid id, UserUpdateRequest request, string etag, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> DeleteAsync(Guid id, string etag, CancellationToken cancellationToken = default);
}

internal sealed class UsersClient(IApiHttpClientFactory factory) : IUsersClient
{
    public async Task<UserListResponse?> ListAsync(string? search = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var query = $"api/users?page={page}&pageSize={Math.Clamp(pageSize, 1, 100)}" + (string.IsNullOrWhiteSpace(search) ? string.Empty : $"&search={Uri.EscapeDataString(search)}");
        return await factory.CreateClient().GetFromJsonAsync<UserListResponse>(query, cancellationToken);
    }

    public Task<HttpResponseMessage> CreateAsync(UserCreateRequest request, CancellationToken cancellationToken = default) => factory.CreateClient().PostAsJsonAsync("api/users", request, cancellationToken);

    public Task<HttpResponseMessage> UpdateAsync(Guid id, UserUpdateRequest request, string etag, CancellationToken cancellationToken = default)
    {
        var message = new HttpRequestMessage(HttpMethod.Put, $"api/users/{id}") { Content = JsonContent.Create(request) };
        message.Headers.TryAddWithoutValidation("If-Match", etag);
        return factory.CreateClient().SendAsync(message, cancellationToken);
    }

    public Task<HttpResponseMessage> DeleteAsync(Guid id, string etag, CancellationToken cancellationToken = default)
    {
        var message = new HttpRequestMessage(HttpMethod.Delete, $"api/users/{id}");
        message.Headers.TryAddWithoutValidation("If-Match", etag);
        return factory.CreateClient().SendAsync(message, cancellationToken);
    }
}

public static class UsersServiceExtensions
{
    public static IServiceCollection AddUsersClient(this IServiceCollection services)
    {
        services.AddScoped<IUsersClient, UsersClient>();
        return services;
    }
}
