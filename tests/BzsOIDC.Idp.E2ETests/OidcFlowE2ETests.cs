using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using BzsOIDC.Idp.E2ETests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Playwright;

namespace BzsOIDC.Idp.E2ETests;

[Collection(E2ETestCollection.Name)]
public sealed class OidcFlowE2ETests(AppHostFixture fixture) : E2EPageTest
{
    private const string CodeChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private const string CodeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task AuthorizationCodeFlow_CanIssueTokensAndUserInfoForInteractiveClient()
    {
        var clientId = $"oidc-ui-{Guid.NewGuid():N}"[..16];
        const string redirectPath = "/oidc-callback";
        var redirectUri = new Uri(fixture.IdpBaseUri, redirectPath).ToString();

        await AppUi.LoginAsAdminAsync(this, fixture, "/admin/clients");
        await Page.GotoAsync(fixture.BuildUrl("/admin/clients"));
        await AppUi.WaitForAppReadyAsync(this);

        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("新建客户端|New OIDC client|Create", RegexOptions.IgnoreCase) }).ClickAsync();
        await Page.GetByLabel("Client ID").FillAsync(clientId);
        await Page.GetByLabel("Display name").FillAsync("OIDC E2E Client");
        await Page.GetByLabel("Client type").SelectOptionAsync("true");
        await Page.GetByLabel("Authentication flow").SelectOptionAsync("AuthorizationCode");
        await Page.GetByLabel("Scopes").FillAsync("openid, profile, email, roles, offline_access, api");
        await Page.GetByLabel("Redirect URIs", new() { Exact = true }).FillAsync(redirectUri);
        await Page.GetByLabel("Post-logout redirect URIs").FillAsync(redirectUri);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        var createdRow = Page.GetByRole(AriaRole.Row).Filter(new() { HasTextString = clientId });
        await Expect(createdRow).ToBeVisibleAsync(new() { Timeout = 20000 });

        var authorizeUrl = QueryHelpers.AddQueryString(
            fixture.BuildUrl("/connect/authorize"),
            new Dictionary<string, string?>
            {
                ["client_id"] = clientId,
                ["response_type"] = "code",
                ["scope"] = "openid profile email roles offline_access api",
                ["redirect_uri"] = redirectUri,
                ["code_challenge"] = CodeChallenge,
                ["code_challenge_method"] = "S256",
            });

        await Page.GotoAsync(authorizeUrl);
        if (Page.Url.Contains("/consent", StringComparison.OrdinalIgnoreCase))
        {
            await AppUi.WaitForAppReadyAsync(this);
            await Expect(Page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("Allow .* access", RegexOptions.IgnoreCase) })).ToBeVisibleAsync(new() { Timeout = 30000 });
            await Page.GetByRole(AriaRole.Button, new() { Name = "Allow access" }).ClickAsync();
        }
        await Expect(Page).ToHaveURLAsync(
            new Regex($"{Regex.Escape(redirectUri)}.*code=", RegexOptions.IgnoreCase),
            new() { Timeout = 30000 });

        var code = QueryHelpers.ParseQuery(new Uri(Page.Url).Query)["code"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(code));

        using var tokenResponse = await fixture.IdpClient.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code"] = code!,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = CodeVerifier,
            }));

        tokenResponse.EnsureSuccessStatusCode();
        using var tokenPayload = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync());
        var accessToken = tokenPayload.RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var userInfoResponse = await fixture.IdpClient.SendAsync(request);
        userInfoResponse.EnsureSuccessStatusCode();

        using var userInfoPayload = await JsonDocument.ParseAsync(await userInfoResponse.Content.ReadAsStreamAsync());
        var name = userInfoPayload.RootElement.GetProperty("name").GetString();
        Assert.Equal(TestCredentials.AdminUserName, name);
    }
}
