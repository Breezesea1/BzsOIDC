using BzsOIDC.Idp.Services.Admin;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.Services.Oidc;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Services.Admin;

public sealed class AdminDashboardServiceTests
{
    [Fact]
    public async Task GetSummaryAsync_WhenUsersIncludeAdmins_ReturnsAggregatedCounts()
    {
        var userAdministration = Substitute.For<IUserAdministration>();
        userAdministration.GetUsersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserAdministrationUser>>([
                new UserAdministrationUser(Guid.Parse("00000000-0000-0000-0000-000000000001"), "admin-01", "admin01@example.com", true),
                new UserAdministrationUser(Guid.Parse("00000000-0000-0000-0000-000000000002"), "user-02", "user02@example.com", false),
                new UserAdministrationUser(Guid.Parse("00000000-0000-0000-0000-000000000003"), "user-03", "user03@example.com", false),
            ]));

        var clientService = Substitute.For<IOidcClientService>();
        clientService.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns([
                new OidcClientResponse
                {
                    ClientId = "web-portal",
                    DisplayName = "Web Portal",
                    AuthFlow = OidcClientAuthFlow.AuthorizationCode,
                    Scopes = ["api"]
                },
                new OidcClientResponse
                {
                    ClientId = "jobs-daemon",
                    DisplayName = "Jobs Daemon",
                    AuthFlow = OidcClientAuthFlow.ClientCredentials,
                    Scopes = ["api", "jobs"]
                }
            ]);

        var permissionTopology = Substitute.For<IPermissionTopology>();
        permissionTopology.GetResourcesAsync(Arg.Any<CancellationToken>())
            .Returns([
                new ProtectedResourceResponse
                {
                    Key = "api",
                    Permissions =
                    [
                        new PermissionDefinitionResponse { Name = "clients.read", ReleaseScopes = ["api"] },
                        new PermissionDefinitionResponse { Name = "clients.write", ReleaseScopes = ["api", "jobs"] },
                    ],
                },
            ]);

        var sut = new AdminDashboardService(userAdministration, clientService, permissionTopology);

        var result = await sut.GetSummaryAsync(CancellationToken.None);

        Assert.Equal(3, result.TotalUsers);
        Assert.Equal(1, result.AdminUsers);
        Assert.Equal(2, result.StandardUsers);
        Assert.Equal(2, result.TotalClients);
        Assert.Equal(1, result.InteractiveClients);
        Assert.Equal(1, result.MachineClients);
        Assert.Equal(2, result.TotalPermissionMappings);
        Assert.Equal(3, result.TotalConfiguredScopes);
        await userAdministration.Received(1).GetUsersAsync(CancellationToken.None);
    }
}

