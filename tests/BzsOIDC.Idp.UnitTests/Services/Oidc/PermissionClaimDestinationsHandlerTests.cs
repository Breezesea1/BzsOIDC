using System.Security.Claims;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.Services.Oidc;
using BzsOIDC.Shared.Infrastructure.Authorization;
using NSubstitute;
using OpenIddict.Abstractions;

namespace BzsOIDC.Idp.UnitTests.Services.Oidc;

public sealed class PermissionClaimDestinationsHandlerTests
{
    [Fact]
    public async Task ApplyDestinationsAsync_WhenIdentityScopesGranted_AddsIdentityTokenDestinations()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.ResolveReleaseScopesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));

        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, "user-1"));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, "alice"));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, "alice@example.com"));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes([
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
        ]);

        await PermissionClaimDestinationsHandler.ApplyDestinationsAsync(principal, service, CancellationToken.None);

        Assert.Equal(
            [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken],
            identity.FindFirst(OpenIddictConstants.Claims.Subject)!.GetDestinations().ToArray());
        Assert.Equal(
            [OpenIddictConstants.Destinations.IdentityToken],
            identity.FindFirst(OpenIddictConstants.Claims.Name)!.GetDestinations().ToArray());
        Assert.Equal(
            [OpenIddictConstants.Destinations.IdentityToken],
            identity.FindFirst(OpenIddictConstants.Claims.Email)!.GetDestinations().ToArray());
    }

    [Fact]
    public async Task ApplyDestinationsAsync_WhenProfileAndEmailScopesMissing_DoesNotEmitNameOrEmailClaims()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.ResolveReleaseScopesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));

        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, "alice"));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Email, "alice@example.com"));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes([OpenIddictConstants.Scopes.OpenId]);

        await PermissionClaimDestinationsHandler.ApplyDestinationsAsync(principal, service, CancellationToken.None);

        Assert.Empty(identity.FindFirst(OpenIddictConstants.Claims.Name)!.GetDestinations());
        Assert.Empty(identity.FindFirst(OpenIddictConstants.Claims.Email)!.GetDestinations());
    }

    [Fact]
    public async Task ApplyDestinationsAsync_WhenRoleScopeMissing_DoesNotEmitRoleClaim()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.ResolveReleaseScopesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));

        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, "admin"));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes([OpenIddictConstants.Scopes.OpenId]);

        await PermissionClaimDestinationsHandler.ApplyDestinationsAsync(principal, service, CancellationToken.None);

        Assert.Empty(identity.FindFirst(OpenIddictConstants.Claims.Role)!.GetDestinations());
    }

    [Fact]
    public async Task ApplyDestinationsAsync_WhenTopologyReleaseScopeMatches_EmitsPermissionToAccessTokenOnly()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.ResolveReleaseScopesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [PermissionConstants.UsersWrite] = [PermissionConstants.ScopeApi],
            });

        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(PermissionConstants.ClaimType, PermissionConstants.UsersWrite));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes([PermissionConstants.ScopeApi]);

        await PermissionClaimDestinationsHandler.ApplyDestinationsAsync(principal, service, CancellationToken.None);

        Assert.Equal(
            [OpenIddictConstants.Destinations.AccessToken],
            identity.FindFirst(PermissionConstants.ClaimType)!.GetDestinations().ToArray());
    }

    [Fact]
    public async Task ApplyDestinationsAsync_WhenTopologyReleaseScopeMissing_DoesNotEmitPermissionClaim()
    {
        var service = Substitute.For<IPermissionTopology>();
        service.ResolveReleaseScopesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [PermissionConstants.UsersWrite] = [PermissionConstants.ScopeApi],
            });

        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(PermissionConstants.ClaimType, PermissionConstants.UsersWrite));

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes([OpenIddictConstants.Scopes.OpenId]);

        await PermissionClaimDestinationsHandler.ApplyDestinationsAsync(principal, service, CancellationToken.None);

        Assert.Empty(identity.FindFirst(PermissionConstants.ClaimType)!.GetDestinations());
    }
}

