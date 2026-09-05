using System.Security.Claims;

namespace BzsOIDC.Idp.Services.Identity;

/// <summary>
/// 从当前认证主体解析出的会话身份信息。
/// </summary>
public sealed record IdentitySession(
    string? UserId,
    string? UserName,
    string? DisplayName);

/// <summary>
/// 读取当前会话身份，不向调用方暴露 Identity 实体或管理器。
/// </summary>
public interface IIdentitySessionReader
{
    Task<IdentitySession> ReadAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}

internal sealed class IdentitySessionReader(IUserService userService) : IIdentitySessionReader
{
    public async Task<IdentitySession> ReadAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = principal.Identity?.Name;
        var displayName = principal.FindFirstValue("display_name") ?? userName;

        if (Guid.TryParse(userId, out var id))
        {
            var user = await userService.GetByIdAsync(id, cancellationToken);
            if (user is not null)
            {
                displayName = user.DisplayName;
                userName ??= user.UserName;
            }
        }

        return new IdentitySession(userId, userName, displayName);
    }
}
