using System.Security.Claims;
using BzsOIDC.Contracts;
using BzsOIDC.Idp.Controllers;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace BzsOIDC.Idp.UnitTests.Controllers;

public sealed class SessionControllerTests
{
    [Fact]
    public async Task GetSession_WhenAnonymous_ReturnsEmptySummaryWithoutReadingIdentity()
    {
        var sessionReader = Substitute.For<IIdentitySessionReader>();
        var sut = CreateSut(sessionReader, new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await sut.GetSession(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<SessionSummary>(ok.Value);
        Assert.False(summary.IsAuthenticated);
        await sessionReader.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task GetSession_WhenAuthenticated_ReturnsIdentityClaimsAndPermissions()
    {
        var userId = Guid.NewGuid().ToString();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, "operator"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("role", "admin"),
            new Claim(PermissionConstants.ClaimType, "users.read"),
            new Claim(PermissionConstants.ClaimType, "users.read"),
        ], "test"));
        var sessionReader = Substitute.For<IIdentitySessionReader>();
        sessionReader.ReadAsync(principal, Arg.Any<CancellationToken>())
            .Returns(new IdentitySession(userId, "operator", "Operator"));
        var sut = CreateSut(sessionReader, principal);

        var result = await sut.GetSession(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var summary = Assert.IsType<SessionSummary>(ok.Value);
        Assert.True(summary.IsAuthenticated);
        Assert.Equal(userId, summary.UserId);
        Assert.Equal("operator", summary.UserName);
        Assert.Equal("Operator", summary.DisplayName);
        Assert.Equal(["Admin"], summary.Roles);
        Assert.Equal(["users.read"], summary.Permissions);
        await sessionReader.Received(1).ReadAsync(principal, Arg.Any<CancellationToken>());
    }

    private static SessionController CreateSut(IIdentitySessionReader sessionReader, ClaimsPrincipal principal)
    {
        var sut = new SessionController(Substitute.For<IAntiforgery>(), sessionReader)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = principal,
                },
            },
        };

        return sut;
    }
}
