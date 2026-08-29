using System.Net;
using System.Net.Http.Json;
using BzsOIDC.Contracts;
using BzsOIDC.Idp.Infra;
using BzsOIDC.Idp.Controllers;
using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Services.Authorization;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ProtectedResourceUpsertRequest = BzsOIDC.Idp.Services.Identity.ProtectedResourceUpsertRequest;
using PermissionDefinitionUpsertRequest = BzsOIDC.Idp.Services.Identity.PermissionDefinitionUpsertRequest;

namespace BzsOIDC.Idp.IntegrationTests;

public sealed class UsersApiIntegrationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private BzsUser _user = null!;

    [Fact]
    public async Task List_RequiresReadPermission_AndSupportsPagingSearchSort()
    {
        var denied = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        denied.Headers.Add(TestAuthHandler.UserHeader, "integration-user");
        var deniedResponse = await _client.SendAsync(denied);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var request = Authorized(HttpMethod.Get, "/api/users?pageSize=1&search=integration&sort=email&direction=desc", PermissionConstants.UsersReadAll);
        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadFromJsonAsync<UserListResponse>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(1, payload.PageSize);
        Assert.True(payload.TotalCount >= 1);
    }

    [Fact]
    public async Task Update_RequiresIfMatch_AndRejectsStaleTag()
    {
        using var listRequest = Authorized(HttpMethod.Get, "/api/users", PermissionConstants.UsersReadAll);
        var user = (await (await _client.SendAsync(listRequest)).Content.ReadFromJsonAsync<UserListResponse>())!.Items[0];
        using var missing = Authorized(HttpMethod.Put, $"/api/users/{user.Id}", PermissionConstants.UsersWrite, new UserUpdateRequest(user.UserName, user.Email, null, user.IsAdmin));
        await AddAntiforgeryAsync(missing);
        Console.WriteLine($"request headers: {string.Join(" | ", missing.Headers.Select(h => h.Key + "=" + string.Join(",", h.Value)))}");
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(missing)).StatusCode);

        using var stale = Authorized(HttpMethod.Put, $"/api/users/{user.Id}", PermissionConstants.UsersWrite, new UserUpdateRequest(user.UserName, user.Email, null, user.IsAdmin));
        await AddAntiforgeryAsync(stale);
        stale.Headers.TryAddWithoutValidation("If-Match", "\"stale\"");
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(stale)).StatusCode);
    }

    [Fact]
    public async Task Delete_CurrentActor_IsProtected()
    {
        using var list = Authorized(HttpMethod.Get, "/api/users", PermissionConstants.UsersReadAll);
        var current = (await (await _client.SendAsync(list)).Content.ReadFromJsonAsync<UserListResponse>())!.Items.Single(u => u.Id == _user.Id);
        using var request = Authorized(HttpMethod.Delete, $"/api/users/{_user.Id}", PermissionConstants.UsersWrite);
        await AddAntiforgeryAsync(request);
        request.Headers.TryAddWithoutValidation("If-Match", current.ETag);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_CurrentActor_ServiceProtection_ReturnsConflictCode()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var administration = scope.ServiceProvider.GetRequiredService<IUserAdministration>();
        var result = await administration.DeleteAsync(_user.Id, _user.Id);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == UserAdministrationErrorCodes.CurrentSessionDeletion);
    }

    [Fact]
    public async Task MutationWithoutAntiforgery_IsRejected()
    {
        using var request = Authorized(HttpMethod.Post, "/api/users", PermissionConstants.UsersWrite, new UserCreateRequest("new-user", "Password1!", null, false));
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendAsync(request)).StatusCode);
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:"); await _connection.OpenAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddAntiforgery();
        builder.Services.AddMemoryCache();
        builder.Services.Configure<PermissionPolicyOptions>(_ => { }); builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>(); builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddDbContext<IdpDbContext>(o => o.UseSqlite(_connection));
        builder.Services.AddIdentityCore<BzsUser>().AddRoles<BzsRole>().AddEntityFrameworkStores<IdpDbContext>().AddDefaultTokenProviders();
        builder.Services.AddScoped<RoleManagementPolicy>();
        builder.Services.AddScoped<IPermissionTopology, PermissionTopologyService>();
        builder.Services.AddScoped<IUserAdministration, UserAdministration>();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(UsersController).Assembly);
        _app = builder.Build();
        _app.Use(async (context, next) => { try { await next(); } catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) { context.Response.StatusCode = 400; } });
        _app.UseAuthentication(); _app.UseAuthorization(); _app.UseAntiforgery(); _app.MapControllers(); await _app.StartAsync();
        await using var scope = _app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<IdpDbContext>(); await db.Database.EnsureCreatedAsync();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<BzsUser>>();
        _user = new BzsUser { UserName = "integration-user", Email = "integration@example.com" }; await manager.CreateAsync(_user, "Password1!");
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<BzsRole>>();
        await roleManager.CreateAsync(new BzsRole { Name = IdentitySeedConstants.AdminRoleName });
        await manager.AddToRoleAsync(_user, IdentitySeedConstants.AdminRoleName);
        await SeedPermissionAsync(PermissionConstants.UsersReadAll);
        await SeedPermissionAsync(PermissionConstants.UsersWrite);
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync() { _client.Dispose(); await _app.StopAsync(); await _app.DisposeAsync(); await _connection.DisposeAsync(); }

    private HttpRequestMessage Authorized(HttpMethod method, string url, string permission, object? body = null)
    {
        var request = new HttpRequestMessage(method, url); request.Headers.Add(TestAuthHandler.UserHeader, "integration-user"); request.Headers.Add(TestAuthHandler.PermissionHeader, permission); if (body is not null) request.Content = JsonContent.Create(body); return request;
    }

    private async Task SeedPermissionAsync(string permissionName)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var topology = scope.ServiceProvider.GetRequiredService<IPermissionTopology>();
        await topology.UpsertResourceAsync(PermissionConstants.ScopeApi, new ProtectedResourceUpsertRequest { DisplayName = "Users API" });
        await topology.UpsertPermissionAsync(PermissionConstants.ScopeApi, permissionName, new PermissionDefinitionUpsertRequest { DisplayName = permissionName });
        await topology.SyncReleaseScopesAsync(permissionName, [PermissionConstants.ScopeApi]);
    }

    private async Task AddAntiforgeryAsync(HttpRequestMessage request)
    {
        using var response = await _client.GetAsync("/api/security/antiforgery");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>()
            ?? throw new InvalidOperationException("Missing antiforgery token.");
        request.Headers.TryAddWithoutValidation(token.HeaderName, token.Token);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", values.Select(static value => value.Split(';', 2)[0])));
        }
    }
}
