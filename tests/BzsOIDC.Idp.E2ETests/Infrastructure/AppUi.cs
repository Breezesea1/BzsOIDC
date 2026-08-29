using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;

namespace BzsOIDC.Idp.E2ETests.Infrastructure;

internal static class AppUi
{
    public static async Task LoginAsAdminAsync(PageTest test, AppHostFixture fixture, string returnPath = "/")
    {
        await test.Page.GotoAsync(fixture.BuildUrl($"/testing/e2e/sign-in-admin?returnUrl={Uri.EscapeDataString(returnPath)}"));
        await test.Expect(test.Page.Locator("#username")).ToHaveCountAsync(0);
        await WaitForAppReadyAsync(test);
    }

    public static async Task LoginWithPasswordAsync(
        PageTest test,
        AppHostFixture fixture,
        string userName,
        string password,
        string returnPath = "/",
        bool rememberMe = false)
    {
        await test.Page.GotoAsync(fixture.BuildUrl($"/login?returnUrl={Uri.EscapeDataString(returnPath)}"));
        await test.Expect(test.Page.Locator("#username")).ToBeVisibleAsync();
        await WaitForAppReadyAsync(test);

        await test.Page.Locator("#username").FillAsync(userName);
        await test.Page.Locator("#password").FillAsync(password);

        var rememberMeToggle = test.Page.Locator("input[name='RememberMe']");
        if (rememberMe)
        {
            await rememberMeToggle.CheckAsync();
        }

        await test.Page.Locator("form").First.EvaluateAsync("form => form.requestSubmit()");
        try
        {
            await test.Expect(test.Page.Locator("#username")).ToHaveCountAsync(0, new() { Timeout = 30000 });
        }
        catch (PlaywrightException exception)
        {
            var loginErrorLocator = test.Page.Locator("#login-error");
            var loginError = await loginErrorLocator.CountAsync().ConfigureAwait(false) > 0
                ? await loginErrorLocator.First.TextContentAsync().ConfigureAwait(false)
                : null;
            var title = await test.Page.TitleAsync().ConfigureAwait(false);
            var usernameCount = await test.Page.Locator("#username").CountAsync().ConfigureAwait(false);
            var passwordCount = await test.Page.Locator("#password").CountAsync().ConfigureAwait(false);

            throw new PlaywrightException(
                $"Password login did not leave the login page. Url: {test.Page.Url}. Title: {title}. " +
                $"Username inputs: {usernameCount}. Password inputs: {passwordCount}. Error: {loginError ?? "<none>"}.",
                exception);
        }

        await WaitForAppReadyAsync(test);
    }

    public static async Task LogoutAsync(PageTest test, AppHostFixture fixture, string returnPath = "/")
    {
        await test.Page.GotoAsync(fixture.BuildUrl($"/logout?returnUrl={Uri.EscapeDataString(returnPath)}"));
        await test.Expect(test.Page.Locator("[data-testid='wasm-logout'] button")).ToBeVisibleAsync();
        await test.Page.Locator("[data-testid='wasm-logout'] button").ClickAsync();
        await WaitForAppReadyAsync(test);
    }

    public static async Task WaitForAppReadyAsync(PageTest test)
    {
        await test.Page.WaitForFunctionAsync(
            """
            () => {
                const app = document.querySelector('#app');
                const loading = document.querySelector('#blazor-loading');
                return !!app && app.children.length > 0 && (!loading || loading.hidden);
            }
            """,
            null,
            new() { Timeout = 30000, PollingInterval = 500 });
    }

    public static async Task OpenPreferencesAsync(PageTest test)
    {
        var trigger = test.Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("偏好设置|Preferences", RegexOptions.IgnoreCase) });
        var menu = test.Page.GetByRole(AriaRole.Menu);

        await WaitForAppReadyAsync(test);
        await test.Expect(trigger).ToBeVisibleAsync();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await trigger.ClickAsync();

            try
            {
                await test.Expect(menu).ToBeVisibleAsync(new() { Timeout = 3000 });
                return;
            }
            catch (PlaywrightException) when (attempt < 2)
            {
                await test.Page.WaitForTimeoutAsync(350);
            }
        }

        await test.Expect(menu).ToBeVisibleAsync();
    }

    public static string UniqueName(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 9, prefix.Length + 9)];
    }
}
