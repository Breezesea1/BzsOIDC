using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BzsOIDC.Idp.Controllers;

[ApiController]
public sealed class PermissionTopologyController(
    IPermissionTopology permissionTopology) : ControllerBase
{
    // The permission-catalog route is retained for API compatibility.
    [HttpGet("~/api/permission-catalog/resources")]
    [PermissionAuthorize(PermissionConstants.PermissionsRead)]
    public async Task<ActionResult<IReadOnlyList<ProtectedResourceResponse>>> GetResources(CancellationToken cancellationToken)
    {
        return Ok(await permissionTopology.GetResourcesAsync(cancellationToken));
    }

    [HttpGet("~/api/permission-catalog/resources/{resourceKey}")]
    [PermissionAuthorize(PermissionConstants.PermissionsRead)]
    public async Task<ActionResult<ProtectedResourceResponse>> GetResource(string resourceKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            ModelState.AddModelError(nameof(resourceKey), "Protected resource key is required.");
            return ValidationProblem(ModelState);
        }

        var resource = await permissionTopology.GetResourceAsync(resourceKey, cancellationToken);
        return resource is null ? NotFound() : Ok(resource);
    }

    [HttpPut("~/api/permission-catalog/resources/{resourceKey}")]
    [PermissionAuthorize(PermissionConstants.PermissionsWrite)]
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

        var result = await permissionTopology.UpsertResourceAsync(resourceKey, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("~/api/permission-catalog/resources/{resourceKey}/permissions/{permissionName}")]
    [PermissionAuthorize(PermissionConstants.PermissionsWrite)]
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

        var result = await permissionTopology.UpsertPermissionAsync(resourceKey, permissionName, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("~/api/permission-catalog/permissions/{permissionName}/release-scopes")]
    [PermissionAuthorize(PermissionConstants.PermissionsWrite)]
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

        var result = await permissionTopology.SyncReleaseScopesAsync(permissionName, request.Scopes, cancellationToken);
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
    public async Task<IActionResult> SyncRolePermissions(
        Guid roleId,
        [FromBody] RolePermissionSyncRequest request,
        CancellationToken cancellationToken)
    {
        var result = await permissionTopology.SyncRolePermissionsAsync(roleId, request.Permissions, cancellationToken);
        if (result.Status == PermissionTopologyCommandStatus.Success)
        {
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

    private ValidationProblemDetails CreateValidationProblem(IEnumerable<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(nameof(PermissionTopologyController), error);
        }

        return new ValidationProblemDetails(ModelState);
    }
}
