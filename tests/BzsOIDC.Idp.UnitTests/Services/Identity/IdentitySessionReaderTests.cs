using System.Security.Claims;
using BzsOIDC.Idp.Models;
using BzsOIDC.Idp.Services.Identity;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Services.Identity;

public sealed class IdentitySessionReaderTests
{
    [Fact]
    public async Task ReadAsync_WhenUserIdMatchesIdentityUser_UsesPersistedDisplayName()
    {
        var userId = Guid.NewGuid();
        var users = Substitute.For<IUserService>();
        users.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(BzsUser.CreateExternal("operator", null, "Operator Display"));
        var sut = new IdentitySessionReader(users);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, "operator"),
        ], "test"));

        var result = await sut.ReadAsync(principal, CancellationToken.None);

        Assert.Equal(userId.ToString(), result.UserId);
        Assert.Equal("operator", result.UserName);
        Assert.Equal("Operator Display", result.DisplayName);
    }

    [Fact]
    public async Task ReadAsync_WhenUserIdIsNotGuid_UsesClaimsWithoutLookup()
    {
        var users = Substitute.For<IUserService>();
        var sut = new IdentitySessionReader(users);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "external-user"),
            new Claim(ClaimTypes.Name, "operator"),
            new Claim("display_name", "Operator Display"),
        ], "test"));

        var result = await sut.ReadAsync(principal, CancellationToken.None);

        Assert.Equal("external-user", result.UserId);
        Assert.Equal("operator", result.UserName);
        Assert.Equal("Operator Display", result.DisplayName);
        await users.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }
}
