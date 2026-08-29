using BzsOIDC.Idp.Services.Oidc;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BzsOIDC.Idp.Controllers;

[ApiController]
public sealed class OidcScopesController(IOidcScopeService oidcScopeService, ILogger<OidcScopesController>? logger = null) : ControllerBase
{
    [HttpGet("~/api/oidc/scopes")]
    [PermissionAuthorize(PermissionConstants.ScopesRead)]
    public async Task<ActionResult<OidcScopeListResponse>> GetAll([FromQuery] OidcScopeListQuery query, CancellationToken cancellationToken)
    {
        var result = await oidcScopeService.ListAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("~/api/oidc/scopes/{name}")]
    [PermissionAuthorize(PermissionConstants.ScopesRead)]
    public async Task<ActionResult<OidcScopeResponse>> GetByName(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(name), "Scope name is required.");
            return ValidationProblem(ModelState);
        }

        var scope = await oidcScopeService.GetByNameAsync(name, cancellationToken);
        if (scope is null) return NotFound();
        Response.Headers[HeaderNames.ETag] = scope.ETag;
        return Ok(scope);
    }

    [HttpPost("~/api/oidc/scopes")]
    [PermissionAuthorize(PermissionConstants.ScopesWrite)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<OidcScopeResponse>> Create([FromBody] OidcScopeUpsertRequest request, CancellationToken cancellationToken)
    {
        var result = await oidcScopeService.RegisterAsync(request, cancellationToken);
        if (result.Status == OidcScopeCommandStatus.ValidationFailed)
        {
            return ValidationProblem(CreateValidationProblem(result.Errors));
        }

        if (result.Status == OidcScopeCommandStatus.Conflict)
        {
            return Conflict(result.Errors[0]);
        }

        var payload = result.Value!;
        Response.Headers[HeaderNames.ETag] = payload.ETag;
        logger?.LogInformation("Scope administration audit: created scope {ScopeName}.", payload.Name);
        return CreatedAtAction(nameof(GetByName), new { name = payload.Name }, payload);
    }

    [HttpPut("~/api/oidc/scopes/{name}")]
    [PermissionAuthorize(PermissionConstants.ScopesWrite)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<OidcScopeResponse>> Update(
        string name,
        [FromBody] OidcScopeUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (ControllerContext.HttpContext is null)
        {
            var commandResult = await oidcScopeService.UpdateAsync(name, request, cancellationToken);
            return commandResult.Status == OidcScopeCommandStatus.NotFound ? NotFound() : Ok(commandResult.Value);
        }
        var current = await oidcScopeService.GetByNameAsync(name, cancellationToken);
        if (current is null) return NotFound();
        if (!Request.Headers.TryGetValue(HeaderNames.IfMatch, out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch))
            return StatusCode(StatusCodes.Status428PreconditionRequired);
        if (!string.Equals(ifMatch.ToString(), current.ETag, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status412PreconditionFailed);

        var result = await oidcScopeService.UpdateAsync(name, request, cancellationToken);
        if (result.Status == OidcScopeCommandStatus.ValidationFailed)
        {
            return ValidationProblem(CreateValidationProblem(result.Errors));
        }

        if (result.Status == OidcScopeCommandStatus.NotFound)
        {
            return NotFound();
        }

        Response.Headers[HeaderNames.ETag] = result.Value?.ETag ?? string.Empty;
        logger?.LogInformation("Scope administration audit: updated scope {ScopeName}.", name);
        return Ok(result.Value);
    }

    [HttpDelete("~/api/oidc/scopes/{name}")]
    [PermissionAuthorize(PermissionConstants.ScopesWrite)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string name, CancellationToken cancellationToken)
    {
        if (ControllerContext.HttpContext is null)
        {
            return await oidcScopeService.DeleteAsync(name, cancellationToken) ? NoContent() : NotFound();
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(nameof(name), "Scope name is required.");
            return ValidationProblem(ModelState);
        }

        var current = await oidcScopeService.GetByNameAsync(name, cancellationToken);
        if (current is null) return NotFound();
        if (current.IsReserved) return Conflict("Reserved scopes cannot be deleted.");
        if (!Request.Headers.TryGetValue(HeaderNames.IfMatch, out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch))
            return StatusCode(StatusCodes.Status428PreconditionRequired);
        if (!string.Equals(ifMatch.ToString(), current.ETag, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        var deleted = await oidcScopeService.DeleteAsync(name, cancellationToken);
        if (deleted) logger?.LogInformation("Scope administration audit: deleted scope {ScopeName}.", name);
        return deleted ? NoContent() : NotFound();
    }

    private ValidationProblemDetails CreateValidationProblem(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(nameof(OidcScopeUpsertRequest), error);
        }

        return new ValidationProblemDetails(ModelState);
    }
}
