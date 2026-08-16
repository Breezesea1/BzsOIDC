using BzsOIDC.Idp.Services.Oidc;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace BzsOIDC.Idp.Services.Identity;

internal sealed class IdentitySeeder(
    IPermissionTopology permissionTopology,
    IOidcScopeService oidcScopeService,
    OidcClientPermissionBackfillService oidcClientPermissionBackfillService,
    IUserService userService,
    IOptions<IdentitySeedOptions> identityOptions,
    IConfiguration configuration,
    ILogger<IdentitySeeder> logger)
{
    /// <summary>
    /// 执行初始化种子逻辑。
    /// </summary>
    /// <param name="cancellationToken">参数cancellationToken。</param>
    /// <returns>执行结果。</returns>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var options = SmokeIdentitySeedProfile.Resolve(
            configuration.IsSmokeTestingEnabled(),
            identityOptions.Value);
        var adminUserName = options.Admin.UserName?.Trim();
        var adminPassword = options.Admin.Password?.Trim();

        if (string.IsNullOrWhiteSpace(adminUserName))
        {
            throw new InvalidOperationException("Identity:Admin:UserName is required.");
        }

        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            throw new InvalidOperationException("Identity:Admin:Password is required.");
        }

        var configuredRoles = options.InitialRoles
            .Where(static roleName => !string.IsNullOrWhiteSpace(roleName))
            .Select(static roleName => roleName.Trim())
            .Concat(options.RolePermissions.Keys.Where(static roleName => !string.IsNullOrWhiteSpace(roleName))
                .Select(static roleName => roleName.Trim()))
            .Append(IdentitySeedConstants.AdminRoleName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rolePermissions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (roleName, permissions) in options.RolePermissions)
        {
            rolePermissions[roleName] = permissions;
        }

        rolePermissions[IdentitySeedConstants.AdminRoleName] = options.RolePermissions.Values
            .SelectMany(static permissions => permissions)
            .Concat(options.PermissionCatalog.SelectMany(static resource => resource.Permissions).Select(static permission => permission.Name))
            .Where(static permission => !string.IsNullOrWhiteSpace(permission))
            .Select(static permission => permission.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await permissionTopology.InitializeDefaultsAsync(options.PermissionCatalog, configuredRoles, rolePermissions, cancellationToken);
        foreach (var roleName in configuredRoles)
        {
            logger.LogInformation("Identity seeding ensured role exists: {RoleName}", roleName);
        }

        foreach (var roleName in rolePermissions.Keys)
        {
            logger.LogInformation("Identity seeding synchronized permissions for role: {RoleName}", roleName);
        }
        await oidcScopeService.InitializeDefaultsIfMissingAsync(options.AdditionalScopes, cancellationToken);

        var adminUser = await userService.GetByNameAsync(adminUserName, cancellationToken);
        if (adminUser is null)
        {
            var createResult = await userService.CreateAsync(adminUserName, adminPassword, cancellationToken: cancellationToken);
            if (!IsSuccessOrExpectedConflict(createResult, "DuplicateUserName"))
            {
                EnsureSuccess(createResult, $"创建管理员用户 '{adminUserName}'");
            }

            adminUser = await userService.GetByNameAsync(adminUserName, cancellationToken);
            if (adminUser is null)
            {
                throw new InvalidOperationException($"创建管理员用户 '{adminUserName}' 后未能读取到用户。");
            }

            logger.LogInformation("Identity seeding ensured admin user exists: {UserName}", adminUserName);
        }

        var ensurePasswordResult = await userService.EnsurePasswordAsync(adminUser.Id, adminPassword, cancellationToken);
        EnsureSuccess(ensurePasswordResult, $"同步管理员用户 '{adminUserName}' 的密码");

        var inAdminRole = await userService.IsInRoleAsync(adminUser.Id, IdentitySeedConstants.AdminRoleName, cancellationToken);
        if (!inAdminRole)
        {
            var addToRoleResult = await userService.AddToRoleAsync(adminUser.Id, IdentitySeedConstants.AdminRoleName, cancellationToken);
            if (!IsSuccessOrExpectedConflict(addToRoleResult, "UserAlreadyInRole"))
            {
                EnsureSuccess(addToRoleResult, $"将用户 '{adminUserName}' 加入 admin 角色");
            }

            logger.LogInformation("Identity seeding granted admin role to: {UserName}", adminUserName);
        }

        await oidcClientPermissionBackfillService.EnsureBackfilledAsync(cancellationToken);
    }

    /// <summary>
    /// 判断条件是否成立。
    /// </summary>
    /// <param name="result">参数result。</param>
    /// <param name="expectedErrorCode">参数expectedErrorCode。</param>
    /// <returns>执行结果。</returns>
    private static bool IsSuccessOrExpectedConflict(IdentityResult result, string expectedErrorCode)
    {
        if (result.Succeeded)
        {
            return true;
        }

        return result.Errors.Any(err => string.Equals(err.Code, expectedErrorCode, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 确保前置条件满足。
    /// </summary>
    /// <param name="result">参数result。</param>
    /// <param name="action">参数action。</param>
    private static void EnsureSuccess(IdentityResult result, string action)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(", ", result.Errors.Select(static err => $"{err.Code}:{err.Description}"));
        throw new InvalidOperationException($"{action}失败。{errors}");
    }
}
