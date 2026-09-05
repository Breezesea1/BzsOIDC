using BzsOIDC.Contracts;
using BzsOIDC.Idp.Services.Oidc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;

namespace BzsOIDC.Idp.Controllers;

[ApiController]
[Authorize]
public sealed class OidcConsentController(
    IOidcConsentPreviewService consentPreviewService) : ControllerBase
{
    [HttpGet("~/api/oidc/consent-preview")]
    public async Task<ActionResult<OidcConsentPreviewResponse>> GetPreview(
        [FromQuery(Name = "request")] string? requestToken,
        CancellationToken cancellationToken)
    {
        var preview = await consentPreviewService.GetPreviewAsync(requestToken ?? string.Empty, cancellationToken);
        return preview is null
            ? BadRequest(new { error = OpenIddictConstants.Errors.InvalidRequest })
            : Ok(preview);
    }
}
