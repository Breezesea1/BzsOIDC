using System.Security.Claims;
using BzsOIDC.Idp.Services.Identity;
using OpenIddict.Abstractions;
using SharedPermissionConstants = BzsOIDC.Shared.Infrastructure.Authorization.PermissionConstants;

namespace BzsOIDC.Idp.Services.Oidc;

public interface IOidcConnectService
{
    Task<OidcConnectUser?> CreateUserPrincipalAsync(
        ClaimsPrincipal cookiePrincipal,
        IReadOnlyCollection<string> scopes,
        IPermissionTopology permissionTopology,
        CancellationToken cancellationToken = default);

    Task<OidcConnectUser?> RefreshUserPrincipalAsync(
        string subject,
        IReadOnlyCollection<string> scopes,
        string? authorizationId,
        IPermissionTopology permissionTopology,
        CancellationToken cancellationToken = default);

    Task<ClaimsPrincipal?> CreateClientPrincipalAsync(
        string clientId,
        IReadOnlyCollection<string> scopes,
        IPermissionTopology permissionTopology,
        CancellationToken cancellationToken = default);

    Task<string> ResolveClientDisplayNameAsync(string? clientId, CancellationToken cancellationToken = default);

    Task<OidcUserInfo?> GetUserInfoAsync(ClaimsPrincipal tokenPrincipal, CancellationToken cancellationToken = default);
}

public sealed record OidcConnectUser(ClaimsPrincipal Principal, string Subject);

public sealed record OidcUserInfo(
    string Subject,
    string? Name,
    string? Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

internal sealed class OidcConnectService(
    IOpenIddictApplicationManager applicationManager,
    IOidcPrincipalFactory oidcPrincipalFactory,
    IIdentitySubjectReader identitySubjectReader,
    IUserService userService) : IOidcConnectService
{
    public async Task<OidcConnectUser?> CreateUserPrincipalAsync(
        ClaimsPrincipal cookiePrincipal,
        IReadOnlyCollection<string> scopes,
        IPermissionTopology permissionTopology,
        CancellationToken cancellationToken = default)
    {
        var identity = identitySubjectReader.Read(cookiePrincipal);
        if (identity is null || !Guid.TryParse(identity.Id, out var userId))
        {
            return null;
        }

        var principal = await oidcPrincipalFactory.CreateUserPrincipalAsync(userId, cancellationToken);
        if (principal is null)
        {
            return null;
        }

        principal.SetScopes(scopes);
        await PermissionClaimDestinationsHandler.ApplyDestinationsAsync(principal, permissionTopology, cancellationToken);
        return new OidcConnectUser(principal, principal.GetClaim(OpenIddictConstants.Claims.Subject) ?? identity.Id);
    }

    public async Task<OidcConnectUser?> RefreshUserPrincipalAsync(
        string subject,
        IReadOnlyCollection<string> scopes,
        string? authorizationId,
        IPermissionTopology permissionTopology,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(subject, out var userId))
        {
            return null;
        }

        var principal = await oidcPrincipalFactory.CreateUserPrincipalAsync(userId, cancellationToken);
        if (principal is null)
        {
            return null;
        }

        principal.SetScopes(scopes);
        if (!string.IsNullOrWhiteSpace(authorizationId))
        {
            principal.SetAuthorizationId(authorizationId);
        }

        await PermissionClaimDestinationsHandler.ApplyDestinationsAsync(principal, permissionTopology, cancellationToken);
        return new OidcConnectUser(principal, subject);
    }

    public async Task<ClaimsPrincipal?> CreateClientPrincipalAsync(
        string clientId,
        IReadOnlyCollection<string> scopes,
        IPermissionTopology permissionTopology,
        CancellationToken cancellationToken = default)
    {
        var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null)
        {
            return null;
        }

        var displayName = await applicationManager.GetDisplayNameAsync(application, cancellationToken);
        var principal = oidcPrincipalFactory.CreateClientPrincipal(clientId, displayName);
        principal.SetScopes(oidcPrincipalFactory.FilterRequestedScopes(scopes));
        await PermissionClaimDestinationsHandler.ApplyDestinationsAsync(principal, permissionTopology, cancellationToken);
        return principal;
    }

    public async Task<string> ResolveClientDisplayNameAsync(string? clientId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var application = await applicationManager.FindByClientIdAsync(clientId, cancellationToken);
            if (application is not null)
            {
                var displayName = await applicationManager.GetDisplayNameAsync(application, cancellationToken);
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    return displayName;
                }
            }
        }

        return clientId ?? "the client";
    }

    public async Task<OidcUserInfo?> GetUserInfoAsync(ClaimsPrincipal tokenPrincipal, CancellationToken cancellationToken = default)
    {
        var subject = tokenPrincipal.GetClaim(OpenIddictConstants.Claims.Subject);
        if (string.IsNullOrWhiteSpace(subject) || !Guid.TryParse(subject, out var userId))
        {
            return null;
        }

        var user = await userService.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var verifiedPrincipal = await oidcPrincipalFactory.CreateUserPrincipalAsync(userId, cancellationToken);
        if (verifiedPrincipal is null)
        {
            return null;
        }

        var roles = tokenPrincipal.Claims
            .Where(static claim => string.Equals(claim.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(claim.Type, OpenIddictConstants.Claims.Role, StringComparison.OrdinalIgnoreCase))
            .Select(static claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var permissions = tokenPrincipal.FindAll(SharedPermissionConstants.ClaimType)
            .Select(static claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OidcUserInfo(
            subject,
            tokenPrincipal.GetClaim(OpenIddictConstants.Claims.Name) ?? tokenPrincipal.Identity?.Name ??
                verifiedPrincipal.GetClaim(OpenIddictConstants.Claims.Name) ?? verifiedPrincipal.Identity?.Name ??
                user.DisplayName ?? user.UserName,
            tokenPrincipal.FindFirstValue(ClaimTypes.Email) ?? verifiedPrincipal.FindFirstValue(ClaimTypes.Email) ?? user.Email,
            roles,
            permissions);
    }
}
