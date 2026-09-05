using System.Security.Claims;
using BzsOIDC.Idp.Services.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace BzsOIDC.Idp.Services.Oidc;

public interface IOidcPrincipalFactory
{
    IReadOnlyList<string> FilterRequestedScopes(IEnumerable<string> requestedScopes);
    Task<ClaimsPrincipal?> CreateUserPrincipalAsync(Guid userId, CancellationToken cancellationToken = default);
    ClaimsPrincipal CreateClientPrincipal(string clientId, string? displayName);
}

internal sealed class OidcPrincipalFactory(
    IIdentityPrincipalFactory identityPrincipalFactory,
    IIdentitySubjectReader identitySubjectReader,
    IOptions<IdentitySeedOptions> identityOptions) : IOidcPrincipalFactory
{
    public IReadOnlyList<string> FilterRequestedScopes(IEnumerable<string> requestedScopes)
    {
        var allowedScopes = identityOptions.Value.AdditionalScopes
            .Append(OpenIddictConstants.Scopes.OpenId)
            .Append(OpenIddictConstants.Scopes.Profile)
            .Append(OpenIddictConstants.Scopes.Email)
            .Append(OpenIddictConstants.Scopes.Roles)
            .Append(OpenIddictConstants.Scopes.OfflineAccess)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return requestedScopes
            .Where(scope => allowedScopes.Contains(scope))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<ClaimsPrincipal?> CreateUserPrincipalAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var principal = await identityPrincipalFactory.CreateAsync(userId, cancellationToken);
        if (principal is null)
        {
            return null;
        }

        var subject = identitySubjectReader.Read(principal);
        if (subject is null)
        {
            return null;
        }

        principal.SetClaim(OpenIddictConstants.Claims.Subject, subject.Id);

        var displayName = principal.FindFirstValue("display_name") ?? subject.UserName;

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            principal.SetClaim(OpenIddictConstants.Claims.Name, displayName);
        }

        var email = principal.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrWhiteSpace(email))
        {
            principal.SetClaim(OpenIddictConstants.Claims.Email, email);
        }

        var identity = principal.Identities.FirstOrDefault();
        if (identity is null)
        {
            return principal;
        }

        RemoveClaims(identity, ClaimTypes.Name, ClaimTypes.Email, ClaimTypes.Role, "display_name");

        var existingRoles = identity.FindAll(OpenIddictConstants.Claims.Role)
            .Select(static claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var roleName in subject.Roles)
        {
            if (existingRoles.Add(roleName))
            {
                identity.AddClaim(new Claim(OpenIddictConstants.Claims.Role, roleName));
            }
        }

        return principal;
    }

    public ClaimsPrincipal CreateClientPrincipal(string clientId, string? displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        identity.SetClaim(OpenIddictConstants.Claims.Subject, clientId);

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            identity.SetClaim(OpenIddictConstants.Claims.Name, displayName);
        }

        return new ClaimsPrincipal(identity);
    }

    private static void RemoveClaims(ClaimsIdentity identity, params string[] claimTypes)
    {
        foreach (var claim in identity.Claims
                     .Where(claim => claimTypes.Contains(claim.Type, StringComparer.OrdinalIgnoreCase))
                     .ToArray())
        {
            identity.RemoveClaim(claim);
        }
    }
}
