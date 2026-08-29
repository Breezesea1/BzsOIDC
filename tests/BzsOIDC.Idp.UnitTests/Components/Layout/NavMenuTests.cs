using System.Security.Claims;
using Bunit;
using BzsOIDC.Idp.Client.Components.Layout;
using BzsOIDC.Idp.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace BzsOIDC.Idp.UnitTests.Components.Layout;

public sealed class NavMenuTests
{
    [Fact]
    public void NavMenu_WhenAuthenticated_RendersCurrentUserIdentity()
    {
        using var context = CreateContext();
        SetAuthenticationState(
            context,
            new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, "readonly-user"),
            new Claim(ClaimTypes.Email, "readonly-user@bzsoidc.local"),
        ], "TestAuth")));

        var cut = RenderNavMenu(context);

        Assert.Contains("readonly-user", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("readonly-user@bzsoidc.local", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("admin@bzsoidc.com", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("/login", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NavMenu_WhenAnonymous_RendersGuestFooter()
    {
        using var context = CreateContext();
        SetAuthenticationState(context, new ClaimsPrincipal(new ClaimsIdentity()));

        var cut = RenderNavMenu(context);

        Assert.Contains("Guest", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Sign in to continue", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("/login", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NavMenu_WhenAuthenticated_OpensUserPanelFromAvatarButton()
    {
        using var context = CreateContext();
        SetAuthenticationState(
            context,
            new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "readonly-user"),
            ], "TestAuth")));

        var cut = RenderNavMenu(context);

        cut.Find("button.sidebar-avatar").Click();

        Assert.Contains("/logout?returnUrl=%2F", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NavMenu_WhenAuthenticated_RendersDedicatedUserMenuWrapper()
    {
        using var context = CreateContext();
        SetAuthenticationState(
            context,
            new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "readonly-user"),
            ], "TestAuth")));

        var cut = RenderNavMenu(context);

        Assert.Contains("class=\"sidebar-user-menu\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("class=\"sidebar-user-menu__trigger sidebar-surface\"", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void WasmNavMenu_WhenRouteLinkActivated_RequestsNavigationClose()
    {
        using var context = CreateContext();
        SetAuthenticationState(
            context,
            new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "admin"),
                new Claim(ClaimTypes.Role, "admin"),
            ], "TestAuth")));

        var closeRequests = 0;
        var cut = context.Render<CascadingAuthenticationState>(parameters => parameters
            .AddChildContent<WasmNavMenu>(child => child
                .Add(x => x.OnNavigate, EventCallback.Factory.Create(this, () => closeRequests++))));

        cut.Find("a[href='/admin/dashboard']").Click();

        Assert.Equal(1, closeRequests);
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddAuthorization();
        context.Services.AddSingleton<IAuthorizationService, AllowAllAuthorizationService>();
        context.Services.AddSingleton<IStringLocalizer<NavMenu>, TestStringLocalizer<NavMenu>>();
        return context;
    }

    private static IRenderedComponent<CascadingAuthenticationState> RenderNavMenu(BunitContext context)
    {
        return context.Render<CascadingAuthenticationState>(parameters => parameters
            .AddChildContent<NavMenu>());
    }

    private static void SetAuthenticationState(BunitContext context, ClaimsPrincipal user)
    {
        context.Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(user));
    }

    private sealed class TestAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        private readonly AuthenticationState _state = new(user);

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            return Task.FromResult(_state);
        }
    }

    private sealed class AllowAllAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
        {
            return Task.FromResult(user.Identity?.IsAuthenticated == true
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
        {
            return Task.FromResult(user.Identity?.IsAuthenticated == true
                ? AuthorizationResult.Success()
                : AuthorizationResult.Failed());
        }
    }
}
