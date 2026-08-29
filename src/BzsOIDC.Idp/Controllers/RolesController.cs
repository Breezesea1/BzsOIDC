using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;

namespace BzsOIDC.Idp.Controllers;

[ApiController]
public sealed class RolesController(
    IPermissionTopology permissionTopology,
    ILogger<RolesController>? logger = null) : ControllerBase
{
    [NonAction]
    internal Task<ActionResult<object>> GetAll(CancellationToken cancellationToken)
        => GetAll(null, cancellationToken);

    [HttpGet("~/api/roles")]
    [PermissionAuthorize(PermissionConstants.RolesRead)]
    public async Task<ActionResult<object>> GetAll([FromQuery] RoleListQuery? query, CancellationToken cancellationToken = default)
    {
        if (query is null || (string.IsNullOrWhiteSpace(query.Search) && string.IsNullOrWhiteSpace(query.Sort) && query.Page == 1 && query.PageSize == 25 && !query.Descending))
        {
            return Ok(await permissionTopology.GetAllRolesAsync(cancellationToken));
        }

        return Ok(await permissionTopology.ListRolesAsync(query, cancellationToken));
    }

    [HttpGet("~/api/roles/{roleId:guid}")]
    [PermissionAuthorize(PermissionConstants.RolesRead)]
    public async Task<ActionResult<RoleResponse>> GetById(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await permissionTopology.GetRoleByIdAsync(roleId, cancellationToken);
        if (role is null) return NotFound();
        Response.Headers[HeaderNames.ETag] = role.ETag;
        return Ok(role);
    }

    [HttpPost("~/api/roles")]
    [PermissionAuthorize(PermissionConstants.RolesWrite)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<RoleResponse>> Create(
        [FromBody] RoleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var result = await permissionTopology.CreateRoleAsync(request, cancellationToken);
        if (result.Status == PermissionTopologyCommandStatus.Success)
        {
            var payload = result.Value!;
            if (ControllerContext.HttpContext is not null) Response.Headers[HeaderNames.ETag] = payload.ETag;
            logger?.LogInformation("Role administration audit: actor {Actor}, action create, resource {RoleId}, result success", GetActorId(), payload.Id);
            return CreatedAtAction(nameof(GetById), new { roleId = payload.Id }, payload);
        }

        return ToActionResult(result);
    }

    [HttpPut("~/api/roles/{roleId:guid}")]
    [PermissionAuthorize(PermissionConstants.RolesWrite)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<RoleResponse>> Update(
        Guid roleId,
        [FromBody] RoleUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (ControllerContext.HttpContext is null)
        {
            return ToActionResult(await permissionTopology.UpdateRoleAsync(roleId, request, cancellationToken));
        }
        if (ControllerContext.HttpContext is not null && (!Request.Headers.TryGetValue(HeaderNames.IfMatch, out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch)))
            return PreconditionRequired();
        var current = await permissionTopology.GetRoleByIdAsync(roleId, cancellationToken);
        if (current is null) return NotFound();
        var precondition = ValidateIfMatch(current.ETag);
        if (precondition.HasValue) return StatusCode(precondition.Value);
        var result = await permissionTopology.UpdateRoleAsync(roleId, request, cancellationToken);
        if (result.Value is not null) Response.Headers[HeaderNames.ETag] = result.Value.ETag;
        if (result.Status == PermissionTopologyCommandStatus.Success)
            logger?.LogInformation("Role administration audit: actor {Actor}, action update, resource {RoleId}, result success", GetActorId(), roleId);
        return ToActionResult(result);
    }

    [HttpDelete("~/api/roles/{roleId:guid}")]
    [PermissionAuthorize(PermissionConstants.RolesWrite)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid roleId, CancellationToken cancellationToken)
    {
        if (ControllerContext.HttpContext is null)
        {
            var commandResult = await permissionTopology.DeleteRoleAsync(roleId, cancellationToken);
            return commandResult.Status == PermissionTopologyCommandStatus.Success ? NoContent() : ToFailureActionResult(commandResult);
        }
        if (ControllerContext.HttpContext is not null && (!Request.Headers.TryGetValue(HeaderNames.IfMatch, out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch)))
            return PreconditionRequired();
        var current = await permissionTopology.GetRoleByIdAsync(roleId, cancellationToken);
        if (current is null) return NotFound();
        var precondition = ValidateIfMatch(current.ETag);
        if (precondition.HasValue) return StatusCode(precondition.Value);
        var result = await permissionTopology.DeleteRoleAsync(roleId, cancellationToken);
        if (result.Status == PermissionTopologyCommandStatus.Success)
        {
            logger?.LogInformation("Role administration audit: actor {Actor}, action delete, resource {RoleId}, result success", GetActorId(), roleId);
            return NoContent();
        }

        return ToFailureActionResult(result);
    }

    [HttpGet("~/api/roles/{roleId:guid}/permissions")]
    [PermissionAuthorize(PermissionConstants.RolesRead)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetPermissions(Guid roleId, CancellationToken cancellationToken)
    {
        var permissions = await permissionTopology.GetRolePermissionsAsync(roleId, cancellationToken);
        return permissions is null ? NotFound() : Ok(permissions);
    }

    [HttpPut("~/api/roles/{roleId:guid}/permissions")]
    [PermissionAuthorize(PermissionConstants.RolesWrite)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncPermissions(
        Guid roleId,
        [FromBody] RolePermissionSyncRequest request,
        CancellationToken cancellationToken)
    {
        if (ControllerContext.HttpContext is null)
        {
            var commandResult = await permissionTopology.SyncRolePermissionsAsync(roleId, request.Permissions, cancellationToken);
            return commandResult.Status == PermissionTopologyCommandStatus.Success ? NoContent() : ToFailureActionResult(commandResult);
        }
        if (ControllerContext.HttpContext is not null && (!Request.Headers.TryGetValue(HeaderNames.IfMatch, out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch)))
            return PreconditionRequired();
        var current = await permissionTopology.GetRoleByIdAsync(roleId, cancellationToken);
        if (current is null) return NotFound();
        var precondition = ValidateIfMatch(current.ETag);
        if (precondition.HasValue) return StatusCode(precondition.Value);
        var result = await permissionTopology.SyncRolePermissionsAsync(roleId, request.Permissions, cancellationToken);
        if (result.Status == PermissionTopologyCommandStatus.Success)
        {
            var updated = await permissionTopology.GetRoleByIdAsync(roleId, cancellationToken);
            if (updated is not null) Response.Headers[HeaderNames.ETag] = updated.ETag;
            logger?.LogInformation("Role administration audit: actor {Actor}, action sync_permissions, resource {RoleId}, result success", GetActorId(), roleId);
        }
        return result.Status == PermissionTopologyCommandStatus.Success
            ? NoContent()
            : ToFailureActionResult(result);
    }

    private int? ValidateIfMatch(string etag)
    {
        if (ControllerContext.HttpContext is null)
        {
            return null;
        }
        if (!Request.Headers.TryGetValue(HeaderNames.IfMatch, out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch))
        {
            return StatusCodes.Status428PreconditionRequired;
        }

        return string.Equals(ifMatch.ToString(), etag, StringComparison.Ordinal)
            ? null
            : StatusCodes.Status412PreconditionFailed;
    }

    private ObjectResult PreconditionRequired()
        => StatusCode(StatusCodes.Status428PreconditionRequired, new ProblemDetails
        {
            Status = StatusCodes.Status428PreconditionRequired,
            Title = "Precondition required",
            Detail = "If-Match is required for this operation.",
            Extensions = { ["code"] = "precondition_required" },
        });

    private string GetActorId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "anonymous";

    private ActionResult<T> ToActionResult<T>(PermissionTopologyCommandResult<T> result)
    {
        return result.Status switch
        {
            PermissionTopologyCommandStatus.Success => Ok(result.Value),
            PermissionTopologyCommandStatus.NotFound => NotFound(),
            PermissionTopologyCommandStatus.Conflict => Conflict(result.Errors.FirstOrDefault()),
            PermissionTopologyCommandStatus.Protected => Conflict(result.Errors.FirstOrDefault()),
            PermissionTopologyCommandStatus.ValidationFailed => ValidationProblem(CreateValidationProblem(result.Errors)),
            _ => Problem("Unexpected role management command status."),
        };
    }

    private IActionResult ToFailureActionResult<T>(PermissionTopologyCommandResult<T> result)
    {
        return result.Status switch
        {
            PermissionTopologyCommandStatus.NotFound => NotFound(),
            PermissionTopologyCommandStatus.Conflict => Conflict(result.Errors.FirstOrDefault()),
            PermissionTopologyCommandStatus.Protected => Conflict(result.Errors.FirstOrDefault()),
            PermissionTopologyCommandStatus.ValidationFailed => ValidationProblem(CreateValidationProblem(result.Errors)),
            _ => Problem("Unexpected role management command status."),
        };
    }

    private ValidationProblemDetails CreateValidationProblem(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(nameof(RoleUpsertRequest), error);
        }

        return new ValidationProblemDetails(ModelState);
    }
}
