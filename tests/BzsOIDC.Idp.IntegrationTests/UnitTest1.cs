using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BzsOIDC.Idp.Controllers;
using BzsOIDC.Idp.Infra;
using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Services.Authorization;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BzsOIDC.Idp.IntegrationTests;

public sealed class PermissionTopologyApiIntegrationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [Fact]
    public async Task GetAll_WithoutAuth_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/permission-catalog/resources");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PermissionTopology_WhenLegacyRouteHasValidClaims_WorksEndToEnd()
    {
        using var resourceRequest = CreateAuthorizedRequest(
            HttpMethod.Put,
            "/api/permission-catalog/resources/orders-api",
            new ProtectedResourceUpsertRequest { DisplayName = "Orders API" });
        using var resourceResponse = await _client.SendAsync(resourceRequest);
        resourceResponse.EnsureSuccessStatusCode();

        using var permissionRequest = CreateAuthorizedRequest(
            HttpMethod.Put,
            "/api/permission-catalog/resources/orders-api/permissions/orders.read",
            new PermissionDefinitionUpsertRequest { DisplayName = "Read orders" });
        using var permissionResponse = await _client.SendAsync(permissionRequest);
        permissionResponse.EnsureSuccessStatusCode();

        using var upsertRequest = CreateAuthorizedRequest(
            HttpMethod.Put,
            "/api/permission-catalog/permissions/orders.read/release-scopes",
            new PermissionReleaseScopesUpsertRequest { Scopes = ["api", "internal"] });

        using var upsertResponse = await _client.SendAsync(upsertRequest);
        upsertResponse.EnsureSuccessStatusCode();

        var upsertPayload = await upsertResponse.Content.ReadFromJsonAsync<PermissionDefinitionResponse>();
        Assert.NotNull(upsertPayload);
        Assert.Equal("orders.read", upsertPayload.Name);
        Assert.Equal(["api", "internal"], upsertPayload.ReleaseScopes.OrderBy(static x => x).ToArray());

        using var getRequest = CreateAuthorizedRequest(HttpMethod.Get, "/api/permission-catalog/resources/orders-api");
        using var getResponse = await _client.SendAsync(getRequest);
        getResponse.EnsureSuccessStatusCode();

        var getPayload = await getResponse.Content.ReadFromJsonAsync<ProtectedResourceResponse>();
        Assert.NotNull(getPayload);
        Assert.Equal("orders-api", getPayload.Key);
        Assert.Contains(getPayload.Permissions, static permission => permission.Name == "orders.read");
    }

    [Fact]
    public async Task RoleManagement_WithValidRolePermissionClaims_WorksEndToEnd()
    {
        await SeedPermissionAsync(PermissionConstants.UsersReadAll);

        using var createRequest = CreateAuthorizedRequest(
            HttpMethod.Post,
            "/api/roles",
            new RoleUpsertRequest { Name = "operators" },
            $"{PermissionConstants.RolesRead},{PermissionConstants.RolesWrite}");
        using var createResponse = await _client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<RoleResponse>();
        Assert.NotNull(created);
        Assert.Equal("operators", created.Name);

        using var updateRequest = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/roles/{created.Id}",
            new RoleUpsertRequest { Name = "support" },
            $"{PermissionConstants.RolesRead},{PermissionConstants.RolesWrite}");
        using var updateResponse = await _client.SendAsync(updateRequest);
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<RoleResponse>();
        Assert.NotNull(updated);
        Assert.Equal("support", updated.Name);

        using var syncRequest = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/roles/{created.Id}/permissions",
            new RolePermissionSyncRequest { Permissions = [PermissionConstants.UsersReadAll] },
            $"{PermissionConstants.RolesRead},{PermissionConstants.RolesWrite}");
        using var syncResponse = await _client.SendAsync(syncRequest);
        Assert.Equal(HttpStatusCode.NoContent, syncResponse.StatusCode);

        using var permissionsRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/roles/{created.Id}/permissions",
            permissions: PermissionConstants.RolesRead);
        using var permissionsResponse = await _client.SendAsync(permissionsRequest);
        permissionsResponse.EnsureSuccessStatusCode();

        var permissions = await permissionsResponse.Content.ReadFromJsonAsync<string[]>();
        Assert.NotNull(permissions);
        Assert.Contains(PermissionConstants.UsersReadAll, permissions);

        using var deleteRequest = CreateAuthorizedRequest(
            HttpMethod.Delete,
            $"/api/roles/{created.Id}",
            permissions: PermissionConstants.RolesWrite);
        using var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });

        builder.WebHost.UseTestServer();

        builder.Services
            .AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

        builder.Services.AddAuthorization();
        builder.Services.Configure<PermissionPolicyOptions>(_ => { });
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        builder.Services.AddMemoryCache();
        builder.Services.AddDbContext<IdpDbContext>(options => options.UseSqlite(_connection));
        builder.Services.AddIdentityCore<BzsUser>()
            .AddRoles<BzsRole>()
            .AddEntityFrameworkStores<IdpDbContext>()
            .AddDefaultTokenProviders();
        builder.Services.AddScoped<RoleManagementPolicy>();
        builder.Services.AddScoped<IPermissionTopology, PermissionTopologyService>();

        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(PermissionTopologyController).Assembly);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();

        await _app.StartAsync();

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IdpDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task SeedPermissionAsync(string permissionName)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var permissionTopology = scope.ServiceProvider.GetRequiredService<IPermissionTopology>();
        var resourceResult = await permissionTopology.UpsertResourceAsync(
            PermissionConstants.ScopeApi,
            new ProtectedResourceUpsertRequest { DisplayName = "BzsOIDC Admin API" });
        Assert.Equal(PermissionTopologyCommandStatus.Success, resourceResult.Status);

        var permissionResult = await permissionTopology.UpsertPermissionAsync(
            PermissionConstants.ScopeApi,
            permissionName,
            new PermissionDefinitionUpsertRequest { DisplayName = permissionName });
        Assert.Equal(PermissionTopologyCommandStatus.Success, permissionResult.Status);

        var scopeResult = await permissionTopology.SyncReleaseScopesAsync(permissionName, [PermissionConstants.ScopeApi]);
        Assert.Equal(PermissionTopologyCommandStatus.Success, scopeResult.Status);
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string url,
        object? content = null,
        string permissions = "permissions.read,permissions.write")
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.UserHeader, "integration-user");
        request.Headers.Add(TestAuthHandler.PermissionHeader, permissions);

        if (content is not null)
        {
            request.Content = JsonContent.Create(content);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        return request;
    }
}

internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "Test";
    internal const string UserHeader = "X-Test-User";
    internal const string PermissionHeader = "X-Test-Permissions";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userValues) ||
            string.IsNullOrWhiteSpace(userValues.ToString()))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing test user header."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userValues.ToString()),
        };

        if (Request.Headers.TryGetValue(PermissionHeader, out var permissionValues))
        {
            var permissions = permissionValues
                .ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            claims.AddRange(permissions.Select(static permission =>
                new Claim(PermissionConstants.ClaimType, permission)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
