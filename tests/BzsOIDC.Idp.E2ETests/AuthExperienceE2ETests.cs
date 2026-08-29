using System.Text.RegularExpressions;
using BzsOIDC.Idp.E2ETests.Infrastructure;
using Microsoft.Playwright;

namespace BzsOIDC.Idp.E2ETests;

[Collection(E2ETestCollection.Name)]
public sealed class AuthExperienceE2ETests(AppHostFixture fixture) : E2EPageTest
{
    [Fact]
    public async Task LoginPage_AllowsThemeAndLanguageSwitching()
    {
        await Page.GotoAsync(fixture.BuildUrl("/login"));
        await AppUi.WaitForAppReadyAsync(this);
        await Expect(Page.Locator("#username")).ToBeVisibleAsync();

        await AppUi.OpenPreferencesAsync(this);
        await Page.GetByRole(AriaRole.Menuitemradio, new() { Name = "EN" }).ClickAsync();
        await AppUi.WaitForAppReadyAsync(this);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("Sign in|登录", RegexOptions.IgnoreCase) })).ToBeVisibleAsync();

        await AppUi.OpenPreferencesAsync(this);
        await Page.GetByRole(AriaRole.Menuitemradio, new() { NameRegex = new Regex("Light|浅色", RegexOptions.IgnoreCase) }).ClickAsync();
        await AppUi.WaitForAppReadyAsync(this);
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
    }

    [Fact]
    public async Task LoginPage_RendersHostedWasmRootWithoutServerCircuit()
    {
        var circuitRequests = 0;
        Page.Request += (_, request) =>
        {
            if (request.Url.Contains("/_blazor", StringComparison.OrdinalIgnoreCase))
            {
                circuitRequests++;
            }
        };

        await Page.GotoAsync(fixture.BuildUrl("/login"));
        await AppUi.WaitForAppReadyAsync(this);
        var passwordInput = Page.Locator("#password");

        await Expect(passwordInput).ToBeVisibleAsync();
        await Expect(Page.Locator("#app")).ToContainTextAsync(new Regex("Sign in|登录", RegexOptions.IgnoreCase));
        await Expect(Page.Locator("#components-reconnect-modal")).ToHaveCountAsync(0);
        Assert.Equal(0, circuitRequests);
    }

    [Fact]
    public async Task RegisterPage_AllowsCreatingANewUser()
    {
        var userName = AppUi.UniqueName("user");
        var email = $"{userName}@example.com";
        const string password = "Passw0rd!";

        await Page.GotoAsync(fixture.BuildUrl("/register"));
        await AppUi.WaitForAppReadyAsync(this);
        await Expect(Page.Locator("#register-username")).ToBeVisibleAsync();

        await Page.Locator("#register-username").FillAsync(userName);
        await Page.Locator("#register-email").FillAsync(email);
        await Page.Locator("#register-password").FillAsync(password);
        await Page.Locator("#register-confirm-password").FillAsync(password);
        await Page.Locator("#register-confirm-password").BlurAsync();

        await Page.Locator("form").First.EvaluateAsync("form => form.requestSubmit()");
        await Expect(Page).ToHaveURLAsync(new Regex("/$"), new() { Timeout = 30000 });
        await AppUi.WaitForAppReadyAsync(this);

        await Expect(Page.Locator(".wasm-nav-logout")).ToBeVisibleAsync();
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Startup")]
    public async Task PublicPages_RenderHostedWasmRoutes()
    {
        await Page.GotoAsync(fixture.BuildUrl("/login"));
        await AppUi.WaitForAppReadyAsync(this);
        await Expect(Page.GetByRole(AriaRole.Heading)).ToContainTextAsync(new Regex("登录|Sign in", RegexOptions.IgnoreCase));

        await Page.GotoAsync(fixture.BuildUrl("/logout?returnUrl=%2F"));
        await AppUi.WaitForAppReadyAsync(this);
        await Expect(Page.GetByRole(AriaRole.Heading)).ToContainTextAsync(new Regex("退出|sign out", RegexOptions.IgnoreCase));

        await Page.GotoAsync(fixture.BuildUrl("/account/denied"));
        await AppUi.WaitForAppReadyAsync(this);
        await Expect(Page.Locator("[data-testid='wasm-denied']")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid='wasm-denied'] a").Last).ToHaveAttributeAsync("href", "/login");

        await Page.GotoAsync(fixture.BuildUrl("/not-found"));
        await AppUi.WaitForAppReadyAsync(this);
        await Expect(Page.GetByRole(AriaRole.Heading)).ToContainTextAsync(new Regex("不存在|not found|404", RegexOptions.IgnoreCase));
    }

    [Fact]
    public async Task LogoutFlow_AfterAdminSession_RedirectsProtectedRouteBackToLogin()
    {
        await AppUi.LoginAsAdminAsync(this, fixture, "/admin/users");
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users", RegexOptions.IgnoreCase), new() { Timeout = 30000 });
        await Page.GotoAsync(fixture.BuildUrl("/admin/users"));
        await Page.Locator("[data-testid='users-table']").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });

        await AppUi.LogoutAsync(this, fixture, "/admin/users");

        await Expect(Page).ToHaveURLAsync(new Regex(@"/login\?returnUrl=%2Fadmin%2Fusers", RegexOptions.IgnoreCase));
        await Expect(Page.Locator("#username")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task LoginPage_WhenAlreadyAuthenticated_RedirectsToHome()
    {
        await AppUi.LoginAsAdminAsync(this, fixture);

        await Page.GotoAsync(fixture.BuildUrl("/login"));

        await Expect(Page).ToHaveURLAsync(new Regex(@"/$"), new() { Timeout = 30000 });
        await AppUi.WaitForAppReadyAsync(this);
        await Expect(Page.Locator("#username")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task LoginPage_WithPassword_RendersAdminDashboardImmediatelyAfterRedirect()
    {
        await AppUi.LoginWithPasswordAsync(
            this,
            fixture,
            TestCredentials.AdminUserName,
            TestCredentials.AdminPassword,
            "/");

        await Expect(Page).ToHaveURLAsync(new Regex(@"/$"), new() { Timeout = 30000 });
        await Expect(Page.Locator(".dashboard-hero-actions a[href='/admin/users']")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Expect(Page.Locator(".dashboard-hero-actions a[href='/login']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task SidebarNavigation_WhenAuthenticated_ShowsLogoutAction()
    {
        await AppUi.LoginAsAdminAsync(this, fixture);
        await Page.GotoAsync(fixture.BuildUrl("/"));
        await AppUi.WaitForAppReadyAsync(this);

        await Expect(Page.Locator(".wasm-nav-logout")).ToContainTextAsync(new Regex("退出|log out", RegexOptions.IgnoreCase));
    }
}
