using System.Security.Claims;
using BzsOIDC.Idp.Services.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using SharedPermissionConstants = BzsOIDC.Shared.Infrastructure.Authorization.PermissionConstants;

namespace BzsOIDC.Idp.Services.Oidc;

internal sealed class PermissionClaimDestinationsHandler(IPermissionTopology permissionTopology)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>
{
    /// <summary>
    /// 执行HandleAsync。
    /// </summary>
    /// <param name="context">参数context。</param>
    /// <returns>执行结果。</returns>
    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessSignInContext context)
    {
        if (context.Principal is null)
        {
            return;
        }

        await ApplyDestinationsAsync(context.Principal, permissionTopology, context.CancellationToken);
    }

    /// <summary>
    /// 为 principal 应用 claims destinations。
    /// </summary>
    /// <param name="principal">参数principal。</param>
    /// <param name="permissionTopology">权限拓扑。</param>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    /// <remarks>
    /// 该方法既被 OpenIddict 的 sign-in 事件调用，也被 ConnectController 在 SignIn 前直接调用，
    /// 用于确保签发点与事件链路共享同一套 destinations 规则。
    /// </remarks>
    internal static async Task ApplyDestinationsAsync(
        ClaimsPrincipal principal,
        IPermissionTopology permissionTopology,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(permissionTopology);

        var permissionValues = principal
            .FindAll(SharedPermissionConstants.ClaimType)
            .Select(static claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var permissionScopes = await permissionTopology.ResolveReleaseScopesAsync(permissionValues, cancellationToken);
        var grantedScopes = principal.GetScopes().ToHashSet(StringComparer.OrdinalIgnoreCase);

        principal.SetDestinations(claim =>
        {
            if (string.Equals(claim.Type, SharedPermissionConstants.ClaimType, StringComparison.OrdinalIgnoreCase))
            {
                if (ShouldEmitPermissionClaim(claim.Value, grantedScopes, permissionScopes))
                {
                    return [OpenIddictConstants.Destinations.AccessToken];
                }

                return [];
            }

            if (string.Equals(claim.Type, OpenIddictConstants.Claims.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(claim.Type, ClaimTypes.Name, StringComparison.OrdinalIgnoreCase))
            {
                return grantedScopes.Contains(OpenIddictConstants.Scopes.Profile)
                    ? [OpenIddictConstants.Destinations.IdentityToken]
                    : [];
            }

            if (string.Equals(claim.Type, OpenIddictConstants.Claims.Email, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(claim.Type, ClaimTypes.Email, StringComparison.OrdinalIgnoreCase))
            {
                return grantedScopes.Contains(OpenIddictConstants.Scopes.Email)
                    ? [OpenIddictConstants.Destinations.IdentityToken]
                    : [];
            }

            if (string.Equals(claim.Type, OpenIddictConstants.Claims.Role, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(claim.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase))
            {
                return grantedScopes.Contains(OpenIddictConstants.Scopes.Roles)
                    ? [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken]
                    : [];
            }

            if (string.Equals(claim.Type, OpenIddictConstants.Claims.Subject, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(claim.Type, ClaimTypes.NameIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return [OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken];
            }

            return [];
        });
    }

    /// <summary>
    /// 执行ShouldEmitPermissionClaim。
    /// </summary>
    /// <param name="permission">参数permission。</param>
    /// <param name="grantedScopes">参数grantedScopes。</param>
    /// <param name="permissionScopes">参数permissionScopes。</param>
    /// <returns>执行结果。</returns>
    private static bool ShouldEmitPermissionClaim(
        string permission,
        ISet<string> grantedScopes,
        IReadOnlyDictionary<string, string[]> permissionScopes)
    {
        if (!permissionScopes.TryGetValue(permission, out var requiredScopes) || requiredScopes.Length == 0)
        {
            return false;
        }

        return requiredScopes.Any(grantedScopes.Contains);
    }
}
