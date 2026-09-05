using System.Security.Claims;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.Services.Oidc;
using Microsoft.Extensions.Options;
using NSubstitute;
using OpenIddict.Abstractions;

namespace BzsOIDC.Idp.UnitTests.Services.Oidc;

public sealed class OidcPrincipalFactoryTests
{
    [Fact]
    public void FilterRequestedScopes_RemovesUnconfiguredScopesAndDuplicates()
    {
        var sut = CreateSut();

        var scopes = sut.FilterRequestedScopes([
            OpenIddictConstants.Scopes.OpenId,
            "api",
            "API",
            "unknown",
        ]);

        Assert.Equal([OpenIddictConstants.Scopes.OpenId, "api"], scopes);
    }

    [Fact]
    public async Task CreateUserPrincipalAsync_RewritesStandardClaimsToOpenIddictClaims()
    {
        var identityFactory = Substitute.For<IIdentityPrincipalFactory>();
        var identityReader = new IdentitySubjectReader();

        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        identity.AddClaim(new Claim(ClaimTypes.Name, "legacy-name"));
        identity.AddClaim(new Claim(ClaimTypes.Email, "octocat@users.noreply.github.com"));
        identity.AddClaim(new Claim("display_name", "The Octocat"));
        identity.AddClaim(new Claim(ClaimTypes.Role, "admin"));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, "admin"));
        identityFactory.CreateAsync(Guid.Parse("9db5d9dd-7c4d-4be4-bf08-cf9c6241a6cc"), Arg.Any<CancellationToken>())
            .Returns(new ClaimsPrincipal(identity));

        var sut = new OidcPrincipalFactory(identityFactory, identityReader, Options.Create(new IdentitySeedOptions()));

        var principal = await sut.CreateUserPrincipalAsync(Guid.Parse("9db5d9dd-7c4d-4be4-bf08-cf9c6241a6cc"));
        Assert.NotNull(principal);
        var resultIdentity = Assert.Single(principal.Identities);

        Assert.Equal("user-1", principal.GetClaim(OpenIddictConstants.Claims.Subject));
        Assert.Equal("The Octocat", principal.GetClaim(OpenIddictConstants.Claims.Name));
        Assert.Equal("octocat@users.noreply.github.com", principal.GetClaim(OpenIddictConstants.Claims.Email));
        Assert.DoesNotContain(resultIdentity.Claims, static claim => claim.Type == ClaimTypes.Name);
        Assert.DoesNotContain(resultIdentity.Claims, static claim => claim.Type == ClaimTypes.Email);
        Assert.DoesNotContain(resultIdentity.Claims, static claim => claim.Type == ClaimTypes.Role);
        Assert.Equal(["admin"], resultIdentity.FindAll(OpenIddictConstants.Claims.Role)
            .Select(static claim => claim.Value)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    [Fact]
    public void CreateClientPrincipal_WhenDisplayNameProvided_UsesClientIdentityClaims()
    {
        var sut = CreateSut();

        var principal = sut.CreateClientPrincipal("machine-client", "Machine Client");

        Assert.Equal("machine-client", principal.GetClaim(OpenIddictConstants.Claims.Subject));
        Assert.Equal("Machine Client", principal.GetClaim(OpenIddictConstants.Claims.Name));
    }

    [Fact]
    public async Task CreateUserPrincipalAsync_WhenDisplayNameMissing_FallsBackToUserName()
    {
        var identityFactory = Substitute.For<IIdentityPrincipalFactory>();
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));
        identity.AddClaim(new Claim(ClaimTypes.Name, "octocat"));
        identityFactory.CreateAsync(Guid.Parse("9db5d9dd-7c4d-4be4-bf08-cf9c6241a6cc"), Arg.Any<CancellationToken>())
            .Returns(new ClaimsPrincipal(identity));

        var sut = new OidcPrincipalFactory(identityFactory, new IdentitySubjectReader(), Options.Create(new IdentitySeedOptions()));

        var principal = await sut.CreateUserPrincipalAsync(Guid.Parse("9db5d9dd-7c4d-4be4-bf08-cf9c6241a6cc"));

        Assert.NotNull(principal);
        Assert.Equal("octocat", principal.GetClaim(OpenIddictConstants.Claims.Name));
    }

    private static OidcPrincipalFactory CreateSut()
    {
        return new OidcPrincipalFactory(
            Substitute.For<IIdentityPrincipalFactory>(),
            Substitute.For<IIdentitySubjectReader>(),
            Options.Create(new IdentitySeedOptions()));
    }
}

