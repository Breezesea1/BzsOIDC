using System.Security.Claims;
using BzsOIDC.Contracts;
using BzsOIDC.Idp.Models;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BzsOIDC.Idp.Controllers;

[ApiController]
public sealed class SessionController(
    IAntiforgery antiforgery,
    UserManager<BzsUser> userManager) : ControllerBase
{
    [HttpGet("~/api/session")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<SessionSummary>> GetSession(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";

        if (User.Identity?.IsAuthenticated != true)
        {
            return Ok(new SessionSummary());
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = User.Identity.Name;
        var displayName = User.FindFirstValue("display_name") ?? userName;
        if (Guid.TryParse(userId, out var id))
        {
            var user = await userManager.FindByIdAsync(id.ToString());
            if (user is not null)
            {
                displayName = user.DisplayName;
                userName ??= user.UserName;
            }
        }

        var roles = User.Claims
            .Where(c => string.Equals(c.Type, ClaimTypes.Role, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var permissions = User.Claims
            .Where(c => string.Equals(c.Type, PermissionConstants.ClaimType, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(new SessionSummary
        {
            IsAuthenticated = true,
            UserId = userId,
            UserName = userName,
            DisplayName = displayName,
            Roles = roles,
            Permissions = permissions,
        });
    }

    [HttpGet("~/api/security/antiforgery")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<AntiforgeryTokenResponse> GetAntiforgeryToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";
        return Ok(new AntiforgeryTokenResponse
        {
            Token = tokens.RequestToken ?? string.Empty,
            HeaderName = tokens.HeaderName ?? "RequestVerificationToken",
            FormFieldName = tokens.FormFieldName ?? "__RequestVerificationToken",
        });
    }
}
