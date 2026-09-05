using System.Security.Claims;

namespace BzsOIDC.Idp.Services.Identity;

/// <summary>
/// 由认证声明构成的只读身份主体。
/// </summary>
public sealed record IdentitySubject(
    string Id,
    string? UserName,
    IReadOnlyList<string> Roles);

/// <summary>
/// 从认证主体读取应用身份信息，不暴露 Identity 实体或管理器。
/// </summary>
public interface IIdentitySubjectReader
{
    IdentitySubject? Read(ClaimsPrincipal principal);
}

internal sealed class IdentitySubjectReader : IIdentitySubjectReader
{
    public IdentitySubject? Read(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var id = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var userName = principal.Identity?.Name
            ?? principal.FindFirstValue(ClaimTypes.Name);

        var roleClaimTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ClaimTypes.Role,
            "role",
        };

        foreach (var identity in principal.Identities)
        {
            if (!string.IsNullOrWhiteSpace(identity.RoleClaimType))
            {
                roleClaimTypes.Add(identity.RoleClaimType);
            }
        }

        var roles = principal.Claims
            .Where(claim => roleClaimTypes.Contains(claim.Type))
            .Select(static claim => claim.Value)
            .Where(static role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new IdentitySubject(id, userName, roles);
    }
}
