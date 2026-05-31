using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using BootstrapBlazor.Components;
using BzsOIDC.Idp.Components;
using BzsOIDC.Idp.Controllers;
using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Client.Services.Dashboard;
using BzsOIDC.Idp.Infra;
using BzsOIDC.Idp.Infra.Oidc;
using BzsOIDC.Idp.Services.Admin;
using BzsOIDC.Idp.Services;
using BzsOIDC.Idp.Services.Authorization;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.Services.Oidc;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using OpenIddict.Abstractions;

namespace BzsOIDC.Idp.IntegrationTests;

public sealed class ConnectControllerIntegrationTests : IAsyncLifetime
{
    private const string MachineClientId = "machine-client";
    private const string MachineClientSecret = "machine-client-secret";
    private const string WebClientId = "web-client";
    private static readonly Uri BaseUri = new("https://localhost");
    private static readonly Uri WebRedirectUri = new("https://localhost/callback");

    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string? _authCookieHeader;

    [Fact]
    public async Task Authorize_WhenUnauthenticated_RedirectsToLogin()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/connect/authorize?client_id=web-client&response_type=code&scope=openid%20profile&redirect_uri=https%3A%2F%2Flocalhost%2Fcallback&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256");
        request.Headers.Accept.ParseAdd("text/html");

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = response.Headers.Location?.OriginalString;
        Assert.False(string.IsNullOrWhiteSpace(location));
        Assert.Contains("/login", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReturnUrl=", location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginPage_WhenRequestedDirectly_ReturnsServerOwnedLoginForm()
    {
        using var response = await _client.GetAsync("/login");

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<form method=\"post\" action=\"/account/login\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/account/external-login/github", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BzsOIDC", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExternalLogin_WhenGitHubRequested_RedirectsToGitHubAuthorizationEndpoint()
    {
        using var response = await _client.PostAsync("/account/external-login/github?returnUrl=%2Fadmin%2Fusers", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Contains("gho_test_valid_client_id", response.Headers.Location.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExternalLogin_WhenForwardedProtoIsHttps_GeneratesHttpsRedirectUri()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "http://auth.breezesea.fun/account/external-login/github?returnUrl=%2Fadmin%2Fusers");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "auth.breezesea.fun");
        request.Content = new FormUrlEncodedContent([]);

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var query = QueryHelpers.ParseQuery(response.Headers.Location.Query);
        Assert.True(query.TryGetValue("redirect_uri", out var redirectUris));
        Assert.Equal("https://auth.breezesea.fun/signin-github", redirectUris.ToString());
    }

    [Fact]
    public async Task AccessDeniedPage_WhenRequestedDirectly_ReturnsServerOwnedDeniedPage()
    {
        using var response = await _client.GetAsync("/account/denied");

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("class=\"denied-page\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"denied-secondary\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/login\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BzsOIDC", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LogoutPage_WhenRequestedDirectly_ReturnsServerOwnedLogoutForm()
    {
        using var response = await _client.GetAsync("/logout?returnUrl=%2F");

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<form", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("method=\"post\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("action=\"/account/logout?returnUrl=%2F\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"logout-form\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"logout-secondary\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Exchange_WhenGrantTypeUnsupported_ReturnsOpenIddictErrorPayload()
    {
        using var response = await _client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = OpenIddictConstants.GrantTypes.Password,
                ["client_id"] = MachineClientId,
                ["client_secret"] = MachineClientSecret,
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(payload);
        Assert.Equal(OpenIddictConstants.Errors.UnsupportedGrantType, payload.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task UserInfo_WithoutAccessToken_ReturnsUnauthorized()
    {
        using var response = await _client.GetAsync("/connect/userinfo");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Exchange_WhenClientCredentialsValid_ReturnsAccessTokenResponse()
    {
        using var response = await _client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = OpenIddictConstants.GrantTypes.ClientCredentials,
                ["client_id"] = MachineClientId,
                ["client_secret"] = MachineClientSecret,
                ["scope"] = PermissionConstants.ScopeApi,
            }));

        response.EnsureSuccessStatusCode();

        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("access_token").GetString()));
        Assert.Equal("Bearer", payload.RootElement.GetProperty("token_type").GetString());
    }

    [Fact]
    public async Task AuthorizationCodeFlow_WhenUserAuthenticated_ReturnsCodeThenTokens()
    {
        await SignInAsAdminAsync();

        var code = await RequestAuthorizationCodeAsync();
        using var tokenResponse = await ExchangeAuthorizationCodeAsync(code);

        tokenResponse.EnsureSuccessStatusCode();

        using var payload = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("access_token").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("refresh_token").GetString()));
        Assert.Equal("Bearer", payload.RootElement.GetProperty("token_type").GetString());
    }

    [Fact]
    public async Task Authorize_WhenPostedWithoutAntiforgeryToken_ReturnsBadRequest()
    {
        await SignInAsAdminAsync();

        using var response = await _client.PostAsync(
            "/connect/authorize",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = WebClientId,
                ["response_type"] = OpenIddictConstants.ResponseTypes.Code,
                ["redirect_uri"] = WebRedirectUri.ToString(),
                ["scope"] = OpenIddictConstants.Scopes.OpenId,
                ["state"] = "test-state",
                ["code_challenge"] = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
                ["code_challenge_method"] = "S256",
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RefreshTokenFlow_WhenRefreshTokenValid_ReturnsNewAccessToken()
    {
        await SignInAsAdminAsync();

        var code = await RequestAuthorizationCodeAsync();
        using var codeExchangeResponse = await ExchangeAuthorizationCodeAsync(code);
        codeExchangeResponse.EnsureSuccessStatusCode();

        using var codePayload = await codeExchangeResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(codePayload);
        var refreshToken = codePayload.RootElement.GetProperty("refresh_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));

        using var refreshResponse = await _client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = OpenIddictConstants.GrantTypes.RefreshToken,
                ["client_id"] = WebClientId,
                ["refresh_token"] = refreshToken!,
            }));

        refreshResponse.EnsureSuccessStatusCode();

        using var refreshPayload = await refreshResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(refreshPayload);
        Assert.False(string.IsNullOrWhiteSpace(refreshPayload.RootElement.GetProperty("access_token").GetString()));
        Assert.Equal("Bearer", refreshPayload.RootElement.GetProperty("token_type").GetString());
    }

    [Fact]
    public async Task Revocation_WhenRefreshTokenRevoked_PreventsRefreshTokenReuse()
    {
        await SignInAsAdminAsync();

        var code = await RequestAuthorizationCodeAsync();
        using var codeExchangeResponse = await ExchangeAuthorizationCodeAsync(code);
        codeExchangeResponse.EnsureSuccessStatusCode();

        using var codePayload = await codeExchangeResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(codePayload);
        var refreshToken = codePayload.RootElement.GetProperty("refresh_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));

        using var revokeResponse = await _client.PostAsync(
            "/connect/revocation",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = WebClientId,
                ["token"] = refreshToken!,
                ["token_type_hint"] = OpenIddictConstants.TokenTypeHints.RefreshToken,
            }));

        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        using var refreshResponse = await _client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = OpenIddictConstants.GrantTypes.RefreshToken,
                ["client_id"] = WebClientId,
                ["refresh_token"] = refreshToken!,
            }));

        Assert.Equal(HttpStatusCode.BadRequest, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Introspection_WhenClientCredentialsValid_ReturnsActiveAndInactiveStates()
    {
        using var tokenResponse = await _client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = OpenIddictConstants.GrantTypes.ClientCredentials,
                ["client_id"] = MachineClientId,
                ["client_secret"] = MachineClientSecret,
                ["scope"] = PermissionConstants.ScopeApi,
            }));

        tokenResponse.EnsureSuccessStatusCode();

        using var tokenPayload = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(tokenPayload);
        var accessToken = tokenPayload.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        using var activeResponse = await IntrospectAsync(accessToken!);
        activeResponse.EnsureSuccessStatusCode();
        using var activePayload = await ResponseContentToJsonAsync(activeResponse);
        Assert.True(activePayload.RootElement.GetProperty("active").GetBoolean());
        Assert.False(activePayload.RootElement.TryGetProperty(PermissionConstants.ClaimType, out _));

        using var inactiveResponse = await IntrospectAsync("not-a-valid-token");
        inactiveResponse.EnsureSuccessStatusCode();
        using var inactivePayload = await ResponseContentToJsonAsync(inactiveResponse);
        Assert.False(inactivePayload.RootElement.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Introspection_WhenPublicClientAttempts_ReturnsUnauthorized()
    {
        using var tokenResponse = await _client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = OpenIddictConstants.GrantTypes.ClientCredentials,
                ["client_id"] = MachineClientId,
                ["client_secret"] = MachineClientSecret,
                ["scope"] = PermissionConstants.ScopeApi,
            }));

        tokenResponse.EnsureSuccessStatusCode();
        using var tokenPayload = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(tokenPayload);
        var accessToken = tokenPayload.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        using var response = await _client.PostAsync(
            "/connect/introspection",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = WebClientId,
                ["token"] = accessToken!,
            }));

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Introspection_WhenClientUnknown_ReturnsUnauthorized()
    {
        using var response = await _client.PostAsync(
            "/connect/introspection",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "missing-client",
                ["client_secret"] = "missing-secret",
                ["token"] = "not-a-valid-token",
            }));

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task OidcClientPermissionBackfill_WhenLegacyEndpointPermissionsMissing_AddsExpectedPermissions()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var backfillService = scope.ServiceProvider.GetRequiredService<OidcClientPermissionBackfillService>();

        await CreateLegacyApplicationAsync(
            applicationManager,
            "legacy-refresh-client",
            OpenIddictConstants.ClientTypes.Public,
            null,
            [
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Prefixes.GrantType + OpenIddictConstants.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.Prefixes.GrantType + OpenIddictConstants.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OpenId,
            ],
            redirectUri: "https://localhost/legacy-refresh-callback");

        await CreateLegacyApplicationAsync(
            applicationManager,
            "legacy-machine-client",
            OpenIddictConstants.ClientTypes.Confidential,
            "legacy-machine-secret",
            [
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Prefixes.GrantType + OpenIddictConstants.GrantTypes.ClientCredentials,
                OpenIddictConstants.Permissions.Prefixes.Scope + PermissionConstants.ScopeApi,
            ]);

        var legacyRefreshClient = await applicationManager.FindByClientIdAsync("legacy-refresh-client");
        var legacyMachineClient = await applicationManager.FindByClientIdAsync("legacy-machine-client");
        Assert.NotNull(legacyRefreshClient);
        Assert.NotNull(legacyMachineClient);

        var legacyRefreshPermissionsBefore = await applicationManager.GetPermissionsAsync(legacyRefreshClient);
        var legacyMachinePermissionsBefore = await applicationManager.GetPermissionsAsync(legacyMachineClient);
        Assert.DoesNotContain(OpenIddictConstants.Permissions.Endpoints.Revocation, legacyRefreshPermissionsBefore);
        Assert.DoesNotContain(OpenIddictConstants.Permissions.Endpoints.Introspection, legacyMachinePermissionsBefore);

        await backfillService.EnsureBackfilledAsync();

        legacyRefreshClient = await applicationManager.FindByClientIdAsync("legacy-refresh-client");
        legacyMachineClient = await applicationManager.FindByClientIdAsync("legacy-machine-client");
        Assert.NotNull(legacyRefreshClient);
        Assert.NotNull(legacyMachineClient);

        var legacyRefreshPermissionsAfter = await applicationManager.GetPermissionsAsync(legacyRefreshClient);
        var legacyMachinePermissionsAfter = await applicationManager.GetPermissionsAsync(legacyMachineClient);

        Assert.Contains(OpenIddictConstants.Permissions.Endpoints.Revocation, legacyRefreshPermissionsAfter);
        Assert.DoesNotContain(OpenIddictConstants.Permissions.Endpoints.Introspection, legacyRefreshPermissionsAfter);
        Assert.Contains(OpenIddictConstants.Permissions.Endpoints.Introspection, legacyMachinePermissionsAfter);
        Assert.DoesNotContain(OpenIddictConstants.Permissions.Endpoints.Revocation, legacyMachinePermissionsAfter);
    }

    [Fact]
    public async Task UserInfo_WithBearerToken_ReturnsExpectedUserClaims()
    {
        await SignInAsAdminAsync();

        var code = await RequestAuthorizationCodeAsync();
        using var tokenResponse = await ExchangeAuthorizationCodeAsync(code);
        tokenResponse.EnsureSuccessStatusCode();

        using var tokenPayload = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(tokenPayload);
        var accessToken = tokenPayload.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(payload);

        var root = payload.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty(OpenIddictConstants.Claims.Subject).GetString()));
        Assert.Equal("admin", GetFirstJsonStringValue(root, OpenIddictConstants.Claims.Name, ClaimTypes.Name));
        Assert.Equal("admin@bzsoidc.local", GetFirstJsonStringValue(root, OpenIddictConstants.Claims.Email, ClaimTypes.Email));
        Assert.Contains(IdentitySeedConstants.AdminRoleName, GetJsonStringValues(root, OpenIddictConstants.Claims.Role, ClaimTypes.Role));
        Assert.Contains(PermissionConstants.UsersWrite, GetJsonStringValues(root, PermissionConstants.ClaimType));
    }

    [Fact]
    public async Task UserInfo_WithBearerTokenViaPost_ReturnsExpectedUserClaims()
    {
        await SignInAsAdminAsync();

        var code = await RequestAuthorizationCodeAsync();
        using var tokenResponse = await ExchangeAuthorizationCodeAsync(code);
        tokenResponse.EnsureSuccessStatusCode();

        using var tokenPayload = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(tokenPayload);
        var accessToken = tokenPayload.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/userinfo")
        {
            Content = new FormUrlEncodedContent([]),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(payload);

        var root = payload.RootElement;
        Assert.Equal("admin", GetFirstJsonStringValue(root, OpenIddictConstants.Claims.Name, ClaimTypes.Name));
        Assert.Equal("admin@bzsoidc.local", GetFirstJsonStringValue(root, OpenIddictConstants.Claims.Email, ClaimTypes.Email));
        Assert.Contains(IdentitySeedConstants.AdminRoleName, GetJsonStringValues(root, OpenIddictConstants.Claims.Role, ClaimTypes.Role));
    }

    [Fact]
    public async Task UserInfo_WhenUserHasDisplayName_ReturnsDisplayNameClaim()
    {
        await UpdateAdminDisplayNameAsync("Admin Display");
        await SignInAsAdminAsync();

        var code = await RequestAuthorizationCodeAsync();
        using var tokenResponse = await ExchangeAuthorizationCodeAsync(code);
        tokenResponse.EnsureSuccessStatusCode();

        using var tokenPayload = await tokenResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(tokenPayload);
        var accessToken = tokenPayload.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(payload);
        Assert.Equal("Admin Display", GetFirstJsonStringValue(payload.RootElement, OpenIddictConstants.Claims.Name, ClaimTypes.Name));
    }

    [Fact]
    public async Task LogoutEndpoint_WhenInvoked_RedirectsToRoot()
    {
        await SignInAsAdminAsync();

        using var response = await _client.GetAsync("/connect/logout");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task RoleManagement_WithAdminCookie_WorksEndToEnd()
    {
        await SignInAsAdminAsync();

        var createRequest = new RoleUpsertRequest { Name = "operators" };
        using var createResponse = await _client.PostAsJsonAsync("/api/roles", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<RoleResponse>();
        Assert.NotNull(created);
        Assert.Equal("operators", created.Name);

        using var updateResponse = await _client.PutAsJsonAsync($"/api/roles/{created.Id}", new RoleUpsertRequest { Name = "support" });
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<RoleResponse>();
        Assert.NotNull(updated);
        Assert.Equal("support", updated.Name);

        using var syncResponse = await _client.PutAsJsonAsync($"/api/roles/{created.Id}/permissions", new RolePermissionSyncRequest
        {
            Permissions = [PermissionConstants.UsersReadAll],
        });
        Assert.Equal(HttpStatusCode.NoContent, syncResponse.StatusCode);

        using var permissionsResponse = await _client.GetAsync($"/api/roles/{created.Id}/permissions");
        permissionsResponse.EnsureSuccessStatusCode();
        var permissions = await permissionsResponse.Content.ReadFromJsonAsync<string[]>();
        Assert.NotNull(permissions);
        Assert.Contains(PermissionConstants.UsersReadAll, permissions);

        using var shimResponse = await _client.GetAsync($"/api/permission-catalog/roles/{created.Id}/permissions");
        shimResponse.EnsureSuccessStatusCode();
        var shimPermissions = await shimResponse.Content.ReadFromJsonAsync<string[]>();
        Assert.NotNull(shimPermissions);
        Assert.Contains(PermissionConstants.UsersReadAll, shimPermissions);

        using var deleteResponse = await _client.DeleteAsync($"/api/roles/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task RoleManagement_WhenPermissionInvalid_ReturnsBadRequestWithoutMutatingClaims()
    {
        await SignInAsAdminAsync();

        using var createResponse = await _client.PostAsJsonAsync("/api/roles", new RoleUpsertRequest { Name = "auditors" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<RoleResponse>();
        Assert.NotNull(created);

        using var invalidResponse = await _client.PutAsJsonAsync($"/api/roles/{created.Id}/permissions", new RolePermissionSyncRequest
        {
            Permissions = [PermissionConstants.UsersReadAll, "missing.permission"],
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        using var permissionsResponse = await _client.GetAsync($"/api/roles/{created.Id}/permissions");
        permissionsResponse.EnsureSuccessStatusCode();
        var permissions = await permissionsResponse.Content.ReadFromJsonAsync<string[]>();
        Assert.NotNull(permissions);
        Assert.Empty(permissions);
    }
    [Fact]
    public async Task AdminDashboardSummary_WithAdminCookie_ReturnsExpectedCounts()
    {
        await SignInAsAdminAsync();

        using var response = await _client.GetAsync("/api/admin/dashboard/summary");
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<AdminDashboardSummaryResponse>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload.TotalClients);
        Assert.Equal(1, payload.InteractiveClients);
        Assert.Equal(1, payload.MachineClients);
        Assert.True(payload.TotalUsers >= 1);
        Assert.True(payload.AdminUsers >= 1);
        Assert.Equal(payload.TotalUsers, payload.AdminUsers + payload.StandardUsers);
        Assert.True(payload.TotalPermissionMappings >= 7);
        Assert.True(payload.TotalConfiguredScopes >= payload.TotalPermissionMappings);
        Assert.Equal(payload.TotalClients, payload.InteractiveClients + payload.MachineClients);
    }

    [Fact]
    public async Task AuthorizationCodeFlow_WhenScopesGranted_ProjectsExpectedClaimsIntoTokens()
    {
        await SignInAsAdminAsync();

        var code = await RequestAuthorizationCodeAsync([
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Roles,
            OpenIddictConstants.Scopes.OfflineAccess,
            PermissionConstants.ScopeApi,
        ]);

        using var tokenResponse = await ExchangeAuthorizationCodeAsync(code);
        tokenResponse.EnsureSuccessStatusCode();

        using var tokenPayload = await ResponseContentToJsonAsync(tokenResponse);
        var accessToken = tokenPayload.RootElement.GetProperty("access_token").GetString();
        var idToken = tokenPayload.RootElement.GetProperty("id_token").GetString();

        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(idToken));

        using var accessPayload = ReadJwtPayload(accessToken!);
        using var idPayload = ReadJwtPayload(idToken!);

        var accessSubject = GetFirstJsonStringValue(accessPayload.RootElement,
            OpenIddictConstants.Claims.Subject,
            ClaimTypes.NameIdentifier);
        var idSubject = GetFirstJsonStringValue(idPayload.RootElement,
            OpenIddictConstants.Claims.Subject,
            ClaimTypes.NameIdentifier);

        Assert.False(string.IsNullOrWhiteSpace(accessSubject));
        Assert.Equal(idSubject, accessSubject);
        Assert.Null(GetFirstJsonStringValue(accessPayload.RootElement, OpenIddictConstants.Claims.Name, ClaimTypes.Name));
        Assert.Null(GetFirstJsonStringValue(accessPayload.RootElement, OpenIddictConstants.Claims.Email, ClaimTypes.Email));
        Assert.Contains(IdentitySeedConstants.AdminRoleName,
            GetJsonStringValues(accessPayload.RootElement, OpenIddictConstants.Claims.Role, ClaimTypes.Role));
        Assert.Contains(PermissionConstants.UsersWrite,
            GetJsonStringValues(accessPayload.RootElement, PermissionConstants.ClaimType));

        Assert.Equal("admin", GetFirstJsonStringValue(idPayload.RootElement, OpenIddictConstants.Claims.Name, ClaimTypes.Name));
        Assert.Equal("admin@bzsoidc.local",
            GetFirstJsonStringValue(idPayload.RootElement, OpenIddictConstants.Claims.Email, ClaimTypes.Email));
        Assert.Contains(IdentitySeedConstants.AdminRoleName,
            GetJsonStringValues(idPayload.RootElement, OpenIddictConstants.Claims.Role, ClaimTypes.Role));
        Assert.False(idPayload.RootElement.TryGetProperty(PermissionConstants.ClaimType, out _));
    }

    [Fact]
    public async Task AuthorizationCodeFlow_WhenOnlyOpenIdScopeGranted_DoesNotProjectRoleOrPermissionClaims()
    {
        await SignInAsAdminAsync();

        var code = await RequestAuthorizationCodeAsync([
            OpenIddictConstants.Scopes.OpenId,
        ]);

        using var tokenResponse = await ExchangeAuthorizationCodeAsync(code);
        tokenResponse.EnsureSuccessStatusCode();

        using var tokenPayload = await ResponseContentToJsonAsync(tokenResponse);
        var accessToken = tokenPayload.RootElement.GetProperty("access_token").GetString();
        var idToken = tokenPayload.RootElement.GetProperty("id_token").GetString();

        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(idToken));

        using var accessPayload = ReadJwtPayload(accessToken!);
        using var idPayload = ReadJwtPayload(idToken!);

        Assert.Empty(GetJsonStringValues(accessPayload.RootElement, OpenIddictConstants.Claims.Role, ClaimTypes.Role));
        Assert.False(accessPayload.RootElement.TryGetProperty(PermissionConstants.ClaimType, out _));
        Assert.Null(GetFirstJsonStringValue(idPayload.RootElement, OpenIddictConstants.Claims.Name, ClaimTypes.Name));
        Assert.Null(GetFirstJsonStringValue(idPayload.RootElement, OpenIddictConstants.Claims.Email, ClaimTypes.Email));
        Assert.Empty(GetJsonStringValues(idPayload.RootElement, OpenIddictConstants.Claims.Role, ClaimTypes.Role));
        Assert.False(idPayload.RootElement.TryGetProperty(PermissionConstants.ClaimType, out _));
    }

    [Fact]
    public async Task ClientRegistration_WithAdminCookie_CreatesAndReadsBackClient()
    {
        await SignInAsAdminAsync();

        var request = new OidcClientUpsertRequest
        {
            ClientId = "interactive-client",
            DisplayName = "Interactive Client",
            PublicClient = true,
            GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode, OpenIddictConstants.GrantTypes.RefreshToken],
            Scopes = [OpenIddictConstants.Scopes.OpenId, OpenIddictConstants.Scopes.Profile, PermissionConstants.ScopeApi],
            RedirectUris = ["https://localhost/interactive/callback"],
            PostLogoutRedirectUris = ["https://localhost/interactive/logout-callback"],
        };

        using var createResponse = await _client.PostAsJsonAsync("/api/oidc/clients", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<OidcClientRegistrationResponse>();
        Assert.NotNull(created);
        Assert.Equal("interactive-client", created.ClientId);
        Assert.Equal(OidcClientAuthFlow.AuthorizationCode, created.AuthFlow);

        using var getResponse = await _client.GetAsync($"/api/oidc/clients/{created.ClientId}");
        getResponse.EnsureSuccessStatusCode();

        var client = await getResponse.Content.ReadFromJsonAsync<OidcClientResponse>();
        Assert.NotNull(client);
        Assert.Equal("interactive-client", client.ClientId);
        Assert.Equal("Interactive Client", client.DisplayName);
        Assert.Equal(OidcClientAuthFlow.AuthorizationCode, client.AuthFlow);
        Assert.Equal(OidcClientConsentType.Implicit, client.ConsentType);
        Assert.Contains(OpenIddictConstants.GrantTypes.AuthorizationCode, client.GrantTypes);
        Assert.Contains(PermissionConstants.ScopeApi, client.Scopes);
    }

    [Fact]
    public async Task ClientRegistration_WhenExplicitConsentRequested_ReadsBackConsentType()
    {
        await SignInAsAdminAsync();

        var request = new OidcClientUpsertRequest
        {
            ClientId = "explicit-managed-client",
            DisplayName = "Explicit Managed Client",
            AuthFlow = OidcClientAuthFlow.AuthorizationCode,
            PublicClient = true,
            RequireProofKeyForCodeExchange = true,
            ConsentType = OidcClientConsentType.Explicit,
            GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode, OpenIddictConstants.GrantTypes.RefreshToken],
            Scopes = [OpenIddictConstants.Scopes.OpenId, PermissionConstants.ScopeApi],
            RedirectUris = ["https://localhost/explicit-managed/callback"],
        };

        using var createResponse = await _client.PostAsJsonAsync("/api/oidc/clients", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var getResponse = await _client.GetAsync("/api/oidc/clients/explicit-managed-client");
        getResponse.EnsureSuccessStatusCode();

        var client = await getResponse.Content.ReadFromJsonAsync<OidcClientResponse>();
        Assert.NotNull(client);
        Assert.Equal(OidcClientConsentType.Explicit, client.ConsentType);
    }

    [Fact]
    public async Task AuthorizationCodeFlow_WhenClientRequiresExplicitConsent_ApprovalCreatesAuthorizationAndDenialReturnsAccessDenied()
    {
        await SignInAsAdminAsync();
        await EnsureExplicitConsentClientAsync("explicit-consent-client", "https://localhost/explicit-callback");

        using var consentPageResponse = await RequestAuthorizationResponseAsync(
            "explicit-consent-client",
            "https://localhost/explicit-callback");

        consentPageResponse.EnsureSuccessStatusCode();
        var consentPage = await consentPageResponse.Content.ReadAsStringAsync();
        Assert.Contains("Allow explicit-consent-client", consentPage, StringComparison.OrdinalIgnoreCase);

        var token = ExtractAntiforgeryToken(consentPage);
        var consentCookies = GetCookieHeader(consentPageResponse);

        using var approveResponse = await PostConsentAsync(
            "explicit-consent-client",
            "https://localhost/explicit-callback",
            "accept",
            token,
            consentCookies);

        Assert.Equal(HttpStatusCode.Found, approveResponse.StatusCode);
        Assert.NotNull(approveResponse.Headers.Location);
        var approveQuery = QueryHelpers.ParseQuery(approveResponse.Headers.Location.Query);
        Assert.True(approveQuery.TryGetValue("code", out var codeValues));
        Assert.False(string.IsNullOrWhiteSpace(codeValues.ToString()));

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var authorizationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            var authorizations = authorizationManager.FindBySubjectAsync(
                (await GetAdminUserIdAsync())!);
            var foundAuthorization = false;
            await foreach (var _ in authorizations)
            {
                foundAuthorization = true;
                break;
            }

            Assert.True(foundAuthorization);
        }

        await EnsureExplicitConsentClientAsync("explicit-deny-client", "https://localhost/explicit-deny-callback");
        using var denyPageResponse = await RequestAuthorizationResponseAsync(
            "explicit-deny-client",
            "https://localhost/explicit-deny-callback");
        denyPageResponse.EnsureSuccessStatusCode();
        var denyPage = await denyPageResponse.Content.ReadAsStringAsync();
        var denyToken = ExtractAntiforgeryToken(denyPage);
        var denyCookies = GetCookieHeader(denyPageResponse);

        using var denyResponse = await PostConsentAsync(
            "explicit-deny-client",
            "https://localhost/explicit-deny-callback",
            "deny",
            denyToken,
            denyCookies);

        Assert.Equal(HttpStatusCode.Found, denyResponse.StatusCode);
        Assert.NotNull(denyResponse.Headers.Location);
        var denyQuery = QueryHelpers.ParseQuery(denyResponse.Headers.Location.Query);
        Assert.False(denyQuery.ContainsKey("code"));
        Assert.Equal(OpenIddictConstants.Errors.AccessDenied, denyQuery["error"].ToString());
    }

    [Fact]
    public async Task AuthorizationCodeFlow_WhenPromptConsentRequested_IgnoresExistingAuthorizationAndShowsConsentPage()
    {
        await SignInAsAdminAsync();
        await EnsureExplicitConsentClientAsync("prompt-consent-client", "https://localhost/prompt-consent-callback");

        using var consentPageResponse = await RequestAuthorizationResponseAsync(
            "prompt-consent-client",
            "https://localhost/prompt-consent-callback");

        consentPageResponse.EnsureSuccessStatusCode();
        var consentPage = await consentPageResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(consentPage);
        var consentCookies = GetCookieHeader(consentPageResponse);

        using var approveResponse = await PostConsentAsync(
            "prompt-consent-client",
            "https://localhost/prompt-consent-callback",
            "accept",
            token,
            consentCookies);

        Assert.Equal(HttpStatusCode.Found, approveResponse.StatusCode);

        using var promptConsentResponse = await RequestAuthorizationResponseAsync(
            "prompt-consent-client",
            "https://localhost/prompt-consent-callback",
            KeyValuePair.Create("prompt", "consent"));

        promptConsentResponse.EnsureSuccessStatusCode();
        var promptConsentPage = await promptConsentResponse.Content.ReadAsStringAsync();
        Assert.Contains("Allow prompt-consent-client", promptConsentPage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthorizationCodeFlow_WhenPromptNoneRequestedWithoutExistingAuthorization_ReturnsConsentRequired()
    {
        await SignInAsAdminAsync();
        await EnsureExplicitConsentClientAsync("prompt-none-client", "https://localhost/prompt-none-callback");

        using var response = await RequestAuthorizationResponseAsync(
            "prompt-none-client",
            "https://localhost/prompt-none-callback",
            KeyValuePair.Create("prompt", "none"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var query = QueryHelpers.ParseQuery(response.Headers.Location.Query);
        Assert.Equal(OpenIddictConstants.Errors.ConsentRequired, query["error"].ToString());
        Assert.False(query.ContainsKey("code"));
    }

    [Fact]
    public async Task ClientRegistration_WhenMachineProfileRequested_CreatesConfidentialMachineClient()
    {
        await SignInAsAdminAsync();

        var request = new OidcClientUpsertRequest
        {
            ClientId = "machine-client-managed",
            DisplayName = "Managed Machine Client",
            AuthFlow = OidcClientAuthFlow.ClientCredentials,
            PublicClient = false,
            GrantTypes = [OpenIddictConstants.GrantTypes.ClientCredentials],
            Scopes = [PermissionConstants.ScopeApi],
        };

        using var createResponse = await _client.PostAsJsonAsync("/api/oidc/clients", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<OidcClientRegistrationResponse>();
        Assert.NotNull(created);
        Assert.Equal(OidcClientAuthFlow.ClientCredentials, created.AuthFlow);
        Assert.False(string.IsNullOrWhiteSpace(created.ClientSecret));

        using var getResponse = await _client.GetAsync($"/api/oidc/clients/{created.ClientId}");
        getResponse.EnsureSuccessStatusCode();

        var client = await getResponse.Content.ReadFromJsonAsync<OidcClientResponse>();
        Assert.NotNull(client);
        Assert.Equal(OidcClientAuthFlow.ClientCredentials, client.AuthFlow);
        Assert.False(client.PublicClient);
        Assert.Equal([OpenIddictConstants.GrantTypes.ClientCredentials], client.GrantTypes);
    }

    [Fact]
    public async Task ClientRegistration_WhenClientExists_ReturnsConflict()
    {
        await SignInAsAdminAsync();

        var request = new OidcClientUpsertRequest
        {
            ClientId = "interactive-client-conflict",
            DisplayName = "Interactive Client",
            PublicClient = true,
            GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode],
            Scopes = [OpenIddictConstants.Scopes.OpenId],
            RedirectUris = ["https://localhost/interactive/conflict-callback"],
        };

        using var firstResponse = await _client.PostAsJsonAsync("/api/oidc/clients", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var secondResponse = await _client.PostAsJsonAsync("/api/oidc/clients", request);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);

        var error = await secondResponse.Content.ReadAsStringAsync();
        Assert.Contains("already exists", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClientRegistration_WhenRequestInvalid_ReturnsValidationProblem()
    {
        await SignInAsAdminAsync();

        var request = new OidcClientUpsertRequest
        {
            DisplayName = string.Empty,
            PublicClient = true,
            GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode],
            Scopes = [OpenIddictConstants.Scopes.OpenId],
        };

        using var response = await _client.PostAsJsonAsync("/api/oidc/clients", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(payload);
        Assert.Contains(nameof(OidcClientUpsertRequest), payload.Errors.Keys);
    }

    [Fact]
    public async Task ClientRegistration_WhenUnsupportedProfileRequested_ReturnsValidationProblem()
    {
        await SignInAsAdminAsync();

        var request = new OidcClientUpsertRequest
        {
            DisplayName = "Unsupported Client",
            PublicClient = false,
            GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode],
            RedirectUris = ["https://localhost/callback"],
        };

        using var response = await _client.PostAsJsonAsync("/api/oidc/clients", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(payload);
        Assert.Contains(nameof(OidcClientUpsertRequest), payload.Errors.Keys);
        Assert.Contains(payload.Errors[nameof(OidcClientUpsertRequest)],
            static error => error.Contains("Authorization Code Flow", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ClientUpdate_WhenClientMissing_ReturnsNotFound()
    {
        await SignInAsAdminAsync();

        var request = new OidcClientUpsertRequest
        {
            DisplayName = "Updated Client",
            PublicClient = true,
            GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode],
            Scopes = [OpenIddictConstants.Scopes.OpenId],
            RedirectUris = ["https://localhost/interactive/updated-callback"],
        };

        using var response = await _client.PutAsJsonAsync("/api/oidc/clients/missing-client", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ClientUpdate_WhenClientExists_ReturnsUpdatedClient()
    {
        await SignInAsAdminAsync();

        var createRequest = new OidcClientUpsertRequest
        {
            ClientId = "interactive-client-update",
            DisplayName = "Interactive Client",
            AuthFlow = OidcClientAuthFlow.AuthorizationCode,
            PublicClient = true,
            GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode, OpenIddictConstants.GrantTypes.RefreshToken],
            Scopes = [OpenIddictConstants.Scopes.OpenId, PermissionConstants.ScopeApi],
            RedirectUris = ["https://localhost/interactive/update-callback"],
        };

        using var createResponse = await _client.PostAsJsonAsync("/api/oidc/clients", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var updateRequest = new OidcClientUpsertRequest
        {
            ClientId = "interactive-client-update",
            DisplayName = "Updated Interactive Client",
            AuthFlow = OidcClientAuthFlow.AuthorizationCode,
            PublicClient = true,
            GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode],
            Scopes = [OpenIddictConstants.Scopes.OpenId],
            RedirectUris = ["https://localhost/interactive/updated-callback"],
        };

        using var updateResponse = await _client.PutAsJsonAsync("/api/oidc/clients/interactive-client-update", updateRequest);
        updateResponse.EnsureSuccessStatusCode();

        var updated = await updateResponse.Content.ReadFromJsonAsync<OidcClientResponse>();
        Assert.NotNull(updated);
        Assert.Equal("interactive-client-update", updated.ClientId);
        Assert.Equal("Updated Interactive Client", updated.DisplayName);
        Assert.Equal(OidcClientAuthFlow.AuthorizationCode, updated.AuthFlow);
        Assert.Equal([OpenIddictConstants.GrantTypes.AuthorizationCode], updated.GrantTypes);
        Assert.Equal(["https://localhost/interactive/updated-callback"], updated.RedirectUris);
    }

    [Fact]
    public async Task ClientDelete_WhenClientMissing_ReturnsNotFound()
    {
        await SignInAsAdminAsync();

        using var response = await _client.DeleteAsync("/api/oidc/clients/missing-client");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ClientDelete_WhenClientExists_ReturnsNoContentAndRemovesClient()
    {
        await SignInAsAdminAsync();

        var request = new OidcClientUpsertRequest
        {
            ClientId = "interactive-client-delete",
            DisplayName = "Interactive Client",
            AuthFlow = OidcClientAuthFlow.AuthorizationCode,
            PublicClient = true,
            GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode],
            Scopes = [OpenIddictConstants.Scopes.OpenId],
            RedirectUris = ["https://localhost/interactive/delete-callback"],
        };

        using var createResponse = await _client.PostAsJsonAsync("/api/oidc/clients", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var deleteResponse = await _client.DeleteAsync("/api/oidc/clients/interactive-client-delete");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var getResponse = await _client.GetAsync("/api/oidc/clients/interactive-client-delete");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
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
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IdpIssuer"] = BaseUri.ToString().TrimEnd('/'),
            ["Identity:Admin:UserName"] = "admin",
            ["Identity:Admin:Password"] = "admin123",
            ["PermissionPolicy:PolicyPrefix"] = PermissionPolicyOptions.DefaultPolicyPrefix,
            ["Authentication:GitHub:ClientId"] = "gho_test_valid_client_id",
            ["Authentication:GitHub:ClientSecret"] = "ghs_test_valid_client_secret",
            ["Authentication:GitHub:CallbackPath"] = "/signin-github",
        });

        builder.Services.AddMemoryCache();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddBootstrapBlazor();
        builder.Services.AddLocalization(options => { options.ResourcesPath = "Resources"; });
        builder.Services.AddForwardedHeaders();
        builder.Services.AddExternalAuthenticationServices(builder.Configuration);
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents()
            .AddInteractiveWebAssemblyComponents();
        builder.Services.AddDbContext<IdpDbContext>(
            options => ConfigureTestDatabase(options, _connection),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);
        builder.Services.AddDbContextFactory<IdpDbContext>(options => ConfigureTestDatabase(options, _connection));

        var registrar = new IdpServiceRegistrar(builder.Services, builder.Configuration, builder.Environment);
        registrar.AddIdpOptions();
        registrar.AddDataProtection();
        registrar.AddOidc();

        builder.Services.AddScoped<IRoleService, RoleService>();
        builder.Services.AddScoped<IUserService, UserService>();
        builder.Services.AddScoped<IRolePermissionService, RolePermissionService>();
        builder.Services.AddScoped<IPermissionCatalogService, PermissionCatalogService>();
        builder.Services.AddScoped<RoleManagementPolicy>();
        builder.Services.AddScoped<IRoleManagementService, RoleManagementService>();
        builder.Services.AddScoped<IOidcPrincipalFactory, OidcPrincipalFactory>();
        builder.Services.AddScoped<IOidcClientService, OidcClientService>();
        builder.Services.AddScoped<IOidcScopeService, OidcScopeService>();
        builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        builder.Services.AddScoped<IdentitySeeder>();
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(ConnectController).Assembly);

        _app = builder.Build();
        _app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        _app.UseForwardedHeaders();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseAntiforgery();
        _app.MapControllers();
        _app.MapRazorComponents<global::BzsOIDC.Idp.Components.App>()
            .AddInteractiveServerRenderMode()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(IAdminDashboardClient).Assembly);

        await _app.StartAsync();

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IdpDbContext>();
            await dbContext.Database.EnsureCreatedAsync();

            var identitySeeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
            await identitySeeder.SeedAsync();

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<BzsUser>>();
            var admin = await userManager.FindByNameAsync("admin");
            Assert.NotNull(admin);
            admin.Email = "admin@bzsoidc.local";
            var updateResult = await userManager.UpdateAsync(admin);
            Assert.True(updateResult.Succeeded, string.Join(", ", updateResult.Errors.Select(static e => e.Description)));

            var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            await EnsureApplicationAsync(
                applicationManager,
                WebClientId,
                new OidcClientUpsertRequest
                {
                    DisplayName = "Web Client",
                    PublicClient = true,
                    GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode, OpenIddictConstants.GrantTypes.RefreshToken],
                    Scopes = [
                        OpenIddictConstants.Scopes.OpenId,
                        OpenIddictConstants.Scopes.Profile,
                        OpenIddictConstants.Scopes.Email,
                        OpenIddictConstants.Scopes.Roles,
                        OpenIddictConstants.Scopes.OfflineAccess,
                        PermissionConstants.ScopeApi,
                    ],
                    RedirectUris = [WebRedirectUri.ToString()],
                });
            await EnsureApplicationAsync(
                applicationManager,
                MachineClientId,
                new OidcClientUpsertRequest
                {
                    DisplayName = "Machine Client",
                    PublicClient = false,
                    ClientSecret = MachineClientSecret,
                    GrantTypes = [OpenIddictConstants.GrantTypes.ClientCredentials],
                    Scopes = [PermissionConstants.ScopeApi],
                });
        }

        _client = _app.GetTestClient();
        _client.BaseAddress = BaseUri;
        _client.DefaultRequestHeaders.ExpectContinue = false;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static void ConfigureTestDatabase(DbContextOptionsBuilder options, SqliteConnection connection)
    {
        options.UseSqlite(connection);
        options.UseOpenIddict();
    }

    private static async Task EnsureApplicationAsync(
        IOpenIddictApplicationManager applicationManager,
        string clientId,
        OidcClientUpsertRequest request)
    {
        var existingApplication = await applicationManager.FindByClientIdAsync(clientId);
        if (existingApplication is not null)
        {
            return;
        }

        var descriptor = OidcClientDescriptorFactory.CreateDescriptor(request, clientId);
        await applicationManager.CreateAsync(descriptor);
    }

    private static async Task CreateLegacyApplicationAsync(
        IOpenIddictApplicationManager applicationManager,
        string clientId,
        string clientType,
        string? clientSecret,
        IEnumerable<string> permissions,
        string? redirectUri = null)
    {
        var existingApplication = await applicationManager.FindByClientIdAsync(clientId);
        if (existingApplication is not null)
        {
            await applicationManager.DeleteAsync(existingApplication);
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ClientType = clientType,
            ConsentType = string.Equals(clientType, OpenIddictConstants.ClientTypes.Confidential, StringComparison.OrdinalIgnoreCase)
                ? OpenIddictConstants.ConsentTypes.External
                : OpenIddictConstants.ConsentTypes.Implicit,
            DisplayName = clientId,
        };

        descriptor.Permissions.UnionWith(permissions);

        if (!string.IsNullOrWhiteSpace(redirectUri))
        {
            descriptor.RedirectUris.Add(new Uri(redirectUri));
            descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
        }

        await applicationManager.CreateAsync(descriptor);
    }

    private async Task SignInAsAdminAsync()
    {
        using var response = await _client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["UserName"] = "admin",
                ["Password"] = "admin123",
                ["RememberMe"] = bool.TrueString,
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var cookieHeaders = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(static value => value.Split(';', 2)[0]).ToArray()
            : [];

        Assert.NotEmpty(cookieHeaders);
        _authCookieHeader = string.Join("; ", cookieHeaders);
        _client.DefaultRequestHeaders.Remove("Cookie");
        _client.DefaultRequestHeaders.Add("Cookie", _authCookieHeader);
    }

    private async Task UpdateAdminDisplayNameAsync(string displayName)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<BzsUser>>();
        var admin = await userManager.FindByNameAsync("admin");
        Assert.NotNull(admin);

        admin.UpdateDisplayName(displayName);

        var updateResult = await userManager.UpdateAsync(admin);
        Assert.True(updateResult.Succeeded, string.Join(", ", updateResult.Errors.Select(static error => error.Description)));
    }

    private async Task<string?> GetAdminUserIdAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<BzsUser>>();
        var admin = await userManager.FindByNameAsync("admin");
        return admin is null ? null : await userManager.GetUserIdAsync(admin);
    }

    private async Task EnsureExplicitConsentClientAsync(string clientId, string redirectUri)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        await EnsureApplicationAsync(
            applicationManager,
            clientId,
            new OidcClientUpsertRequest
            {
                DisplayName = clientId,
                AuthFlow = OidcClientAuthFlow.AuthorizationCode,
                PublicClient = true,
                RequireProofKeyForCodeExchange = true,
                ConsentType = OidcClientConsentType.Explicit,
                GrantTypes = [OpenIddictConstants.GrantTypes.AuthorizationCode, OpenIddictConstants.GrantTypes.RefreshToken],
                Scopes = [
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    PermissionConstants.ScopeApi,
                ],
                RedirectUris = [redirectUri],
            });
    }

    private Task<HttpResponseMessage> IntrospectAsync(string token)
    {
        return _client.PostAsync(
            "/connect/introspection",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = MachineClientId,
                ["client_secret"] = MachineClientSecret,
                ["token"] = token,
            }));
    }

    private async Task<string> RequestAuthorizationCodeAsync(IEnumerable<string>? scopes = null)
    {
        var requestedScopes = scopes?.ToArray() ??
            [
                OpenIddictConstants.Scopes.OpenId,
                OpenIddictConstants.Scopes.Profile,
                OpenIddictConstants.Scopes.Email,
                OpenIddictConstants.Scopes.Roles,
                OpenIddictConstants.Scopes.OfflineAccess,
                PermissionConstants.ScopeApi,
            ];

        var authorizeUrl = QueryHelpers.AddQueryString(
            "/connect/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = WebClientId,
                ["response_type"] = OpenIddictConstants.ResponseTypes.Code,
                ["redirect_uri"] = WebRedirectUri.ToString(),
                ["scope"] = string.Join(' ', requestedScopes),
                ["state"] = "test-state",
                ["code_challenge"] = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
                ["code_challenge_method"] = "S256",
            });

        using var request = new HttpRequestMessage(HttpMethod.Get, authorizeUrl);
        request.Headers.Accept.ParseAdd("text/html");

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (response.StatusCode != HttpStatusCode.Found)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException(
                $"Expected 302 redirect from /connect/authorize but got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Equal(WebRedirectUri.GetLeftPart(UriPartial.Path), location.GetLeftPart(UriPartial.Path));

        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.True(query.TryGetValue("code", out var codeValues));
        var code = codeValues.ToString();
        Assert.False(string.IsNullOrWhiteSpace(code));

        return code;
    }

    private async Task<HttpResponseMessage> RequestAuthorizationResponseAsync(
        string clientId,
        string redirectUri,
        params KeyValuePair<string, string>[] extraQueryParameters)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["response_type"] = OpenIddictConstants.ResponseTypes.Code,
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.Join(' ', [
                OpenIddictConstants.Scopes.OpenId,
                OpenIddictConstants.Scopes.Profile,
                PermissionConstants.ScopeApi,
            ]),
            ["state"] = "test-state",
            ["code_challenge"] = "E9Melhoa2OwvFrEMTJguCHaoe1t8URWbuGJSstw-cM",
            ["code_challenge_method"] = "S256",
        };

        foreach (var parameter in extraQueryParameters)
        {
            query[parameter.Key] = parameter.Value;
        }

        var authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", query);

        var request = new HttpRequestMessage(HttpMethod.Get, authorizeUrl);
        request.Headers.Accept.ParseAdd("text/html");

        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private Task<HttpResponseMessage> PostConsentAsync(
        string clientId,
        string redirectUri,
        string consent,
        string antiforgeryToken,
        string consentCookies)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/connect/authorize")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["response_type"] = OpenIddictConstants.ResponseTypes.Code,
                ["redirect_uri"] = redirectUri,
                ["scope"] = string.Join(' ', [
                    OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Scopes.Profile,
                    PermissionConstants.ScopeApi,
                ]),
                ["state"] = "test-state",
                ["code_challenge"] = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
                ["code_challenge_method"] = "S256",
                ["__RequestVerificationToken"] = antiforgeryToken,
                ["consent"] = consent,
            }),
        };

        request.Headers.Add("Cookie", string.Join("; ", new[] { _authCookieHeader, consentCookies }
            .Where(static cookie => !string.IsNullOrWhiteSpace(cookie))));

        return _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private Task<HttpResponseMessage> ExchangeAuthorizationCodeAsync(string code)
    {
        return _client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = OpenIddictConstants.GrantTypes.AuthorizationCode,
                ["client_id"] = WebClientId,
                ["code"] = code,
                ["redirect_uri"] = WebRedirectUri.ToString(),
                ["code_verifier"] = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk",
            }));
    }

    private static async Task<JsonDocument> ResponseContentToJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(payload);
        return payload;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"",
            RegexOptions.IgnoreCase);

        Assert.True(match.Success, "Consent page should contain an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    private static string GetCookieHeader(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return string.Empty;
        }

        return string.Join("; ", values.Select(static value => value.Split(';', 2)[0]));
    }

    private static JsonDocument ReadJwtPayload(string token)
    {
        var segments = token.Split('.');
        Assert.True(segments.Length >= 2, "JWT token must contain at least header and payload segments.");

        var payloadBytes = WebEncoders.Base64UrlDecode(segments[1]);
        return JsonDocument.Parse(payloadBytes);
    }

    private static string? GetFirstJsonStringValue(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }

            var first = property.ValueKind == JsonValueKind.Array
                ? property.EnumerateArray().Select(static item => item.GetString()).OfType<string>().FirstOrDefault()
                : null;

            if (!string.IsNullOrWhiteSpace(first))
            {
                return first;
            }
        }

        return null;
    }

    private static string[] GetJsonStringValues(JsonElement root, params string[] propertyNames)
    {
        var values = new List<string>();

        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            switch (property.ValueKind)
            {
                case JsonValueKind.Array:
                    values.AddRange(property.EnumerateArray()
                        .Select(static item => item.GetString())
                        .OfType<string>());
                    break;
                case JsonValueKind.String:
                    if (!string.IsNullOrWhiteSpace(property.GetString()))
                    {
                        values.Add(property.GetString()!);
                    }

                    break;
            }
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
