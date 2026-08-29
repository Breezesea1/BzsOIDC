using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ProtectedResourceUpsertRequest = BzsOIDC.Idp.Services.Identity.ProtectedResourceUpsertRequest;
using PermissionDefinitionUpsertRequest = BzsOIDC.Idp.Services.Identity.PermissionDefinitionUpsertRequest;
using BzsOIDC.Idp.Controllers;
using BzsOIDC.Idp.Infra;
using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Services.Authorization;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Contracts;
using BzsOIDC.Shared.Infrastructure.Http;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BzsOIDC.Idp.Services.Oidc;
using BzsOIDC.Idp.Infra.Oidc;
using NSubstitute;

namespace BzsOIDC.Idp.IntegrationTests;

public sealed class PermissionTopologyApiIntegrationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private IOidcScopeService _scopeService = null!;

    [Fact]
    public async Task ScopesList_WithPagingAndSearch_ReturnsPagedEnvelope()
    {
        _scopeService.ListAsync(Arg.Any<OidcScopeListQuery>(), Arg.Any<CancellationToken>())
            .Returns(new OidcScopeListResponse { Page = 1, PageSize = 25, TotalCount = 2, Items = [new OidcScopeResponse { Name = "api" }, new OidcScopeResponse { Name = "profile" }] });
        using var response = await _client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, "/api/oidc/scopes?search=api&page=1&pageSize=25", permissions: PermissionConstants.ScopesRead));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OidcScopeListResponse>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload.TotalCount);
        await _scopeService.Received(1).ListAsync(Arg.Is<OidcScopeListQuery>(q => q.Search == "api" && q.PageSize == 25), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScopesWrite_RequiresIfMatchAndRejectsStaleTag()
    {
        _scopeService.GetByNameAsync("api", Arg.Any<CancellationToken>()).Returns(new OidcScopeResponse { Name = "api", ETag = "\"current\"" });
        using var missing = CreateAuthorizedRequest(HttpMethod.Put, "/api/oidc/scopes/api", new OidcScopeUpsertRequest { Name = "api", Resources = ["api"] }, PermissionConstants.ScopesWrite);
        await AddAntiforgeryAsync(missing);
        using var missingResponse = await _client.SendAsync(missing);
        Assert.Equal(HttpStatusCode.PreconditionRequired, missingResponse.StatusCode);

        using var stale = CreateAuthorizedRequest(HttpMethod.Put, "/api/oidc/scopes/api", new OidcScopeUpsertRequest { Name = "api", Resources = ["api"] }, PermissionConstants.ScopesWrite);
        await AddAntiforgeryAsync(stale);
        stale.Headers.TryAddWithoutValidation("If-Match", "\"stale\"");
        using var staleResponse = await _client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleResponse.StatusCode);
    }

    [Fact]
    public async Task ScopesWrite_WithoutPermission_ReturnsForbidden()
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/oidc/scopes", new OidcScopeUpsertRequest { Name = "api", Resources = ["api"] }, permissions: string.Empty);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ScopesWrite_WithoutAntiforgery_ReturnsBadRequest()
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, "/api/oidc/scopes", new OidcScopeUpsertRequest { Name = "custom", Resources = ["api"] }, PermissionConstants.ScopesWrite);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReservedScope_Delete_ReturnsConflict()
    {
        _scopeService.GetByNameAsync("openid", Arg.Any<CancellationToken>()).Returns(new OidcScopeResponse { Name = "openid", IsReserved = true, ETag = "\"reserved\"" });
        using var request = CreateAuthorizedRequest(HttpMethod.Delete, "/api/oidc/scopes/openid", permissions: PermissionConstants.ScopesWrite);
        await AddAntiforgeryAsync(request);
        request.Headers.TryAddWithoutValidation("If-Match", "\"reserved\"");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_WithoutAuth_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/permission-catalog/resources");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownApiRoute_ReturnsProblemJsonInsteadOfNotFoundHtml()
    {
        using var response = await _client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var payload = await response.Content.ReadFromJsonAsync<ApiProblemDetails>();
        Assert.NotNull(payload);
        Assert.Equal(ApiErrorCodes.NotFound, payload.Code);
        Assert.False(string.IsNullOrWhiteSpace(payload.TraceId));
    }

    [Fact]
    public async Task Session_Anonymous_ReturnsNonCacheableSummary()
    {
        using var response = await _client.GetAsync("/api/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var payload = await response.Content.ReadFromJsonAsync<SessionSummary>();
        Assert.NotNull(payload);
        Assert.False(payload.IsAuthenticated);
    }

    [Fact]
    public async Task Session_Authenticated_ExposesDisplayIdentityAndPermissions()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        request.Headers.Add(TestAuthHandler.UserHeader, "integration-user");
        request.Headers.Add(TestAuthHandler.PermissionHeader, "users.read.all");

        using var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadFromJsonAsync<SessionSummary>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.IsAuthenticated);
        Assert.Equal("integration-user", payload.UserName);
        Assert.Contains("users.read.all", payload.Permissions);
    }

    [Fact]
    public async Task Antiforgery_ReturnsTokenMetadataAndCookie()
    {
        using var response = await _client.GetAsync("/api/security/antiforgery");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var payload = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.Token));
        Assert.False(string.IsNullOrWhiteSpace(payload.HeaderName));
    }

    [Fact]
    public async Task PermissionTopology_WhenLegacyRouteHasValidClaims_WorksEndToEnd()
    {
        using var resourceRequest = CreateAuthorizedRequest(
            HttpMethod.Put,
            "/api/permission-catalog/resources/orders-api",
            new ProtectedResourceUpsertRequest { DisplayName = "Orders API" });
        await AddAntiforgeryAsync(resourceRequest);
        using var resourceResponse = await _client.SendAsync(resourceRequest);
        resourceResponse.EnsureSuccessStatusCode();

        using var permissionRequest = CreateAuthorizedRequest(
            HttpMethod.Put,
            "/api/permission-catalog/resources/orders-api/permissions/orders.read",
            new PermissionDefinitionUpsertRequest { DisplayName = "Read orders" });
        await AddAntiforgeryAsync(permissionRequest);
        using var permissionResponse = await _client.SendAsync(permissionRequest);
        permissionResponse.EnsureSuccessStatusCode();

        using var upsertRequest = CreateAuthorizedRequest(
            HttpMethod.Put,
            "/api/permission-catalog/permissions/orders.read/release-scopes",
            new PermissionReleaseScopesUpsertRequest { Scopes = ["api", "internal"] });
        await AddAntiforgeryAsync(upsertRequest);
        upsertRequest.Headers.TryAddWithoutValidation(
            "If-Match",
            permissionResponse.Headers.ETag?.Tag ?? throw new InvalidOperationException("Permission response did not include an ETag."));

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
        await AddAntiforgeryAsync(createRequest);
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
        await AddAntiforgeryAsync(updateRequest);
        updateRequest.Headers.TryAddWithoutValidation("If-Match", created.ETag);
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
        await AddAntiforgeryAsync(syncRequest);
        syncRequest.Headers.TryAddWithoutValidation("If-Match", updated.ETag);
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
        await AddAntiforgeryAsync(deleteRequest);
        deleteRequest.Headers.TryAddWithoutValidation(
            "If-Match",
            syncResponse.Headers.ETag?.Tag ?? throw new InvalidOperationException("Permission sync response did not include an ETag."));
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
        builder.Services.AddAntiforgery();
        builder.Services.Configure<PermissionPolicyOptions>(_ => { });
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        builder.Services.AddMemoryCache();
        builder.Services.AddDataProtectionKeyStorage(new DataProtectionOptions
        {
            ApplicationName = "BzsOIDC.Idp.Test",
            StorageDirectory = Path.Combine(AppContext.BaseDirectory, "UnitTestDataProtectionKeys"),
            KeyLifetimeDays = 365,
        });
        builder.Services.AddDbContext<IdpDbContext>(options => options.UseSqlite(_connection));
        builder.Services.AddIdentityCore<BzsUser>()
            .AddRoles<BzsRole>()
            .AddEntityFrameworkStores<IdpDbContext>()
            .AddDefaultTokenProviders();
        builder.Services.AddScoped<RoleManagementPolicy>();
        builder.Services.AddScoped<IPermissionTopology, PermissionTopologyService>();
        _scopeService = Substitute.For<IOidcScopeService>();
        builder.Services.AddSingleton(_scopeService);

        builder.Services
            .AddControllersWithViews()
            .AddApplicationPart(typeof(PermissionTopologyController).Assembly);

        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseAntiforgery();
        _app.UseWhen(
            context => ApiProblemDetailsWriter.IsApiRequest(context.Request),
            apiBranch => apiBranch.UseStatusCodePages(async statusContext =>
            {
                var context = statusContext.HttpContext;
                if (context.Response.StatusCode >= StatusCodes.Status400BadRequest && !context.Response.HasStarted)
                {
                    await ApiProblemDetailsWriter.WriteAsync(
                        context,
                        context.Response.StatusCode,
                        ApiProblemDetailsWriter.CodeForStatus(context.Response.StatusCode));
                }
            }));
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

    private async Task AddAntiforgeryAsync(HttpRequestMessage request)
    {
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "/api/security/antiforgery");
        if (request.Headers.TryGetValues(TestAuthHandler.UserHeader, out var users))
        {
            tokenRequest.Headers.TryAddWithoutValidation(TestAuthHandler.UserHeader, users);
        }
        if (request.Headers.TryGetValues(TestAuthHandler.PermissionHeader, out var permissions))
        {
            tokenRequest.Headers.TryAddWithoutValidation(TestAuthHandler.PermissionHeader, permissions);
        }
        using var response = await _client.SendAsync(tokenRequest);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>()
            ?? throw new InvalidOperationException("Missing antiforgery token.");
        request.Headers.TryAddWithoutValidation(token.HeaderName, token.Token);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            var cookie = string.Join("; ", values.Select(static value => value.Split(';', 2)[0]));
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }
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
            new(ClaimTypes.NameIdentifier, userValues.ToString()),
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
