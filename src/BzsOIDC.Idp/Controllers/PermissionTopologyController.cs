using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BzsOIDC.Idp.Controllers;

[ApiController]
public sealed class PermissionTopologyController(
    IPermissionTopology permissionTopology) : ControllerBase
{
    // The permission-catalog route is retained for API compatibility.
    [HttpGet("~/api/permission-catalog/resources")]
    [HttpGet("~/api/permission-topology/resources")]
    [HttpGet("~/api/admin/permission-topology/resources")]
    [PermissionAuthorize(PermissionConstants.PermissionsRead)]
    public async Task<ActionResult<IReadOnlyList<ProtectedResourceResponse>>> GetResources(CancellationToken cancellationToken)
    {
        return Ok(await permissionTopology.GetResourcesAsync(cancellationToken));
    }

    [HttpGet("~/api/permission-catalog/resources/{resourceKey}")]
    [HttpGet("~/api/permission-topology/resources/{resourceKey}")]
    [HttpGet("~/api/admin/permission-topology/resources/{resourceKey}")]
    [PermissionAuthorize(PermissionConstants.PermissionsRead)]
    public async Task<ActionResult<ProtectedResourceResponse>> GetResource(string resourceKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            ModelState.AddModelError(nameof(resourceKey), "Protected resource key is required.");
            return ValidationProblem(ModelState);
        }

        var resource = await permissionTopology.GetResourceAsync(resourceKey, cancellationToken);
        if (resource is null)
        {
            return NotFound();
        }

        Response.Headers[HeaderNames.ETag] = resource.ETag;
        return Ok(resource);
    }

    [HttpPut("~/api/permission-catalog/resources/{resourceKey}")]
    [HttpPut("~/api/permission-topology/resources/{resourceKey}")]
    [HttpPut("~/api/admin/permission-topology/resources/{resourceKey}")]
    [PermissionAuthorize(PermissionConstants.PermissionsWrite)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<ProtectedResourceResponse>> UpsertResource(
        string resourceKey,
        [FromBody] ProtectedResourceUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            ModelState.AddModelError(nameof(resourceKey), "Protected resource key is required.");
            return ValidationProblem(ModelState);
        }

        if (ControllerContext.HttpContext is not null)
        {
            var precondition = await ValidateIfMatchAsync(
                () => permissionTopology.GetResourceAsync(resourceKey, cancellationToken));
            if (precondition is not null)
            {
                return precondition;
            }
        }

        var result = await permissionTopology.UpsertResourceAsync(resourceKey, request, cancellationToken);
        if (ControllerContext.HttpContext is not null && result.Status == PermissionTopologyCommandStatus.Success && result.Value is not null)
        {
            Response.Headers[HeaderNames.ETag] = result.Value.ETag;
        }
        return ToActionResult(result);
    }

    [HttpPut("~/api/permission-catalog/resources/{resourceKey}/permissions/{permissionName}")]
    [HttpPut("~/api/permission-topology/resources/{resourceKey}/permissions/{permissionName}")]
    [HttpPut("~/api/admin/permission-topology/resources/{resourceKey}/permissions/{permissionName}")]
    [PermissionAuthorize(PermissionConstants.PermissionsWrite)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<PermissionDefinitionResponse>> UpsertPermission(
        string resourceKey,
        string permissionName,
        [FromBody] PermissionDefinitionUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            ModelState.AddModelError(nameof(resourceKey), "Protected resource key is required.");
        }

        if (string.IsNullOrWhiteSpace(permissionName))
        {
            ModelState.AddModelError(nameof(permissionName), "Permission name is required.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (ControllerContext.HttpContext is not null)
        {
            var precondition = await ValidateIfMatchAsync(
                () => permissionTopology.GetPermissionAsync(permissionName, cancellationToken));
            if (precondition is not null)
            {
                return precondition;
            }
        }

        var result = await permissionTopology.UpsertPermissionAsync(resourceKey, permissionName, request, cancellationToken);
        if (ControllerContext.HttpContext is not null && result.Status == PermissionTopologyCommandStatus.Success && result.Value is not null)
        {
            Response.Headers[HeaderNames.ETag] = result.Value.ETag;
        }
        return ToActionResult(result);
    }

    [HttpPut("~/api/permission-catalog/permissions/{permissionName}/release-scopes")]
    [HttpPut("~/api/permission-topology/permissions/{permissionName}/release-scopes")]
    [HttpPut("~/api/admin/permission-topology/permissions/{permissionName}/release-scopes")]
    [PermissionAuthorize(PermissionConstants.PermissionsWrite)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<PermissionDefinitionResponse>> SyncReleaseScopes(
        string permissionName,
        [FromBody] PermissionReleaseScopesUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(permissionName))
        {
            ModelState.AddModelError(nameof(permissionName), "Permission name is required.");
            return ValidationProblem(ModelState);
        }

        if (ControllerContext.HttpContext is not null)
        {
            var precondition = await ValidateIfMatchAsync(
                () => permissionTopology.GetPermissionAsync(permissionName, cancellationToken));
            if (precondition is not null)
            {
                return precondition;
            }
        }

        var result = await permissionTopology.SyncReleaseScopesAsync(permissionName, request.Scopes, cancellationToken);
        if (ControllerContext.HttpContext is not null && result.Status == PermissionTopologyCommandStatus.Success && result.Value is not null)
        {
            Response.Headers[HeaderNames.ETag] = result.Value.ETag;
        }
        return ToActionResult(result);
    }

    [HttpGet("~/api/permission-catalog/roles/{roleId:guid}/permissions")]
    [PermissionAuthorize(PermissionConstants.PermissionsRead)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetRolePermissions(Guid roleId, CancellationToken cancellationToken)
    {
        var permissions = await permissionTopology.GetRolePermissionsAsync(roleId, cancellationToken);
        return permissions is null ? NotFound() : Ok(permissions);
    }

    [HttpPut("~/api/permission-catalog/roles/{roleId:guid}/permissions")]
    [PermissionAuthorize(PermissionConstants.PermissionsWrite)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncRolePermissions(
        Guid roleId,
        [FromBody] RolePermissionSyncRequest request,
        CancellationToken cancellationToken)
    {
        if (ControllerContext.HttpContext is null)
        {
            var commandResult = await permissionTopology.SyncRolePermissionsAsync(roleId, request.Permissions, cancellationToken);
            return commandResult.Status switch
            {
                PermissionTopologyCommandStatus.Success => NoContent(),
                PermissionTopologyCommandStatus.NotFound => NotFound(),
                _ => ValidationProblem(ModelState),
            };
        }
        var current = await permissionTopology.GetRoleByIdAsync(roleId, cancellationToken);
        if (current is null) return NotFound();
        if (!Request.Headers.TryGetValue(HeaderNames.IfMatch, out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch))
            return StatusCode(StatusCodes.Status428PreconditionRequired);
        if (!string.Equals(ifMatch.ToString(), current.ETag, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status412PreconditionFailed);
        var result = await permissionTopology.SyncRolePermissionsAsync(roleId, request.Permissions, cancellationToken);
        if (result.Status == PermissionTopologyCommandStatus.Success)
        {
            var updated = await permissionTopology.GetRoleByIdAsync(roleId, cancellationToken);
            if (updated is not null) Response.Headers[HeaderNames.ETag] = updated.ETag;
            return NoContent();
        }

        if (result.Status == PermissionTopologyCommandStatus.NotFound)
        {
            return NotFound();
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(nameof(RolePermissionSyncRequest), error);
        }

        return ValidationProblem(ModelState);
    }

    private ActionResult<T> ToActionResult<T>(PermissionTopologyCommandResult<T> result)
    {
        return result.Status switch
        {
            PermissionTopologyCommandStatus.Success => Ok(result.Value),
            PermissionTopologyCommandStatus.NotFound => NotFound(),
            PermissionTopologyCommandStatus.Conflict => Conflict(result.Errors.FirstOrDefault()),
            PermissionTopologyCommandStatus.ValidationFailed => ValidationProblem(CreateValidationProblem(result.Errors)),
            _ => Problem("Unexpected permission topology command status."),
        };
    }

    private async Task<ObjectResult?> ValidateIfMatchAsync<T>(
        Func<Task<T?>> currentFactory)
    {
        var current = await currentFactory();
        // An absent resource represents an upsert/create operation; there is no
        // prior representation against which an If-Match value can be checked.
        if (current is null)
        {
            return null;
        }

        if (!Request.Headers.TryGetValue(HeaderNames.IfMatch, out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch))
        {
            return PreconditionRequired();
        }

        if (string.Equals(ifMatch.ToString(), GetETag(current), StringComparison.Ordinal) || string.Equals(ifMatch.ToString(), "*", StringComparison.Ordinal))
        {
            return null;
        }

        return ConcurrencyConflict();
    }

    private static string GetETag<T>(T value)
        => value switch
        {
            ProtectedResourceResponse resource => resource.ETag,
            PermissionDefinitionResponse permission => permission.ETag,
            _ => string.Empty,
        };

    private ObjectResult PreconditionRequired()
        => StatusCode(StatusCodes.Status428PreconditionRequired, new ProblemDetails
        {
            Status = StatusCodes.Status428PreconditionRequired,
            Title = "Precondition required",
            Detail = "If-Match is required for this operation.",
            Extensions = { ["code"] = "precondition_required" },
        });

    private ObjectResult ConcurrencyConflict()
        => StatusCode(StatusCodes.Status412PreconditionFailed, new ProblemDetails
        {
            Status = StatusCodes.Status412PreconditionFailed,
            Title = "Precondition failed",
            Detail = "The resource has changed since it was read.",
            Extensions = { ["code"] = "concurrency_conflict" },
        });

    private ValidationProblemDetails CreateValidationProblem(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(nameof(PermissionTopologyController), error);
        }

        return new ValidationProblemDetails(ModelState);
    }
}
