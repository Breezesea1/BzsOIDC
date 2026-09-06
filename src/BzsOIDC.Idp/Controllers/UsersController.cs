using System.Security.Claims;
using BzsOIDC.Contracts;
using BzsOIDC.Idp.Services;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BzsOIDC.Idp.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(IUserAdministration users, ILogger<UsersController> logger) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(PermissionConstants.UsersReadAll)]
    public async Task<ActionResult<UserListResponse>> List(
        [FromQuery] string? search,
        [FromQuery] string? sort = "userName",
        [FromQuery] string? direction = "asc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var all = await users.GetUsersAsync(cancellationToken);
        IEnumerable<UserAdministrationUser> query = all;
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => u.UserName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (u.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var descending = string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase);
        query = sort?.ToLowerInvariant() switch
        {
            "email" => descending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "admin" or "isadmin" => descending ? query.OrderByDescending(u => u.IsAdmin) : query.OrderBy(u => u.IsAdmin),
            _ => descending ? query.OrderByDescending(u => u.UserName) : query.OrderBy(u => u.UserName),
        };
        var total = query.Count();
        var items = query.Skip((page - 1) * pageSize).Take(pageSize).Select(ToResponse).ToArray();
        return Ok(new UserListResponse(items, total, page, pageSize));
    }

    [HttpGet("{id:guid}")]
    [PermissionAuthorize(PermissionConstants.UsersReadAll)]
    public async Task<ActionResult<UserResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var user = (await users.GetUsersAsync(cancellationToken)).FirstOrDefault(u => u.Id == id);
        if (user is null) return NotFound();
        Response.Headers.ETag = user.ETag;
        return Ok(ToResponse(user));
    }

    [HttpPost]
    [PermissionAuthorize(PermissionConstants.UsersWrite)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<UserResponse>> Create([FromBody] UserCreateRequest request, CancellationToken cancellationToken)
    {
        var result = await users.CreateAsync(new CreateUserAdministrationRequest(request.UserName, request.Password, request.Email, request.IsAdmin), cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return ValidationProblem();
        }

        var created = (await users.GetUsersAsync(cancellationToken)).FirstOrDefault(u => string.Equals(u.UserName, request.UserName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (created is null) return Problem("Created user could not be loaded.");
        logger.LogInformation("User administration audit: actor {Actor}, action create, target {Target}", await GetActorIdAsync(cancellationToken), created.Id);
        Response.Headers.ETag = created.ETag;
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ToResponse(created));
    }

    [HttpPut("{id:guid}")]
    [PermissionAuthorize(PermissionConstants.UsersWrite)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<UserResponse>> Update(Guid id, [FromBody] UserUpdateRequest request, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch))
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired);
        }

        var existing = (await users.GetUsersAsync(cancellationToken)).FirstOrDefault(u => u.Id == id);
        if (existing is null) return NotFound();
        if (!string.Equals(ifMatch.ToString(), existing.ETag, StringComparison.Ordinal)) return StatusCode(StatusCodes.Status412PreconditionFailed);

        var actor = await GetActorIdAsync(cancellationToken);
        var result = await users.UpdateAsync(id, new UpdateUserAdministrationRequest(request.UserName, request.Email, request.Password, request.IsAdmin), actor, cancellationToken);
        if (!result.Succeeded) { AddErrors(result); return ValidationProblem(); }
        var updated = (await users.GetUsersAsync(cancellationToken)).First(u => u.Id == id);
        logger.LogInformation("User administration audit: actor {Actor}, action update, target {Target}, passwordChanged {PasswordChanged}, admin {IsAdmin}", actor, id, !string.IsNullOrWhiteSpace(request.Password), request.IsAdmin);
        Response.Headers.ETag = updated.ETag;
        return Ok(ToResponse(updated));
    }

    [HttpDelete("{id:guid}")]
    [PermissionAuthorize(PermissionConstants.UsersWrite)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("If-Match", out var ifMatch) || string.IsNullOrWhiteSpace(ifMatch))
            return StatusCode(StatusCodes.Status428PreconditionRequired);
        var existing = (await users.GetUsersAsync(cancellationToken)).FirstOrDefault(u => u.Id == id);
        if (existing is null) return NotFound();
        if (!string.Equals(ifMatch.ToString(), existing.ETag, StringComparison.Ordinal)) return StatusCode(StatusCodes.Status412PreconditionFailed);
        var actor = await GetActorIdAsync(cancellationToken);
        var result = await users.DeleteAsync(id, actor, cancellationToken);
        if (result.Succeeded) logger.LogInformation("User administration audit: actor {Actor}, action delete, target {Target}", actor, id);
        return result.Succeeded ? NoContent() : Conflict(result.Errors);
    }

    private async Task<Guid> GetActorIdAsync(CancellationToken cancellationToken)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (Guid.TryParse(value, out var id)) return id;
        var name = User.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            var match = (await users.GetUsersAsync(cancellationToken)).FirstOrDefault(u => string.Equals(u.UserName, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Id;
        }
        return Guid.Empty;
    }

    private static UserResponse ToResponse(UserAdministrationUser user) => new(user.Id, user.UserName, user.Email, user.IsAdmin, user.ETag ?? string.Empty);

    private void AddErrors(UserAdministrationResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(error.Code, error.Arguments.Count == 0 ? error.Code : string.Join(" ", error.Arguments));
    }
}
