using System.Security.Claims;
using BootstrapBlazor.Components;
using Bunit;
using Bunit.JSInterop;
using BzsOIDC.Idp.Components.Admin;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.Services.Oidc;
using BzsOIDC.Idp.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Components.Admin;

[Collection("BootstrapBlazor component tests")]
public sealed class ClientManagementPageTests
{
    [Fact]
    public void PageSizeChange_RecomputesVisibleRows_AndNextPageShowsRemainingClients()
    {
        using var context = CreateContext();

        var clients = Enumerable.Range(1, 22)
            .Select(index => new OidcClientResponse
            {
                ClientId = $"client-{index:00}",
                DisplayName = $"Client {index:00}",
                AuthFlow = index % 2 == 0 ? OidcClientAuthFlow.AuthorizationCode : OidcClientAuthFlow.ClientCredentials,
                GrantTypes = [index % 2 == 0 ? "authorization_code" : "client_credentials"],
                Scopes = [$"scope-{index:00}"],
                RedirectUris = index % 2 == 0 ? [$"https://app{index:00}.example.com/signin"] : []
            })
            .ToArray();

        RegisterServices(context, clients);

        var cut = context.Render<ClientManagement>();

        cut.WaitForAssertion(() => Assert.Equal(10, cut.FindAll("[data-testid='clients-table'] .table-card > .table-row").Count));

        cut.Find("#client-page-size").Change("20");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(20, cut.FindAll("[data-testid='clients-table'] .table-card > .table-row").Count);
            Assert.Contains("client-20", cut.Markup, StringComparison.Ordinal);
        });

        cut.FindAll("button").Single(button => button.TextContent.Contains("NextPage", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll("[data-testid='clients-table'] .table-card > .table-row").Count);
            Assert.Contains("client-21", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("client-22", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Search_WhenTriggeredFromLaterPage_ResetsToFirstPageAndNoSelectionUiIsRendered()
    {
        using var context = CreateContext();

        var clients = Enumerable.Range(1, 15)
            .Select(index => new OidcClientResponse
            {
                ClientId = $"alpha-client-{index:00}",
                DisplayName = $"Alpha Client {index:00}",
                AuthFlow = OidcClientAuthFlow.AuthorizationCode,
                GrantTypes = ["authorization_code"],
                Scopes = ["api"],
                RedirectUris = [$"https://alpha{index:00}.example.com/signin"]
            })
            .Concat(Enumerable.Range(16, 5).Select(index => new OidcClientResponse
            {
                ClientId = $"beta-client-{index:00}",
                DisplayName = $"Beta Client {index:00}",
                AuthFlow = OidcClientAuthFlow.ClientCredentials,
                GrantTypes = ["client_credentials"],
                Scopes = ["api"]
            }))
            .ToArray();

        RegisterServices(context, clients);

        var cut = context.Render<ClientManagement>();

        cut.WaitForAssertion(() => Assert.Equal(10, cut.FindAll("[data-testid='clients-table'] .table-card > .table-row").Count));
        Assert.Empty(cut.FindAll("[data-testid='clients-table'] input[type='checkbox']"));
        Assert.DoesNotContain("selection-summary", cut.Markup, StringComparison.OrdinalIgnoreCase);

        cut.FindAll("button").Single(button => button.TextContent.Contains("NextPage", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.Contains("beta-client-16", cut.Markup, StringComparison.Ordinal));

        cut.Find("#client-search").Input("alpha");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("alpha-client-01", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("alpha-client-11", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(10, cut.FindAll("[data-testid='clients-table'] .table-card > .table-row").Count);
        });
    }

    [Fact]
    public void AuthFlowFilter_WhenToggled_FiltersRowsAndPreservesCounts()
    {
        using var context = CreateContext();

        var clients = new[]
        {
            new OidcClientResponse
            {
                ClientId = "interactive-client",
                DisplayName = "Interactive Client",
                AuthFlow = OidcClientAuthFlow.AuthorizationCode,
                GrantTypes = ["authorization_code"],
                Scopes = ["api"],
                RedirectUris = ["https://interactive.example.com/signin"]
            },
            new OidcClientResponse
            {
                ClientId = "machine-client",
                DisplayName = "Machine Client",
                AuthFlow = OidcClientAuthFlow.ClientCredentials,
                GrantTypes = ["client_credentials"],
                Scopes = ["api"]
            }
        };

        RegisterServices(context, clients);

        var cut = context.Render<ClientManagement>();

        cut.WaitForAssertion(() => Assert.Contains("interactive-client", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button.admin-filter-tab").Single(button => button.TextContent.Contains("ClientCredentialsFlow", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("machine-client", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("interactive-client", cut.Markup, StringComparison.Ordinal);
        });
    }

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./Components/Admin/AdminDialogShell.razor.js")
            .SetupVoid("activate", _ => true);
        return context;
    }

    private static void RegisterServices(BunitContext context, IReadOnlyList<OidcClientResponse> clients)
    {
        var clientService = Substitute.For<IOidcClientService>();
        clientService.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(clients));
        var scopeService = Substitute.For<IOidcScopeService>();
        scopeService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OidcScopeResponse>>([]));

        context.Services.AddBootstrapBlazor();
        context.Services.AddSingleton<IOidcClientService>(clientService);
        context.Services.AddSingleton<IOidcScopeService>(scopeService);
        context.Services.AddSingleton<IStringLocalizer<ClientManagement>, TestStringLocalizer<ClientManagement>>();
        context.Services.AddSingleton<IHttpContextAccessor>(CreateAdminHttpContextAccessor());
    }

    private static IHttpContextAccessor CreateAdminHttpContextAccessor()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Role, "admin")
            ],
            "TestAuth");

        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }
}
