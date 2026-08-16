using BzsOIDC.Idp.Infra;
using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Services.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BzsOIDC.Idp.UnitTests.Services.Identity;

public sealed class UserAdministrationTests
{
    [Fact]
    public async Task GetUsersAsync_WhenUsersHaveMixedMembership_ProjectsAdminMembershipInUserNameOrder()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        var admin = await harness.CreateUserAsync("zulu", "Password1!");
        await harness.CreateUserAsync("alpha", "Password1!");
        await harness.CreateRoleAsync("operators");
        await harness.UserManager.AddToRoleAsync(admin, IdentitySeedConstants.AdminRoleName);
        await harness.UserManager.AddToRoleAsync(admin, "operators");

        var users = await harness.Administration.GetUsersAsync();

        Assert.Equal(["alpha", "zulu"], users.Select(static user => user.UserName));
        Assert.False(users[0].IsAdmin);
        Assert.True(users[1].IsAdmin);
    }

    [Fact]
    public async Task CreateAsync_WhenIdentityCreateFails_ShortCircuitsBeforeRoleSynchronization()
    {
        await using var harness = await SqliteHarness.CreateAsync();

        var result = await harness.Administration.CreateAsync(
            new CreateUserAdministrationRequest("new-user", "short", null, IsAdmin: true));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, static error => error.Code == UserAdministrationErrorCodes.PasswordTooShort);
        Assert.Null(await harness.UserManager.FindByNameAsync("new-user"));
    }

    [Fact]
    public async Task CreateAsync_WhenSuccessful_CreatesReloadsThenSynchronizesAdminRole()
    {
        await using var harness = await SqliteHarness.CreateAsync();

        var result = await harness.Administration.CreateAsync(
            new CreateUserAdministrationRequest("new-admin", "Password1!", "admin@example.com", IsAdmin: true));

        Assert.True(result.Succeeded);
        var created = await harness.UserManager.FindByNameAsync("new-admin");
        Assert.NotNull(created);
        Assert.Equal("admin@example.com", created.Email);
        Assert.True(await harness.UserManager.IsInRoleAsync(created, IdentitySeedConstants.AdminRoleName));
    }

    [Fact]
    public async Task UpdateAsync_WhenPasswordFails_PersistsProfileAndShortCircuitsBeforeRoleSynchronization()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        var user = await harness.CreateUserAsync("existing", "Password1!");

        var result = await harness.Administration.UpdateAsync(
            user.Id,
            new UpdateUserAdministrationRequest("renamed", null, "short", IsAdmin: true),
            Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, static error => error.Code == UserAdministrationErrorCodes.PasswordTooShort);
        var updated = await harness.UserManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(updated);
        Assert.Equal("renamed", updated.UserName);
        Assert.False(await harness.UserManager.IsInRoleAsync(updated, IdentitySeedConstants.AdminRoleName));
    }

    [Fact]
    public async Task UpdateAsync_WhenAdminRoleIsUnchanged_SynchronizesOnlyAdminRole()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        var user = await harness.CreateUserAsync("operator", "Password1!");
        await harness.CreateRoleAsync("operators");
        await harness.UserManager.AddToRoleAsync(user, "operators");

        var addAdmin = await harness.Administration.UpdateAsync(
            user.Id,
            new UpdateUserAdministrationRequest("operator", null, null, IsAdmin: true),
            Guid.NewGuid());
        var removeAdmin = await harness.Administration.UpdateAsync(
            user.Id,
            new UpdateUserAdministrationRequest("operator", null, null, IsAdmin: false),
            Guid.NewGuid());

        Assert.True(addAdmin.Succeeded);
        Assert.True(removeAdmin.Succeeded);
        var roles = await harness.UserManager.GetRolesAsync(user);
        Assert.Equal(["operators"], roles);
    }

    [Fact]
    public async Task UpdateAsync_WhenDemotingCurrentSessionUser_ReturnsProtectedErrorAndKeepsAdminRole()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        var user = await harness.CreateUserAsync("current-admin", "Password1!");
        await harness.UserManager.AddToRoleAsync(user, IdentitySeedConstants.AdminRoleName);

        var result = await harness.Administration.UpdateAsync(
            user.Id,
            new UpdateUserAdministrationRequest("current-admin", null, null, IsAdmin: false),
            user.Id);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, static error => error.Code == UserAdministrationErrorCodes.CurrentSessionAdminDemotion);
        Assert.True(await harness.UserManager.IsInRoleAsync(user, IdentitySeedConstants.AdminRoleName));
    }

    [Fact]
    public async Task DeleteAsync_WhenCurrentAndOtherUsersAreTargeted_ProtectsCurrentUserAndDeletesOtherUsers()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        var currentUser = await harness.CreateUserAsync("current-admin", "Password1!");
        var otherUser = await harness.CreateUserAsync("other-user", "Password1!");

        var protectedResult = await harness.Administration.DeleteAsync(currentUser.Id, currentUser.Id);
        var deletedResult = await harness.Administration.DeleteAsync(otherUser.Id, currentUser.Id);

        Assert.False(protectedResult.Succeeded);
        Assert.Contains(protectedResult.Errors, static error => error.Code == UserAdministrationErrorCodes.CurrentSessionDeletion);
        Assert.True(deletedResult.Succeeded);
        Assert.NotNull(await harness.UserManager.FindByIdAsync(currentUser.Id.ToString()));
        Assert.Null(await harness.UserManager.FindByIdAsync(otherUser.Id.ToString()));
    }

    [Fact]
    public async Task CreateAsync_WhenIdentityDescriptionContainsQuotedText_UsesStableCodeAndRequestArguments()
    {
        await using var harness = await SqliteHarness.CreateAsync(new AlternateIdentityErrorDescriber());
        await harness.CreateUserAsync("existing", "Password1!");

        var result = await harness.Administration.CreateAsync(
            new CreateUserAdministrationRequest("existing", "Password1!", null, IsAdmin: false));

        var error = Assert.Single(result.Errors);
        Assert.Equal(UserAdministrationErrorCodes.DuplicateUserName, error.Code);
        Assert.Equal(["existing"], error.Arguments);
    }

    private sealed class SqliteHarness : IAsyncDisposable
    {
        private SqliteHarness(ServiceProvider provider, IServiceScope scope, SqliteConnection connection)
        {
            Provider = provider;
            Scope = scope;
            Connection = connection;
            UserManager = scope.ServiceProvider.GetRequiredService<UserManager<BzsUser>>();
            RoleManager = scope.ServiceProvider.GetRequiredService<RoleManager<BzsRole>>();
            Administration = scope.ServiceProvider.GetRequiredService<IUserAdministration>();
        }

        private ServiceProvider Provider { get; }
        private IServiceScope Scope { get; }
        private SqliteConnection Connection { get; }
        public UserManager<BzsUser> UserManager { get; }
        private RoleManager<BzsRole> RoleManager { get; }
        public IUserAdministration Administration { get; }

        public static async Task<SqliteHarness> CreateAsync(IdentityErrorDescriber? errorDescriber = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddDbContext<IdpDbContext>(options => options.UseSqlite(connection));
            services.AddIdentityCore<BzsUser>(options =>
                {
                    options.Password.RequireDigit = true;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireNonAlphanumeric = true;
                    options.Password.RequiredLength = 8;
                })
                .AddRoles<BzsRole>()
                .AddEntityFrameworkStores<IdpDbContext>()
                .AddDefaultTokenProviders();
            if (errorDescriber is not null)
            {
                services.AddSingleton<IdentityErrorDescriber>(errorDescriber);
            }
            services.AddScoped<IUserAdministration, UserAdministration>();
            var provider = services.BuildServiceProvider();
            var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IdpDbContext>().Database.EnsureCreatedAsync();
            var roleResult = await scope.ServiceProvider.GetRequiredService<RoleManager<BzsRole>>().CreateAsync(
                new BzsRole { Name = IdentitySeedConstants.AdminRoleName });
            Assert.True(roleResult.Succeeded);
            return new SqliteHarness(provider, scope, connection);
        }

        public async Task<BzsUser> CreateUserAsync(string userName, string password)
        {
            var user = new BzsUser { UserName = userName };
            var result = await UserManager.CreateAsync(user, password);
            Assert.True(result.Succeeded);
            return user;
        }

        public async Task CreateRoleAsync(string roleName)
        {
            var result = await RoleManager.CreateAsync(new BzsRole { Name = roleName });
            Assert.True(result.Succeeded);
        }

        public async ValueTask DisposeAsync()
        {
            Scope.Dispose();
            await Provider.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class AlternateIdentityErrorDescriber : IdentityErrorDescriber
    {
        public override IdentityError DuplicateUserName(string userName)
        {
            return new IdentityError
            {
                Code = nameof(DuplicateUserName),
                Description = "Provider text says 'first' and 'second'.",
            };
        }
    }
}
