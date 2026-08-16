using System.Security.Claims;
using BzsOIDC.Idp.Infra;
using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BzsOIDC.Idp.UnitTests.Services.Identity;

public sealed class PermissionTopologyServiceTests
{
    [Fact]
    public async Task UpsertResourceAsync_WhenTopologyCommandsReceiveInvalidInputs_PreservesNormalizationAndValidationResults()
    {
        await using var harness = await SqliteHarness.CreateAsync();

        var resource = await harness.Topology.UpsertResourceAsync("Orders-API", new ProtectedResourceUpsertRequest
        {
            DisplayName = "Orders API",
            Description = "Order service",
        });
        var missingResource = await harness.Topology.UpsertPermissionAsync("missing", "orders.read", new PermissionDefinitionUpsertRequest());
        var emptyScopes = await harness.Topology.SyncReleaseScopesAsync("orders.read", []);
        var invalidPermissions = await harness.Topology.ValidateAssignablePermissionsAsync(["missing.permission"]);

        Assert.Equal(PermissionTopologyCommandStatus.Success, resource.Status);
        Assert.Equal("orders-api", resource.Value!.Key);
        Assert.Equal(PermissionTopologyCommandStatus.NotFound, missingResource.Status);
        Assert.Equal(PermissionTopologyCommandStatus.ValidationFailed, emptyScopes.Status);
        Assert.Equal(["missing.permission"], invalidPermissions);
    }

    [Fact]
    public async Task GetResourcesAsync_WhenRolesAreAssigned_ProjectsRolesInNameOrder()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        await harness.Topology.UpsertResourceAsync("api", new ProtectedResourceUpsertRequest());
        await harness.Topology.UpsertPermissionAsync("api", "orders.read", new PermissionDefinitionUpsertRequest());
        var zulu = await harness.CreateRoleAsync("zulu");
        var alpha = await harness.CreateRoleAsync("alpha");
        await harness.RoleManager.AddClaimAsync(zulu, new Claim(PermissionConstants.ClaimType, "ORDERS.READ"));
        await harness.RoleManager.AddClaimAsync(alpha, new Claim("Permission", "orders.read"));
        await harness.RoleManager.AddClaimAsync(alpha, new Claim(PermissionConstants.ClaimType, "ORDERS.READ"));

        var permission = Assert.Single(Assert.Single(await harness.Topology.GetResourcesAsync()).Permissions);

        Assert.Equal(["alpha", "zulu"], permission.AssignedRoles.Select(static assignment => assignment.RoleName));
        Assert.All(permission.AssignedRoles, static assignment => Assert.True(assignment.Assigned));
        Assert.Equal(["orders.read"], await harness.Topology.GetRolePermissionsAsync(alpha.Id));
    }

    [Fact]
    public async Task ResolveReleaseScopesAsync_WhenPermissionIsInactive_RetainsAssignmentButExcludesRelease()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        await harness.Topology.UpsertResourceAsync("api", new ProtectedResourceUpsertRequest());
        await harness.Topology.UpsertPermissionAsync("api", "orders.read", new PermissionDefinitionUpsertRequest { IsActive = false });
        await harness.Topology.SyncReleaseScopesAsync("orders.read", ["api"]);
        var role = await harness.CreateRoleAsync("operators");
        await harness.RoleManager.AddClaimAsync(role, new Claim(PermissionConstants.ClaimType, "orders.read"));

        var resource = Assert.Single(await harness.Topology.GetResourcesAsync());

        Assert.Single(resource.Permissions[0].AssignedRoles);
        Assert.Empty(await harness.Topology.ResolveReleaseScopesAsync(["orders.read"]));
    }

    [Fact]
    public async Task UpsertResourceAsync_WhenActivityChanges_InvalidatesReleaseScopeCache()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        await harness.Topology.UpsertResourceAsync("api", new ProtectedResourceUpsertRequest());
        await harness.Topology.UpsertPermissionAsync("api", "orders.read", new PermissionDefinitionUpsertRequest());
        await harness.Topology.SyncReleaseScopesAsync("orders.read", ["api"]);
        Assert.Single(await harness.Topology.ResolveReleaseScopesAsync(["orders.read"]));

        await harness.Topology.UpsertResourceAsync("api", new ProtectedResourceUpsertRequest { IsActive = false });
        Assert.Empty(await harness.Topology.ResolveReleaseScopesAsync(["orders.read"]));

        await harness.Topology.UpsertResourceAsync("api", new ProtectedResourceUpsertRequest { IsActive = true });
        Assert.Single(await harness.Topology.ResolveReleaseScopesAsync(["orders.read"]));
    }

    [Fact]
    public async Task UpdateRoleAsync_WhenAdminRoleIsTargeted_ProtectsLifecycleButAllowsPermissionEditing()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        await harness.Topology.UpsertResourceAsync("api", new ProtectedResourceUpsertRequest());
        await harness.Topology.UpsertPermissionAsync("api", "roles.read", new PermissionDefinitionUpsertRequest());
        var admin = await harness.CreateRoleAsync(IdentitySeedConstants.AdminRoleName);

        var rename = await harness.Topology.UpdateRoleAsync(admin.Id, new RoleUpsertRequest { Name = "operators" });
        var sync = await harness.Topology.SyncRolePermissionsAsync(admin.Id, ["roles.read"]);
        var delete = await harness.Topology.DeleteRoleAsync(admin.Id);

        Assert.Equal(PermissionTopologyCommandStatus.Protected, rename.Status);
        Assert.Equal(PermissionTopologyCommandStatus.Success, sync.Status);
        Assert.Equal(PermissionTopologyCommandStatus.Protected, delete.Status);
    }

    [Fact]
    public async Task InitializeDefaultsAsync_WhenCalledTwice_IsIdempotentAndPreservesManualResources()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        var seed = new[] { new PermissionTopologySeedResource { ResourceKey = "api", Permissions = [new PermissionTopologySeedPermission { Name = "roles.read", ReleaseScopes = ["api"] }] } };
        await harness.Topology.UpsertResourceAsync("manual", new ProtectedResourceUpsertRequest());

        await harness.Topology.InitializeDefaultsAsync(seed, ["operators"], new Dictionary<string, string[]> { ["operators"] = ["roles.read"] });
        await harness.Topology.InitializeDefaultsAsync(seed, ["operators"], new Dictionary<string, string[]> { ["operators"] = ["roles.read"] });

        var resources = await harness.Topology.GetResourcesAsync();
        Assert.Equal(["api", "manual"], resources.Select(static resource => resource.Key));
        Assert.Equal(["api"], resources[0].Permissions[0].ReleaseScopes);
        Assert.Equal(["roles.read"], await harness.Topology.GetRolePermissionsAsync((await harness.Topology.GetAllRolesAsync()).Single().Id));
    }

    [Fact]
    public async Task InitializeDefaultsAsync_WhenSeedTopologyChanges_RemovesObsoleteResourcesAndKeepsFirstPermissionOwner()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        await harness.Topology.UpsertResourceAsync("legacy-api", new ProtectedResourceUpsertRequest());
        await harness.Topology.UpsertPermissionAsync("legacy-api", "legacy.read", new PermissionDefinitionUpsertRequest());

        await harness.Topology.InitializeDefaultsAsync(
        [
            new PermissionTopologySeedResource
            {
                ResourceKey = "api",
                Permissions = [new PermissionTopologySeedPermission { Name = "orders.read", ReleaseScopes = ["api"] }],
            },
            new PermissionTopologySeedResource
            {
                ResourceKey = "orders-api",
                Permissions = [new PermissionTopologySeedPermission { Name = "orders.read", ReleaseScopes = ["orders-api"] }],
            },
        ], [], new Dictionary<string, string[]>());

        var resources = await harness.Topology.GetResourcesAsync();
        var permission = Assert.Single(resources.SelectMany(static resource => resource.Permissions));
        Assert.DoesNotContain(resources, static resource => resource.Key == "legacy-api");
        Assert.Equal("api", permission.ResourceKey);
        Assert.Equal(["api"], permission.ReleaseScopes);
    }

    [Fact]
    public async Task CreateRoleAsync_WhenOrdinaryRolesAreManaged_PreservesSortedSummariesAndRoleLifecycle()
    {
        await using var harness = await SqliteHarness.CreateAsync();

        var zulu = await harness.Topology.CreateRoleAsync(new RoleUpsertRequest { Name = "zulu" });
        var alpha = await harness.Topology.CreateRoleAsync(new RoleUpsertRequest { Name = "alpha" });
        var missingUpdate = await harness.Topology.UpdateRoleAsync(Guid.NewGuid(), new RoleUpsertRequest { Name = "missing" });
        var delete = await harness.Topology.DeleteRoleAsync(zulu.Value!.Id);

        Assert.Equal(PermissionTopologyCommandStatus.Success, alpha.Status);
        Assert.Equal(PermissionTopologyCommandStatus.NotFound, missingUpdate.Status);
        Assert.Equal(PermissionTopologyCommandStatus.Success, delete.Status);
        Assert.Null(await harness.RoleManager.FindByIdAsync(zulu.Value.Id.ToString()));
        Assert.Equal(["alpha"], (await harness.Topology.GetAllRolesAsync()).Select(static role => role.Name));
    }

    [Fact]
    public async Task SyncRolePermissionsAsync_WhenPermissionSetsChange_AddsRemovesAndRejectsInvalidPermissionsWithoutMutation()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        await harness.Topology.UpsertResourceAsync("api", new ProtectedResourceUpsertRequest());
        await harness.Topology.UpsertPermissionAsync("api", "roles.read", new PermissionDefinitionUpsertRequest());
        await harness.Topology.UpsertPermissionAsync("api", "roles.write", new PermissionDefinitionUpsertRequest());
        var role = await harness.CreateRoleAsync("operators");

        var seed = await harness.Topology.SyncRolePermissionsAsync(role.Id, ["roles.read", "roles.write"]);
        var shrink = await harness.Topology.SyncRolePermissionsAsync(role.Id, ["roles.read"]);
        var invalid = await harness.Topology.SyncRolePermissionsAsync(role.Id, ["missing.permission"]);

        Assert.Equal(PermissionTopologyCommandStatus.Success, seed.Status);
        Assert.Equal(["roles.read", "roles.write"], seed.Value);
        Assert.Equal(PermissionTopologyCommandStatus.Success, shrink.Status);
        Assert.Equal(["roles.read"], shrink.Value);
        Assert.Equal(PermissionTopologyCommandStatus.ValidationFailed, invalid.Status);
        Assert.Equal(["roles.read"], await harness.Topology.GetRolePermissionsAsync(role.Id));
    }

    [Fact]
    public async Task InitializeDefaultsAsync_WhenRoleIsEnsureOnly_PreservesManualAssignments()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        var role = await harness.CreateRoleAsync("operators");
        await harness.RoleManager.AddClaimAsync(role, new Claim(PermissionConstants.ClaimType, "manual.permission"));

        await harness.Topology.InitializeDefaultsAsync([], ["operators"], new Dictionary<string, string[]>());

        Assert.Equal(["manual.permission"], await harness.Topology.GetRolePermissionsAsync(role.Id));
    }

    [Fact]
    public async Task ResolveRolePermissionsAsync_WhenRolesOverlap_ReturnsCanonicalUnionInOneTopologyOperation()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        var alpha = await harness.CreateRoleAsync("alpha");
        var beta = await harness.CreateRoleAsync("beta");
        await harness.RoleManager.AddClaimAsync(alpha, new Claim("Permission", "ORDERS.READ"));
        await harness.RoleManager.AddClaimAsync(beta, new Claim(PermissionConstants.ClaimType, "orders.read"));
        await harness.RoleManager.AddClaimAsync(beta, new Claim(PermissionConstants.ClaimType, "orders.write"));

        var permissions = await harness.Topology.ResolveRolePermissionsAsync(["ALPHA", "beta"]);

        Assert.Equal(["orders.read", "orders.write"], permissions);
    }

    [Fact]
    public async Task SyncRolePermissionsAsync_WhenRoleMissingAndPermissionInvalid_ReturnsNotFound()
    {
        await using var harness = await SqliteHarness.CreateAsync();

        var result = await harness.Topology.SyncRolePermissionsAsync(Guid.NewGuid(), ["missing.permission"]);

        Assert.Equal(PermissionTopologyCommandStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task SyncRolePermissionsAsync_WhenIdentityValidationFails_DoesNotLeakClaimIntoLaterSave()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        await harness.Topology.UpsertResourceAsync("api", new ProtectedResourceUpsertRequest());
        await harness.Topology.UpsertPermissionAsync("api", "roles.read", new PermissionDefinitionUpsertRequest());
        var invalidRole = new BzsRole { Id = Guid.NewGuid() };
        harness.DbContext.Roles.Add(invalidRole);
        await harness.DbContext.SaveChangesAsync();

        var result = await harness.Topology.SyncRolePermissionsAsync(invalidRole.Id, ["roles.read"]);
        await harness.Topology.UpsertResourceAsync("unrelated", new ProtectedResourceUpsertRequest());

        Assert.Equal(PermissionTopologyCommandStatus.ValidationFailed, result.Status);
        Assert.False(await harness.DbContext.RoleClaims.AsNoTracking().AnyAsync(claim => claim.RoleId == invalidRole.Id));
    }

    private sealed class SqliteHarness : IAsyncDisposable
    {
        private SqliteHarness(ServiceProvider provider, IServiceScope scope, SqliteConnection connection)
        {
            Provider = provider;
            Scope = scope;
            Connection = connection;
            DbContext = scope.ServiceProvider.GetRequiredService<IdpDbContext>();
            Topology = scope.ServiceProvider.GetRequiredService<IPermissionTopology>();
            RoleManager = scope.ServiceProvider.GetRequiredService<RoleManager<BzsRole>>();
        }

        public ServiceProvider Provider { get; }
        public IServiceScope Scope { get; }
        public SqliteConnection Connection { get; }
        public IdpDbContext DbContext { get; }
        public IPermissionTopology Topology { get; }
        public RoleManager<BzsRole> RoleManager { get; }

        public static async Task<SqliteHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddMemoryCache();
            services.AddDbContext<IdpDbContext>(options => options.UseSqlite(connection));
            services.AddIdentityCore<BzsUser>().AddRoles<BzsRole>().AddEntityFrameworkStores<IdpDbContext>();
            services.AddScoped<RoleManagementPolicy>();
            services.AddScoped<IPermissionTopology, PermissionTopologyService>();
            var provider = services.BuildServiceProvider();
            var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IdpDbContext>().Database.EnsureCreatedAsync();
            return new SqliteHarness(provider, scope, connection);
        }

        public async Task<BzsRole> CreateRoleAsync(string name)
        {
            var role = new BzsRole { Name = name };
            var result = await RoleManager.CreateAsync(role);
            Assert.True(result.Succeeded);
            return role;
        }

        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
