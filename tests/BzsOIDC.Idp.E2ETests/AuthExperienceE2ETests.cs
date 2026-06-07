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
        await Expect(Page.Locator("#username")).ToBeVisibleAsync();

        await AppUi.OpenPreferencesAsync(this);
        await Page.GetByRole(AriaRole.Menuitemradio, new() { Name = "EN" }).ClickAsync();
        await AppUi.WaitForAppReadyAsync(this);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex("Welcome back", RegexOptions.IgnoreCase) })).ToBeVisibleAsync();

        await AppUi.OpenPreferencesAsync(this);
        await Page.GetByRole(AriaRole.Menuitemradio, new() { NameRegex = new Regex("Light|浅色", RegexOptions.IgnoreCase) }).ClickAsync();
        await AppUi.WaitForAppReadyAsync(this);
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
    }

    [Fact]
    public async Task LoginPage_TogglePassword_DoesNotShiftToggleButtonPosition()
    {
        await Page.GotoAsync(fixture.BuildUrl("/login"));
        var passwordInput = Page.Locator("#password");
        var toggleButton = Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("切换密码可见性|Toggle password", RegexOptions.IgnoreCase) });

        await Expect(passwordInput).ToBeVisibleAsync();
        await AppUi.WaitForAppReadyAsync(this);
        await passwordInput.FillAsync("Passw0rd!");
        await Expect(toggleButton).ToHaveAttributeAsync("aria-pressed", "false");

        var beforeBox = await toggleButton.BoundingBoxAsync();
        Assert.NotNull(beforeBox);

        await toggleButton.ClickAsync();
        await Expect(toggleButton).ToHaveAttributeAsync("aria-pressed", "true", new() { Timeout = 5000 });
        await Expect(passwordInput).ToHaveAttributeAsync("type", "text", new() { Timeout = 5000 });

        var afterBox = await toggleButton.BoundingBoxAsync();
        Assert.NotNull(afterBox);

        Assert.InRange(Math.Abs(afterBox!.Y - beforeBox!.Y), 0, 1);
    }

    [Fact]
    public async Task RegisterPage_AllowsCreatingANewUser()
    {
        var userName = AppUi.UniqueName("user");
        var email = $"{userName}@example.com";
        const string password = "Passw0rd!";

        await Page.GotoAsync(fixture.BuildUrl("/register"));
        await Expect(Page.Locator("#register-username")).ToBeVisibleAsync();
        await AppUi.WaitForAppReadyAsync(this);

        await Page.Locator("#register-username").FillAsync(userName);
        await Page.Locator("#register-email").FillAsync(email);
        await Page.Locator("#register-password").FillAsync(password);
        await Page.Locator("#register-confirm-password").FillAsync(password);
        await Page.Locator("#register-confirm-password").BlurAsync();

        await Page.Locator("form.register-form").EvaluateAsync("form => form.requestSubmit()");
        await Expect(Page).ToHaveURLAsync(new Regex("/$"), new() { Timeout = 30000 });
        await AppUi.WaitForAppReadyAsync(this);

        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("打开用户菜单|Open user menu", RegexOptions.IgnoreCase) })).ToBeVisibleAsync();
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Category", "Startup")]
    public async Task PublicPages_RenderExpectedServerOwnedShells()
    {
        await Page.GotoAsync(fixture.BuildUrl("/login"));
        await Expect(Page.GetByRole(AriaRole.Heading)).ToContainTextAsync(new Regex("欢迎回来|Welcome back", RegexOptions.IgnoreCase));

        await Page.GotoAsync(fixture.BuildUrl("/logout?returnUrl=%2F"));
        await Expect(Page.GetByRole(AriaRole.Heading)).ToContainTextAsync(new Regex("退出|sign out", RegexOptions.IgnoreCase));

        await Page.GotoAsync(fixture.BuildUrl("/account/denied"));
        await Expect(Page.Locator(".denied-page")).ToBeVisibleAsync();
        await Expect(Page.Locator(".denied-secondary")).ToHaveAttributeAsync("href", "/login");

        await Page.GotoAsync(fixture.BuildUrl("/not-found"));
        await Expect(Page.GetByRole(AriaRole.Heading)).ToContainTextAsync(new Regex("不存在|not found", RegexOptions.IgnoreCase));
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
    public async Task SidebarAvatar_WhenClicked_OpensUserMenu()
    {
        await AppUi.LoginAsAdminAsync(this, fixture);
        await Page.GotoAsync(fixture.BuildUrl("/"));

        await AppUi.OpenSidebarUserMenuAsync(this);

        await Expect(Page.Locator(".sidebar-user-panel__action")).ToContainTextAsync(new Regex("退出|log out", RegexOptions.IgnoreCase));
    }
}
