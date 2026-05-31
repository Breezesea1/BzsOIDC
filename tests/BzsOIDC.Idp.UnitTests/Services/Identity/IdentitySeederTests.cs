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
        var roleService = Substitute.For<IRoleService>();
        var rolePermissionService = Substitute.For<IRolePermissionService>();
        var permissionCatalogService = Substitute.For<IPermissionCatalogService>();
        var oidcScopeService = Substitute.For<IOidcScopeService>();
        var oidcClientPermissionBackfillService = CreateOidcClientPermissionBackfillService();
        var userService = Substitute.For<IUserService>();

        var options = Options.Create(new IdentitySeedOptions
        {
            Admin = new SeedAdminOptions
            {
                UserName = string.Empty,
                Password = "admin123",
            },
        });
        var configuration = new ConfigurationBuilder().Build();

        var sut = new IdentitySeeder(
            roleService,
            rolePermissionService,
            permissionCatalogService,
            oidcScopeService,
            oidcClientPermissionBackfillService,
            userService,
            options,
            configuration,
            NullLogger<IdentitySeeder>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SeedAsync());
    }

    [Fact]
    public async Task SeedAsync_WhenAdminAlreadyExists_DoesNotCreateUserAgain()
    {
        var roleService = Substitute.For<IRoleService>();
        var rolePermissionService = Substitute.For<IRolePermissionService>();
        var permissionCatalogService = Substitute.For<IPermissionCatalogService>();
        var oidcScopeService = Substitute.For<IOidcScopeService>();
        var oidcClientPermissionBackfillService = CreateOidcClientPermissionBackfillService();
        var userService = Substitute.For<IUserService>();

        roleService.GetByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BzsRole { Id = Guid.NewGuid(), Name = IdentitySeedConstants.AdminRoleName });
        rolePermissionService.SyncPermissionsAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(IdentityResult.Success));

        var existingAdmin = new BzsUser
        {
            Id = Guid.NewGuid(),
            UserName = "admin",
        };

        userService.GetByNameAsync("admin", Arg.Any<CancellationToken>())
            .Returns(existingAdmin);
        userService.EnsurePasswordAsync(existingAdmin.Id, "admin123", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(IdentityResult.Success));
        userService.IsInRoleAsync(default, default!, default)
            .ReturnsForAnyArgs(Task.FromResult(true));

        var options = Options.Create(new IdentitySeedOptions
        {
            Admin = new SeedAdminOptions
            {
                UserName = "admin",
                Password = "admin123",
            },
            InitialRoles = [IdentitySeedConstants.UserRoleName],
            RolePermissions = new Dictionary<string, string[]>
            {
                [IdentitySeedConstants.UserRoleName] = ["users.read.self"],
            },
            PermissionCatalog = [new() { ResourceKey = "api", Permissions = [new() { Name = "users.read.self", ReleaseScopes = ["api"] }] }],
        });
        var configuration = new ConfigurationBuilder().Build();

        var sut = new IdentitySeeder(
            roleService,
            rolePermissionService,
            permissionCatalogService,
            oidcScopeService,
            oidcClientPermissionBackfillService,
            userService,
            options,
            configuration,
            NullLogger<IdentitySeeder>.Instance);

        await sut.SeedAsync();

        await permissionCatalogService.Received(1)
            .InitializeDefaultsAsync(options.Value.PermissionCatalog, Arg.Any<CancellationToken>());
        await oidcScopeService.Received(1)
            .InitializeDefaultsIfMissingAsync(options.Value.AdditionalScopes, Arg.Any<CancellationToken>());
        await userService.Received(1)
            .EnsurePasswordAsync(existingAdmin.Id, "admin123", Arg.Any<CancellationToken>());
        await userService.DidNotReceive()
            .CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await userService.DidNotReceive()
            .AddToRoleAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedAsync_WhenAdminMissing_CreatesUserAndAssignsAdminRole()
    {
        var roleService = Substitute.For<IRoleService>();
        var rolePermissionService = Substitute.For<IRolePermissionService>();
        var permissionCatalogService = Substitute.For<IPermissionCatalogService>();
        var oidcScopeService = Substitute.For<IOidcScopeService>();
        var oidcClientPermissionBackfillService = CreateOidcClientPermissionBackfillService();
        var userService = Substitute.For<IUserService>();

        roleService.GetByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BzsRole { Id = Guid.NewGuid(), Name = IdentitySeedConstants.AdminRoleName });
        rolePermissionService.SyncPermissionsAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(IdentityResult.Success));

        var createdAdmin = new BzsUser
        {
            Id = Guid.NewGuid(),
            UserName = "admin",
        };

        userService.GetByNameAsync("admin", Arg.Any<CancellationToken>())
            .Returns((BzsUser?)null, createdAdmin);
        userService.CreateAsync("admin", "admin123", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(IdentityResult.Success));
        userService.EnsurePasswordAsync(createdAdmin.Id, "admin123", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(IdentityResult.Success));
        userService.IsInRoleAsync(default, default!, default)
            .ReturnsForAnyArgs(Task.FromResult(false));
        userService.AddToRoleAsync(default, default!, default)
            .ReturnsForAnyArgs(Task.FromResult(IdentityResult.Success));

        var options = Options.Create(new IdentitySeedOptions
        {
            Admin = new SeedAdminOptions
            {
                UserName = "admin",
                Password = "admin123",
            },
            InitialRoles = [IdentitySeedConstants.UserRoleName],
            RolePermissions = new Dictionary<string, string[]>
            {
                [IdentitySeedConstants.UserRoleName] = ["users.read.self"],
            },
            PermissionCatalog = [new() { ResourceKey = "api", Permissions = [new() { Name = "users.read.self", ReleaseScopes = ["api"] }] }],
        });
        var configuration = new ConfigurationBuilder().Build();

        var sut = new IdentitySeeder(
            roleService,
            rolePermissionService,
            permissionCatalogService,
            oidcScopeService,
            oidcClientPermissionBackfillService,
            userService,
            options,
            configuration,
            NullLogger<IdentitySeeder>.Instance);

        await sut.SeedAsync();

        await userService.Received(1)
            .CreateAsync("admin", "admin123", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await userService.Received(1)
            .EnsurePasswordAsync(createdAdmin.Id, "admin123", Arg.Any<CancellationToken>());
        await userService.Received(1)
            .AddToRoleAsync(createdAdmin.Id, IdentitySeedConstants.AdminRoleName, Arg.Any<CancellationToken>());
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
