using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.Services.Oidc;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenIddict.Abstractions;

namespace BzsOIDC.Idp.UnitTests.Services.Identity;

public sealed class IdentitySeederTests
{
    [Fact]
    public async Task SeedAsync_WhenAdminUserNameMissing_ThrowsInvalidOperationException()
    {
        var sut = new IdentitySeeder(
            Substitute.For<IPermissionTopology>(),
            Substitute.For<IOidcScopeService>(),
            CreateOidcClientPermissionBackfillService(),
            Substitute.For<IUserService>(),
            Options.Create(new IdentitySeedOptions
            {
                Admin = new SeedAdminOptions
                {
                    UserName = string.Empty,
                    Password = "admin123",
                },
            }),
            new ConfigurationBuilder().Build(),
            NullLogger<IdentitySeeder>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SeedAsync());
    }

    [Fact]
    public async Task SeedAsync_WhenDefaultsAreConfigured_InitializesCompleteTopologyThroughSingleSeam()
    {
        var topology = Substitute.For<IPermissionTopology>();
        topology.InitializeDefaultsAsync(
            Arg.Any<IEnumerable<PermissionTopologySeedResource>>(),
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<IReadOnlyDictionary<string, string[]>>(),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var users = Substitute.For<IUserService>();
        users.GetByNameAsync("admin", Arg.Any<CancellationToken>()).Returns(new BzsUser { Id = Guid.NewGuid(), UserName = "admin" });
        users.EnsurePasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(IdentityResult.Success);
        users.IsInRoleAsync(Arg.Any<Guid>(), IdentitySeedConstants.AdminRoleName, Arg.Any<CancellationToken>()).Returns(true);
        var seed = new IdentitySeeder(topology, Substitute.For<IOidcScopeService>(), CreateOidcClientPermissionBackfillService(), users,
            Options.Create(new IdentitySeedOptions
            {
                Admin = new SeedAdminOptions { UserName = "admin", Password = "password" },
                InitialRoles = ["operators"],
            }),
            new ConfigurationBuilder().Build(), NullLogger<IdentitySeeder>.Instance);

        await seed.SeedAsync();

        await topology.Received(1).InitializeDefaultsAsync(
            Arg.Any<IEnumerable<PermissionTopologySeedResource>>(),
            Arg.Is<IEnumerable<string>>(roles => roles.Contains("operators", StringComparer.OrdinalIgnoreCase)),
            Arg.Is<IReadOnlyDictionary<string, string[]>>(roles =>
                roles.ContainsKey(IdentitySeedConstants.AdminRoleName) &&
                !roles.ContainsKey("operators")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_WhenAdminMissing_CreatesUserAndAssignsAdminRole()
    {
        var topology = Substitute.For<IPermissionTopology>();
        var users = Substitute.For<IUserService>();
        var createdAdmin = new BzsUser
        {
            Id = Guid.NewGuid(),
            UserName = "admin",
        };
        users.GetByNameAsync("admin", Arg.Any<CancellationToken>())
            .Returns((BzsUser?)null, createdAdmin);
        users.CreateAsync("admin", "admin123", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Success);
        users.EnsurePasswordAsync(createdAdmin.Id, "admin123", Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Success);
        users.IsInRoleAsync(createdAdmin.Id, IdentitySeedConstants.AdminRoleName, Arg.Any<CancellationToken>())
            .Returns(false);
        users.AddToRoleAsync(createdAdmin.Id, IdentitySeedConstants.AdminRoleName, Arg.Any<CancellationToken>())
            .Returns(IdentityResult.Success);
        var seed = new IdentitySeeder(
            topology,
            Substitute.For<IOidcScopeService>(),
            CreateOidcClientPermissionBackfillService(),
            users,
            Options.Create(new IdentitySeedOptions
            {
                Admin = new SeedAdminOptions
                {
                    UserName = "admin",
                    Password = "admin123",
                },
            }),
            new ConfigurationBuilder().Build(),
            NullLogger<IdentitySeeder>.Instance);

        await seed.SeedAsync();

        await users.Received(1).CreateAsync("admin", "admin123", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await users.Received(1).EnsurePasswordAsync(createdAdmin.Id, "admin123", Arg.Any<CancellationToken>());
        await users.Received(1).AddToRoleAsync(createdAdmin.Id, IdentitySeedConstants.AdminRoleName, Arg.Any<CancellationToken>());
    }

    private static OidcClientPermissionBackfillService CreateOidcClientPermissionBackfillService()
    {
        var applicationManager = Substitute.For<IOpenIddictApplicationManager>();
        applicationManager
            .ListAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(EmptyApplications());

        return new OidcClientPermissionBackfillService(applicationManager);
    }

    private static async IAsyncEnumerable<object> EmptyApplications()
    {
        await Task.CompletedTask;
        yield break;
    }
}
