using System.Security.Claims;
using Bunit;
using BzsOIDC.Idp.Components.Admin;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Components.Admin;

public sealed class PermissionManagementPageTests
{
    [Fact]
    public void Render_WhenTopologyHasData_ShowsPermissionCenterSections()
    {
        using var context = CreateContext();
        var permissionTopology = Substitute.For<IPermissionTopology>();
        permissionTopology.GetResourcesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ProtectedResourceResponse>>([
                new ProtectedResourceResponse
                {
                    Key = "orders-api",
                    DisplayName = "Orders API",
                    Permissions =
                    [
                        new PermissionDefinitionResponse
                        {
                            ResourceKey = "orders-api",
                            Name = "orders.read",
                            DisplayName = "Read orders",
                            IsActive = true,
                            ReleaseScopes = ["orders-api"],
                            AssignedRoles =
                            [
                                new RolePermissionAssignmentResponse
                                {
                                    RoleId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                                    RoleName = "admin",
                                    Assigned = true,
                                },
                            ],
                        },
                    ],
                },
            ]));

        context.Services.AddSingleton(permissionTopology);
        context.Services.AddSingleton<IStringLocalizer<PermissionManagement>, TestStringLocalizer<PermissionManagement>>();
        context.Services.AddSingleton<IHttpContextAccessor>(CreateAdminHttpContextAccessor());

        var cut = context.Render<PermissionManagement>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Orders API", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("orders.read", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("ReleaseScopes", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("RoleAssignments", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("RoleAssignmentsReadOnlyLead", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("admin", cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("input[type='checkbox']"));
            Assert.Contains("Topology", cut.Markup, StringComparison.Ordinal);
        });
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
