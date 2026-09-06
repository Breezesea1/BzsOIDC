using System.Security.Claims;
using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Services.Identity;

public sealed class PermissionClaimsPrincipalFactoryTests
{
    [Fact]
    public async Task CreateAsync_WhenRolesContainDuplicates_ResolvesRolesOnceAndPermissionsThroughTopology()
    {
        var userManager = new PrincipalUserManager(["alpha", "ALPHA", "beta"]);
        var topology = Substitute.For<IPermissionTopology>();
        topology.ResolveRolePermissionsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(["orders.read", "orders.write"]);
        var sut = new PermissionClaimsPrincipalFactory(
            userManager,
            Options.Create(new IdentityOptions()),
            topology);
        var user = new BzsUser
        {
            Id = Guid.NewGuid(),
            UserName = "operator",
        };

        var principal = await sut.CreateAsync(user);

        Assert.Equal(1, userManager.GetRolesCallCount);
        Assert.Equal(["alpha", "beta"], principal.FindAll(ClaimTypes.Role).Select(static claim => claim.Value));
        Assert.Equal(
            ["orders.read", "orders.write"],
            principal.FindAll(PermissionConstants.ClaimType).Select(static claim => claim.Value));
        await topology.Received(1).ResolveRolePermissionsAsync(
            Arg.Is<IEnumerable<string>>(roles => roles.SequenceEqual(new[] { "alpha", "ALPHA", "beta" })),
            Arg.Any<CancellationToken>());
    }

    private sealed class PrincipalUserManager(IReadOnlyList<string> roles)
        : UserManager<BzsUser>(
            Substitute.For<IUserStore<BzsUser>>(),
            Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
            new PasswordHasher<BzsUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<BzsUser>>>())
    {
        public int GetRolesCallCount { get; private set; }

        public override Task<string?> GetUserNameAsync(BzsUser user)
        {
            return Task.FromResult(user.UserName);
        }

        public override Task<IList<Claim>> GetClaimsAsync(BzsUser user)
        {
            return Task.FromResult<IList<Claim>>([]);
        }

        public override Task<IList<string>> GetRolesAsync(BzsUser user)
        {
            GetRolesCallCount++;
            return Task.FromResult<IList<string>>(roles.ToList());
        }
    }
}
