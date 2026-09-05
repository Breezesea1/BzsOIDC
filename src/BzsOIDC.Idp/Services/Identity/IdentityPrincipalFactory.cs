using System.Security.Claims;
using BzsOIDC.Idp.Models;
using Microsoft.AspNetCore.Identity;

namespace BzsOIDC.Idp.Services.Identity;

/// <summary>
/// Loads an application identity by its stable identifier and creates its claims principal.
/// </summary>
public interface IIdentityPrincipalFactory
{
    Task<ClaimsPrincipal?> CreateAsync(Guid userId, CancellationToken cancellationToken = default);
}

internal sealed class IdentityPrincipalFactory(
    UserManager<BzsUser> userManager,
    SignInManager<BzsUser> signInManager) : IIdentityPrincipalFactory
{
    public async Task<ClaimsPrincipal?> CreateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !await signInManager.CanSignInAsync(user))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            if (principal.Identity is ClaimsIdentity identity)
            {
                identity.AddClaim(new Claim("display_name", user.DisplayName));
            }
        }

        if (!string.IsNullOrWhiteSpace(user.Email) && principal.Identity is ClaimsIdentity emailIdentity)
        {
            emailIdentity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        }

        return principal;
    }
}
