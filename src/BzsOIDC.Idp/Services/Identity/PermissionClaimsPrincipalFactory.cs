using System.Security.Claims;
using BzsOIDC.Idp.Models;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BzsOIDC.Idp.Services.Identity;

internal sealed class PermissionClaimsPrincipalFactory(
    UserManager<BzsUser> userManager,
    IOptions<IdentityOptions> optionsAccessor,
    IPermissionTopology permissionTopology)
    : UserClaimsPrincipalFactory<BzsUser>(userManager, optionsAccessor)
{
    /// <summary>
    /// 生成结果。
    /// </summary>
    /// <param name="user">参数user。</param>
    /// <returns>执行结果。</returns>
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(BzsUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        var roleNames = await UserManager.GetRolesAsync(user);
        if (roleNames.Count == 0)
        {
            return identity;
        }

        var existingPermissions = identity.Claims
            .Where(static c => string.Equals(c.Type, PermissionConstants.ClaimType, StringComparison.OrdinalIgnoreCase))
            .Select(static c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingRoles = identity.Claims
            .Where(claim => string.Equals(claim.Type, identity.RoleClaimType, StringComparison.OrdinalIgnoreCase))
            .Select(static claim => claim.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var roleName in roleNames)
        {
            if (existingRoles.Add(roleName))
            {
                identity.AddClaim(new Claim(identity.RoleClaimType, roleName));
            }
        }

        var rolePermissions = await permissionTopology.ResolveRolePermissionsAsync(roleNames);
        foreach (var permission in rolePermissions)
        {
            if (existingPermissions.Add(permission))
            {
                identity.AddClaim(new Claim(PermissionConstants.ClaimType, permission));
            }
        }

        return identity;
    }
}
