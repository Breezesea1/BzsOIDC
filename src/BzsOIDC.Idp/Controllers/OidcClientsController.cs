using BzsOIDC.Idp.Services.Oidc;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BzsOIDC.Idp.Controllers;

[ApiController]
public sealed class OidcClientsController(IOidcClientService oidcClientService, ILogger<OidcClientsController>? logger = null) : ControllerBase
{
    /// <summary>
    /// 注册资源。
    /// </summary>
    /// <param name="request">参数request。</param>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    [HttpPost("~/connect/register")]
    [PermissionAuthorize(PermissionConstants.ClientsWrite)]
    public async Task<ActionResult<OidcClientRegistrationResponse>> Register(
        [FromBody] OidcClientUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var result = await oidcClientService.RegisterAsync(request, cancellationToken);
        if (result.Status == OidcClientCommandStatus.ValidationFailed)
        {
            return ValidationProblem(CreateValidationProblem(result.Errors));
        }

        if (result.Status == OidcClientCommandStatus.Conflict)
        {
            return Conflict(result.Errors[0]);
        }

        var payload = result.Value!;
        logger?.LogInformation("OIDC client administration audit: action create, clientId {ClientId}, result success", payload.ClientId);
        return CreatedAtAction(nameof(GetByClientId), new { clientId = payload.ClientId }, payload);
    }

    /// <summary>
    /// 注销资源。
    /// </summary>
    /// <param name="clientId">参数clientId。</param>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    [HttpDelete("~/connect/register/{clientId}")]
    [PermissionAuthorize(PermissionConstants.ClientsWrite)]
    public async Task<IActionResult> Unregister(string clientId, CancellationToken cancellationToken)
    {
        var deleted = await oidcClientService.DeleteAsync(clientId, cancellationToken);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>
    /// 获取数据。
    /// </summary>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    [HttpGet("~/api/oidc/clients")]
    [PermissionAuthorize(PermissionConstants.ClientsRead)]
    public async Task<ActionResult<object>> GetAll([FromQuery] OidcClientListQuery? query, CancellationToken cancellationToken)
    {
        if (query is not null && Request.Query.Count > 0)
            return Ok(await oidcClientService.ListAsync(query, cancellationToken));
        var list = await oidcClientService.GetAllAsync(cancellationToken);
        return Ok(list);
    }

    /// <summary>
    /// 获取数据。
    /// </summary>
    /// <param name="clientId">参数clientId。</param>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    [HttpGet("~/api/oidc/clients/{clientId}")]
    [PermissionAuthorize(PermissionConstants.ClientsRead)]
    public async Task<ActionResult<OidcClientResponse>> GetByClientId(string clientId, CancellationToken cancellationToken)
    {
        var application = await oidcClientService.GetByClientIdAsync(clientId, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        Response.Headers[HeaderNames.ETag] = application.ETag;
        return Ok(application);
    }

    /// <summary>
    /// 创建数据。
    /// </summary>
    /// <param name="request">参数request。</param>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    [HttpPost("~/api/oidc/clients")]
    [PermissionAuthorize(PermissionConstants.ClientsWrite)]
    [ValidateAntiForgeryToken]
    public Task<ActionResult<OidcClientRegistrationResponse>> Create(
        [FromBody] OidcClientUpsertRequest request,
        CancellationToken cancellationToken)
    {
        return Register(request, cancellationToken);
    }

    /// <summary>
    /// 更新数据。
    /// </summary>
    /// <param name="clientId">参数clientId。</param>
    /// <param name="request">参数request。</param>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    [HttpPut("~/api/oidc/clients/{clientId}")]
    [PermissionAuthorize(PermissionConstants.ClientsWrite)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<OidcClientResponse>> Update(
        string clientId,
        [FromBody] OidcClientUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (ControllerContext.HttpContext is null)
        {
            var commandResult = await oidcClientService.UpdateAsync(clientId, request, cancellationToken);
            return commandResult.Status == OidcClientCommandStatus.NotFound ? NotFound() : Ok(commandResult.Value);
        }
        if (!Request.Headers.TryGetValue(HeaderNames.IfMatch, out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch))
            return StatusCode(StatusCodes.Status428PreconditionRequired);
        var current = await oidcClientService.GetByClientIdAsync(clientId, cancellationToken);
        if (current is null) return NotFound();
        if (!string.Equals(ifMatch.ToString(), current.ETag, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        var result = await oidcClientService.UpdateAsync(clientId, request, cancellationToken);
        if (result.Status == OidcClientCommandStatus.ValidationFailed)
        {
            return ValidationProblem(CreateValidationProblem(result.Errors));
        }

        if (result.Status == OidcClientCommandStatus.NotFound)
        {
            return NotFound();
        }

        Response.Headers[HeaderNames.ETag] = result.Value?.ETag ?? string.Empty;
        logger?.LogInformation("OIDC client administration audit: action update, clientId {ClientId}, result success", clientId);
        return Ok(result.Value);
    }

    /// <summary>
    /// 删除数据。
    /// </summary>
    /// <param name="clientId">参数clientId。</param>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    [HttpDelete("~/api/oidc/clients/{clientId}")]
    [PermissionAuthorize(PermissionConstants.ClientsWrite)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string clientId, CancellationToken cancellationToken)
    {
        var current = await oidcClientService.GetByClientIdAsync(clientId, cancellationToken);
        if (current is null) return NotFound();
        if (!Request.Headers.TryGetValue(HeaderNames.IfMatch, out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch))
            return StatusCode(StatusCodes.Status428PreconditionRequired);
        if (!string.Equals(ifMatch.ToString(), current.ETag, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        var response = await Unregister(clientId, cancellationToken);
        logger?.LogInformation("OIDC client administration audit: action delete, clientId {ClientId}, result {Result}", clientId, response is NoContentResult ? "success" : "not_found");
        return response;
    }

    [HttpPost("~/api/oidc/clients/{clientId}/secret/rotate")]
    [PermissionAuthorize(PermissionConstants.ClientsWrite)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<OidcClientSecretResponse>> RotateSecret(string clientId, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue(HeaderNames.IfMatch, out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch))
            return StatusCode(StatusCodes.Status428PreconditionRequired);
        var current = await oidcClientService.GetByClientIdAsync(clientId, cancellationToken);
        if (current is null) return NotFound();
        if (!string.Equals(ifMatch.ToString(), current.ETag, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        var result = await oidcClientService.RotateSecretAsync(clientId, cancellationToken);
        if (result.Status == OidcClientCommandStatus.ValidationFailed)
            return ValidationProblem(CreateValidationProblem(result.Errors));
        if (result.Status == OidcClientCommandStatus.NotFound) return NotFound();
        logger?.LogInformation("OIDC client administration audit: action rotate_secret, clientId {ClientId}, result success", clientId);
        return Ok(result.Value);
    }

    /// <summary>
    /// 创建数据。
    /// </summary>
    /// <param name="errors">参数errors。</param>
    /// <returns>执行结果。</returns>
    private ValidationProblemDetails CreateValidationProblem(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(nameof(OidcClientUpsertRequest), error);
        }

        return new ValidationProblemDetails(ModelState);
    }
}
