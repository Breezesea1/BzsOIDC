using System.Security.Claims;
using BootstrapBlazor.Components;
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

[Collection("BootstrapBlazor component tests")]
public sealed class ScopeManagementPageTests
{
    [Fact]
    public void SearchInput_WhenTermMatchesOneScope_FiltersMatchingScopes()
    {
        using var context = CreateContext();

        var scopes = new[]
        {
            new OidcScopeResponse { Name = "api.read", DisplayName = "Read API", Resources = ["api"] },
            new OidcScopeResponse { Name = "api.write", DisplayName = "Write API", Resources = ["api"] },
        };

        RegisterServices(context, scopes);

        var cut = context.Render<ScopeManagement>();

        cut.WaitForAssertion(() => Assert.Contains("api.read", cut.Markup, StringComparison.Ordinal));

        cut.Find("#scope-search").Input("write");

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("api.read", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("api.write", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void PageSizeChange_RecomputesVisibleRows_AndNextPageShowsRemainingScopes()
    {
        using var context = CreateContext();

        var scopes = Enumerable.Range(1, 22)
            .Select(index => new OidcScopeResponse
            {
                Name = $"api.scope.{index:00}",
                DisplayName = $"Scope {index:00}",
                Description = $"Scope description {index:00}",
                Resources = [$"resource-{index:00}"]
            })
            .ToArray();

        RegisterServices(context, scopes);

        var cut = context.Render<ScopeManagement>();

        cut.WaitForAssertion(() => Assert.Equal(10, cut.FindAll("[data-testid='scopes-table'] .table-card > .table-row").Count));

        cut.Find("#scope-page-size").Change("20");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(20, cut.FindAll("[data-testid='scopes-table'] .table-card > .table-row").Count);
            Assert.Contains("api.scope.20", cut.Markup, StringComparison.Ordinal);
        });

        cut.FindAll("button").Single(button => button.TextContent.Contains("NextPage", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll("[data-testid='scopes-table'] .table-card > .table-row").Count);
            Assert.Contains("api.scope.21", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("api.scope.22", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Search_WhenTriggeredFromLaterPage_ResetsToFirstPageAndNoSelectionUiIsRendered()
    {
        using var context = CreateContext();

        var scopes = Enumerable.Range(1, 15)
            .Select(index => new OidcScopeResponse
            {
                Name = $"alpha.scope.{index:00}",
                DisplayName = $"Alpha Scope {index:00}",
                Resources = ["alpha-resource"]
            })
            .Concat(Enumerable.Range(16, 5).Select(index => new OidcScopeResponse
            {
                Name = $"beta.scope.{index:00}",
                DisplayName = $"Beta Scope {index:00}",
                Resources = ["beta-resource"]
            }))
            .ToArray();

        RegisterServices(context, scopes);

        var cut = context.Render<ScopeManagement>();

        cut.WaitForAssertion(() => Assert.Equal(10, cut.FindAll("[data-testid='scopes-table'] .table-card > .table-row").Count));
        Assert.Empty(cut.FindAll("[data-testid='scopes-table'] input[type='checkbox']"));
        Assert.DoesNotContain("selection-summary", cut.Markup, StringComparison.OrdinalIgnoreCase);

        cut.FindAll("button").Single(button => button.TextContent.Contains("NextPage", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.Contains("beta.scope.16", cut.Markup, StringComparison.Ordinal));

        cut.Find("#scope-search").Input("alpha");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("alpha.scope.01", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("alpha.scope.11", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(10, cut.FindAll("[data-testid='scopes-table'] .table-card > .table-row").Count);
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

    private static void RegisterServices(BunitContext context, IReadOnlyList<OidcScopeResponse> scopes)
    {
        var service = Substitute.For<IOidcScopeService>();
        var topology = Substitute.For<IOidcAdministrationTopology>();
        topology.ReadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OidcAdministrationTopologySnapshot(
                [],
                scopes.Select(static scope => new OidcAdministrationScope(
                    scope.Name,
                    scope.DisplayName,
                    scope.Description,
                    scope.Resources,
                    [],
                    [])).ToArray(),
                0)));

        context.Services.AddBootstrapBlazor();
        context.Services.AddSingleton<IOidcScopeService>(service);
        context.Services.AddSingleton<IOidcAdministrationTopology>(topology);
        context.Services.AddSingleton<IStringLocalizer<ScopeManagement>, TestStringLocalizer<ScopeManagement>>();
        context.Services.AddSingleton<IHttpContextAccessor>(CreateAdminHttpContextAccessor());
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
