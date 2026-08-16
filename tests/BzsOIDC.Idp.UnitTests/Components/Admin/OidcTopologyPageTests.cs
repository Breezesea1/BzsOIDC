using System.Security.Claims;
using Bunit;
using BzsOIDC.Idp.Components.Admin;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.Services.Oidc;
using BzsOIDC.Idp.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Components.Admin;

public sealed class OidcTopologyPageTests
{
    [Fact]
    public void Render_WhenAdminAccessGranted_ShowsClientAndScopeTopology()
    {
        using var context = CreateContext();

        var topology = Substitute.For<IOidcAdministrationTopology>();
        topology.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OidcAdministrationTopologySnapshot(
                [new OidcAdministrationClient("client-1", "Client 1", OidcClientAuthFlow.AuthorizationCode, ["api"])],
                [new OidcAdministrationScope("api", "API", null, ["resource"], ["Client 1"], ["clients.read"])],
                1)));

        context.Services.AddSingleton<IOidcAdministrationTopology>(topology);
        context.Services.AddSingleton<IStringLocalizer<ScopeManagement>, TestStringLocalizer<ScopeManagement>>();
        context.Services.AddSingleton<IHttpContextAccessor>(CreateAdminHttpContextAccessor());

        var cut = context.Render<OidcTopology>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Client 1", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("api", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("clients.read", cut.Markup, StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(5));
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static IHttpContextAccessor CreateAdminHttpContextAccessor()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Role, "admin"),
            ],
            "TestAuth");

        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            },
        };
    }
}

