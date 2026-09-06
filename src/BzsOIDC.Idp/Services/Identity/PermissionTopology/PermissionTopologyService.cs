using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BzsOIDC.Idp.Infra;
using BzsOIDC.Idp.Models;
using BzsOIDC.Shared.Infrastructure.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BzsOIDC.Idp.Services.Identity;

internal sealed class PermissionTopologyService(
    IdpDbContext dbContext,
    RoleManager<BzsRole> roleManager,
    RoleManagementPolicy policy,
    IMemoryCache cache) : IPermissionTopology
{
    private const string ReleaseScopeMappingsCacheKey = "idp.permission-topology.release-scopes";
    private static readonly string[] ObsoleteSeedResourceKeys = ["legacy-api", "oidc-clients", "oidc-scopes"];

    public async Task<IReadOnlyList<ProtectedResourceResponse>> GetResourcesAsync(CancellationToken cancellationToken = default)
    {
        var resources = await QueryResources().OrderBy(static resource => resource.Key).ToListAsync(cancellationToken);
        var roleAssignments = await GetAssignedRolesByPermissionAsync(cancellationToken);
        return resources.Select(resource => ToResponse(resource, roleAssignments)).ToArray();
    }

    public async Task<ProtectedResourceResponse?> GetResourceAsync(string resourceKey, CancellationToken cancellationToken = default)
    {
        var resource = await QueryResources().FirstOrDefaultAsync(resource => resource.Key == ProtectedResource.NormalizeKey(resourceKey), cancellationToken);
        return resource is null ? null : ToResponse(resource, await GetAssignedRolesByPermissionAsync(cancellationToken));
    }

    public async Task<PermissionDefinitionResponse?> GetPermissionAsync(string permissionName, CancellationToken cancellationToken = default)
    {
        var permission = await QueryPermissions().FirstOrDefaultAsync(permission => permission.Name == PermissionDefinition.NormalizeName(permissionName), cancellationToken);
        return permission is null ? null : ToResponse(permission, await GetAssignedRolesByPermissionAsync(cancellationToken));
    }

    public async Task<PermissionTopologyCommandResult<ProtectedResourceResponse>> UpsertResourceAsync(string resourceKey, ProtectedResourceUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedKey = ProtectedResource.NormalizeKey(resourceKey);
        var resource = await QueryResources().FirstOrDefaultAsync(resource => resource.Key == normalizedKey, cancellationToken);
        if (resource is null)
        {
            resource = ProtectedResource.Create(normalizedKey, request.DisplayName, request.Description);
            await dbContext.AddAsync(resource, cancellationToken);
        }

        resource.Update(request.DisplayName ?? normalizedKey, request.Description, request.IsActive);
        await dbContext.SaveChangesAsync(cancellationToken);
        cache.Remove(ReleaseScopeMappingsCacheKey);
        return PermissionTopologyCommandResult<ProtectedResourceResponse>.Success(ToResponse(resource, EmptyRoleAssignments));
    }

    public async Task<PermissionTopologyCommandResult<PermissionDefinitionResponse>> UpsertPermissionAsync(string resourceKey, string permissionName, PermissionDefinitionUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedResourceKey = ProtectedResource.NormalizeKey(resourceKey);
        var normalizedPermissionName = PermissionDefinition.NormalizeName(permissionName);
        var resource = await QueryResources().FirstOrDefaultAsync(resource => resource.Key == normalizedResourceKey, cancellationToken);
        if (resource is null)
        {
            return PermissionTopologyCommandResult<PermissionDefinitionResponse>.Failure(PermissionTopologyCommandStatus.NotFound, $"Protected resource '{normalizedResourceKey}' was not found.");
        }

        var permission = resource.Permissions.FirstOrDefault(permission => string.Equals(permission.Name, normalizedPermissionName, StringComparison.OrdinalIgnoreCase));
        if (permission is null)
        {
            permission = resource.AddPermission(normalizedPermissionName, request.DisplayName, request.Description);
        }

        permission.Update(request.DisplayName, request.Description, request.IsActive);
        await dbContext.SaveChangesAsync(cancellationToken);
        cache.Remove(ReleaseScopeMappingsCacheKey);
        return PermissionTopologyCommandResult<PermissionDefinitionResponse>.Success(ToResponse(permission, EmptyRoleAssignments));
    }

    public async Task<PermissionTopologyCommandResult<PermissionDefinitionResponse>> SyncReleaseScopesAsync(string permissionName, IEnumerable<string> scopes, CancellationToken cancellationToken = default)
    {
        var targetScopes = scopes.Where(static scope => !string.IsNullOrWhiteSpace(scope)).Select(PermissionReleaseScope.NormalizeScope).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (targetScopes.Length == 0)
        {
            return PermissionTopologyCommandResult<PermissionDefinitionResponse>.Failure(PermissionTopologyCommandStatus.ValidationFailed, "At least one release scope is required.");
        }

        var normalizedPermissionName = PermissionDefinition.NormalizeName(permissionName);
        var permission = await QueryPermissions().FirstOrDefaultAsync(permission => permission.Name == normalizedPermissionName, cancellationToken);
        if (permission is null)
        {
            return PermissionTopologyCommandResult<PermissionDefinitionResponse>.Failure(PermissionTopologyCommandStatus.NotFound, $"Permission '{normalizedPermissionName}' was not found.");
        }

        permission.SyncReleaseScopes(targetScopes);
        await dbContext.SaveChangesAsync(cancellationToken);
        cache.Remove(ReleaseScopeMappingsCacheKey);
        return PermissionTopologyCommandResult<PermissionDefinitionResponse>.Success(ToResponse(permission, await GetAssignedRolesByPermissionAsync(cancellationToken)));
    }

    public async Task<IReadOnlyDictionary<string, string[]>> ResolveReleaseScopesAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        var normalizedPermissions = permissions.Where(static permission => !string.IsNullOrWhiteSpace(permission)).Select(PermissionDefinition.NormalizeName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalizedPermissions.Length == 0) return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var mappings = await GetReleaseScopeMappingsAsync(cancellationToken);
        return normalizedPermissions.Where(mappings.ContainsKey).ToDictionary(permission => permission, permission => mappings[permission], StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string[]> ValidateAssignablePermissionsAsync(IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        var targets = permissions.Where(static permission => !string.IsNullOrWhiteSpace(permission)).Select(PermissionDefinition.NormalizeName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (targets.Length == 0) return [];
        var active = await dbContext.PermissionDefinitions.AsNoTracking().Where(static permission => permission.IsActive && permission.Resource.IsActive).Select(static permission => permission.Name).ToListAsync(cancellationToken);
        var activeSet = active.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return targets.Where(permission => !activeSet.Contains(permission)).OrderBy(static permission => permission, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<RoleResponse>> GetAllRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await dbContext.Roles.AsNoTracking().OrderBy(static role => role.Name).ToListAsync(cancellationToken);
        var permissions = await GetPermissionsByRoleAsync(cancellationToken);
        return roles.Select(role => ToRoleResponse(role, permissions, includePermissions: false)).ToArray();
    }

    public async Task<RoleListResponse> ListRolesAsync(RoleListQuery query, CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var roles = await GetAllRolesAsync(cancellationToken);
        IEnumerable<RoleResponse> filtered = roles;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            filtered = filtered.Where(role => role.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        filtered = query.Sort?.ToLowerInvariant() switch
        {
            "permissioncount" => query.Descending ? filtered.OrderByDescending(role => role.PermissionCount).ThenBy(role => role.Id) : filtered.OrderBy(role => role.PermissionCount).ThenBy(role => role.Id),
            _ => query.Descending ? filtered.OrderByDescending(role => role.Name, StringComparer.OrdinalIgnoreCase).ThenBy(role => role.Id) : filtered.OrderBy(role => role.Name, StringComparer.OrdinalIgnoreCase).ThenBy(role => role.Id),
        };

        var materialized = filtered.ToArray();
        return new RoleListResponse
        {
            Items = materialized.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
            TotalCount = materialized.Length,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<RoleResponse?> GetRoleByIdAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await dbContext.Roles.AsNoTracking().FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken);
        if (role is null) return null;
        return ToRoleResponse(role, await GetPermissionsByRoleAsync(cancellationToken), includePermissions: true);
    }

    public async Task<IReadOnlyList<string>?> GetRolePermissionsAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.Roles.AsNoTracking().AnyAsync(role => role.Id == roleId, cancellationToken);
        if (!exists) return null;
        var permissions = await GetPermissionsByRoleAsync(cancellationToken);
        return permissions.TryGetValue(roleId, out var values) ? values : [];
    }

    public async Task<IReadOnlyList<string>> ResolveRolePermissionsAsync(
        IEnumerable<string> roleNames,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoleNames = roleNames
            .Where(static roleName => !string.IsNullOrWhiteSpace(roleName))
            .Select(policy.NormalizeKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedRoleNames.Length == 0)
        {
            return [];
        }

        var permissions = await dbContext.Roles.AsNoTracking()
            .Where(role => role.NormalizedName != null && normalizedRoleNames.Contains(role.NormalizedName))
            .Join(
                dbContext.RoleClaims.AsNoTracking()
                    .Where(static claim => claim.ClaimType != null && claim.ClaimType.ToLower() == PermissionConstants.ClaimType),
                role => role.Id,
                claim => claim.RoleId,
                (role, claim) => claim.ClaimValue)
            .ToListAsync(cancellationToken);

        return permissions
            .Where(static permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => PermissionDefinition.NormalizeName(permission!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static permission => permission, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<PermissionTopologyCommandResult<RoleResponse>> CreateRoleAsync(RoleUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var reservedRoleExists = await roleManager.FindByNameAsync(IdentitySeedConstants.AdminRoleName) is not null;
        var validation = policy.ValidateCreate(request.Name, reservedRoleExists);
        if (validation.Length > 0) return PermissionTopologyCommandResult<RoleResponse>.Failure(PermissionTopologyCommandStatus.ValidationFailed, validation);
        var name = policy.NormalizeName(request.Name);
        if (await roleManager.FindByNameAsync(name) is not null) return PermissionTopologyCommandResult<RoleResponse>.Failure(PermissionTopologyCommandStatus.Conflict, $"角色 '{name}' 已存在");
        var role = new BzsRole { Name = name, NormalizedName = policy.NormalizeKey(name) };
        var result = await roleManager.CreateAsync(role);
        return result.Succeeded
            ? PermissionTopologyCommandResult<RoleResponse>.Success(ToRoleResponse(role, EmptyRolePermissions, true))
            : FromIdentityFailure<RoleResponse>(result);
    }

    public async Task<PermissionTopologyCommandResult<RoleResponse>> UpdateRoleAsync(Guid roleId, RoleUpsertRequest request, CancellationToken cancellationToken = default)
    {
        var role = await roleManager.FindByIdAsync(roleId.ToString());
        if (role is null) return PermissionTopologyCommandResult<RoleResponse>.Failure(PermissionTopologyCommandStatus.NotFound, $"角色 '{roleId}' 不存在");
        var validation = policy.ValidateRename(role, request.Name);
        if (validation.Length > 0) return PermissionTopologyCommandResult<RoleResponse>.Failure(policy.IsProtectedRole(role) ? PermissionTopologyCommandStatus.Protected : PermissionTopologyCommandStatus.ValidationFailed, validation);
        var name = policy.NormalizeName(request.Name);
        var existing = await roleManager.FindByNameAsync(name);
        if (existing is not null && existing.Id != role.Id) return PermissionTopologyCommandResult<RoleResponse>.Failure(PermissionTopologyCommandStatus.Conflict, $"角色 '{name}' 已存在");
        role.Name = name;
        role.NormalizedName = policy.NormalizeKey(name);
        role.ConcurrencyStamp = Guid.NewGuid().ToString();
        var result = await roleManager.UpdateAsync(role);
        return result.Succeeded
            ? PermissionTopologyCommandResult<RoleResponse>.Success(ToRoleResponse(role, await GetPermissionsByRoleAsync(cancellationToken), true))
            : FromIdentityFailure<RoleResponse>(result);
    }

    public async Task<PermissionTopologyCommandResult<RoleResponse>> DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await roleManager.FindByIdAsync(roleId.ToString());
        if (role is null) return PermissionTopologyCommandResult<RoleResponse>.Failure(PermissionTopologyCommandStatus.NotFound, $"角色 '{roleId}' 不存在");
        var validation = policy.ValidateDelete(role);
        if (validation.Length > 0) return PermissionTopologyCommandResult<RoleResponse>.Failure(PermissionTopologyCommandStatus.Protected, validation);
        var response = ToRoleResponse(role, await GetPermissionsByRoleAsync(cancellationToken), true);
        var result = await roleManager.DeleteAsync(role);
        return result.Succeeded ? PermissionTopologyCommandResult<RoleResponse>.Success(response) : FromIdentityFailure<RoleResponse>(result);
    }

    public async Task<PermissionTopologyCommandResult<IReadOnlyList<string>>> SyncRolePermissionsAsync(Guid roleId, IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        var roleExists = await dbContext.Roles.AsNoTracking().AnyAsync(role => role.Id == roleId, cancellationToken);
        if (!roleExists)
        {
            return PermissionTopologyCommandResult<IReadOnlyList<string>>.Failure(PermissionTopologyCommandStatus.NotFound, $"角色 '{roleId}' 不存在");
        }

        var targetSet = permissions.Where(static permission => !string.IsNullOrWhiteSpace(permission)).Select(PermissionDefinition.NormalizeName).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalid = await ValidateAssignablePermissionsAsync(targetSet, cancellationToken);
        if (invalid.Length > 0)
        {
            return PermissionTopologyCommandResult<IReadOnlyList<string>>.Failure(PermissionTopologyCommandStatus.ValidationFailed, $"以下权限不存在或未启用：{string.Join(", ", invalid)}");
        }

        PermissionTopologyCommandResult<IReadOnlyList<string>>? commandResult = null;
        BzsRole? attemptedRole = null;
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            commandResult = null;
            attemptedRole = null;
            try
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                var role = await roleManager.FindByIdAsync(roleId.ToString());
                if (role is null)
                {
                    commandResult = PermissionTopologyCommandResult<IReadOnlyList<string>>.Failure(PermissionTopologyCommandStatus.NotFound, $"角色 '{roleId}' 不存在");
                    return;
                }

                attemptedRole = role;
                var existingClaims = await roleManager.GetClaimsAsync(role);
                var existingSet = existingClaims.Where(static claim => string.Equals(claim.Type, PermissionConstants.ClaimType, StringComparison.OrdinalIgnoreCase)).Select(static claim => PermissionDefinition.NormalizeName(claim.Value)).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var permission in targetSet.Except(existingSet, StringComparer.OrdinalIgnoreCase))
                {
                    var result = await roleManager.AddClaimAsync(role, new Claim(PermissionConstants.ClaimType, permission));
                    if (!result.Succeeded)
                    {
                        commandResult = FromIdentityFailure<IReadOnlyList<string>>(result);
                        return;
                    }
                }

                foreach (var permission in existingSet.Except(targetSet, StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var claim in existingClaims.Where(claim => string.Equals(claim.Type, PermissionConstants.ClaimType, StringComparison.OrdinalIgnoreCase) && string.Equals(claim.Value, permission, StringComparison.OrdinalIgnoreCase)))
                    {
                        var result = await roleManager.RemoveClaimAsync(role, claim);
                        if (!result.Succeeded)
                        {
                            commandResult = FromIdentityFailure<IReadOnlyList<string>>(result);
                            return;
                        }
                    }
                }

                role.ConcurrencyStamp = Guid.NewGuid().ToString();
                var roleUpdate = await roleManager.UpdateAsync(role);
                if (!roleUpdate.Succeeded)
                {
                    commandResult = FromIdentityFailure<IReadOnlyList<string>>(roleUpdate);
                    return;
                }

                await transaction.CommitAsync(cancellationToken);
                commandResult = PermissionTopologyCommandResult<IReadOnlyList<string>>.Success(await GetRolePermissionClaimsAsync(role.Id, cancellationToken));
            }
            catch
            {
                DetachRoleClaimAttempt(attemptedRole);
                throw;
            }
        });

        if (commandResult?.Status != PermissionTopologyCommandStatus.Success)
        {
            DetachRoleClaimAttempt(attemptedRole);
        }

        return commandResult!;
    }

    public async Task InitializeDefaultsAsync(
        IEnumerable<PermissionTopologySeedResource> resources,
        IEnumerable<string> rolesToEnsure,
        IReadOnlyDictionary<string, string[]> rolePermissions,
        CancellationToken cancellationToken = default)
    {
        await RemoveObsoleteSeedResourcesAsync(cancellationToken);
        var resourcesByKey = await QueryResources().ToDictionaryAsync(static resource => resource.Key, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var permissionsByName = resourcesByKey.Values.SelectMany(static resource => resource.Permissions).ToDictionary(static permission => permission.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var seedResource in resources.Where(static resource => !string.IsNullOrWhiteSpace(resource.ResourceKey)).GroupBy(static resource => ProtectedResource.NormalizeKey(resource.ResourceKey), StringComparer.OrdinalIgnoreCase).Select(static group => group.Last()))
        {
            var resourceKey = ProtectedResource.NormalizeKey(seedResource.ResourceKey);
            if (!resourcesByKey.TryGetValue(resourceKey, out var resource))
            {
                resource = ProtectedResource.Create(resourceKey, seedResource.DisplayName, seedResource.Description);
                await dbContext.AddAsync(resource, cancellationToken);
                resourcesByKey[resourceKey] = resource;
            }

            resource.Update(seedResource.DisplayName ?? resource.DisplayName, seedResource.Description, true);
            foreach (var seedPermission in seedResource.Permissions.Where(static permission => !string.IsNullOrWhiteSpace(permission.Name)))
            {
                var name = PermissionDefinition.NormalizeName(seedPermission.Name);
                var permission = resource.Permissions.FirstOrDefault(permission => string.Equals(permission.Name, name, StringComparison.OrdinalIgnoreCase));
                if (permission is null && !permissionsByName.ContainsKey(name))
                {
                    permission = resource.AddPermission(name, seedPermission.DisplayName, seedPermission.Description);
                    permissionsByName[name] = permission;
                }

                if (permission is not null)
                {
                    permission.SyncReleaseScopes(seedPermission.ReleaseScopes.Length > 0 ? seedPermission.ReleaseScopes : [resourceKey]);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        cache.Remove(ReleaseScopeMappingsCacheKey);

        foreach (var roleName in rolesToEnsure
                     .Concat(rolePermissions.Keys)
                     .Where(static roleName => !string.IsNullOrWhiteSpace(roleName))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await EnsureRoleAsync(roleName);
        }

        foreach (var (roleName, permissions) in rolePermissions)
        {
            var role = await EnsureRoleAsync(roleName);
            var syncResult = await SyncRolePermissionsAsync(role.Id, permissions, cancellationToken);
            if (syncResult.Status != PermissionTopologyCommandStatus.Success)
            {
                throw new InvalidOperationException($"同步角色 '{roleName}' 的权限失败。{string.Join(", ", syncResult.Errors)}");
            }
        }
    }

    private async Task<BzsRole> EnsureRoleAsync(string roleName)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is not null)
        {
            return role;
        }

        role = new BzsRole
        {
            Name = policy.NormalizeName(roleName),
            NormalizedName = policy.NormalizeKey(roleName),
        };
        var createResult = await roleManager.CreateAsync(role);
        var existingRole = createResult.Succeeded ? role : await roleManager.FindByNameAsync(roleName);
        if (existingRole is null)
        {
            throw new InvalidOperationException($"创建角色 '{roleName}' 失败。{string.Join(", ", createResult.Errors.Select(static error => error.Description))}");
        }

        return existingRole;
    }

    private void DetachRoleClaimAttempt(BzsRole? attemptedRole)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<IdentityRoleClaim<Guid>>().ToArray())
        {
            entry.State = EntityState.Detached;
        }

        if (attemptedRole is not null)
        {
            dbContext.Entry(attemptedRole).State = EntityState.Detached;
        }
    }

    private async Task RemoveObsoleteSeedResourcesAsync(CancellationToken cancellationToken)
    {
        var obsolete = await QueryResources().Where(resource => ObsoleteSeedResourceKeys.Contains(resource.Key)).ToArrayAsync(cancellationToken);
        if (obsolete.Length > 0)
        {
            dbContext.RemoveRange(obsolete);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyDictionary<string, string[]>> GetReleaseScopeMappingsAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(ReleaseScopeMappingsCacheKey, out IReadOnlyDictionary<string, string[]>? cached) && cached is not null) return cached;
        var records = await dbContext.PermissionDefinitions.AsNoTracking().Where(static permission => permission.IsActive && permission.Resource.IsActive).Select(static permission => new { permission.Name, Scopes = permission.ReleaseScopes.Select(static scope => scope.Scope).ToArray() }).ToListAsync(cancellationToken);
        var mappings = records.Where(static record => record.Scopes.Length > 0).ToDictionary(static record => record.Name, static record => record.Scopes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static scope => scope, StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);
        cache.Set(ReleaseScopeMappingsCacheKey, mappings, TimeSpan.FromMinutes(5));
        return mappings;
    }

    // Topology reads project all role assignments in one query instead of per-role claim lookups.
    private async Task<IReadOnlyDictionary<string, RolePermissionAssignmentResponse[]>> GetAssignedRolesByPermissionAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.RoleClaims.AsNoTracking()
            .Where(static claim => claim.ClaimType != null && claim.ClaimType.ToLower() == PermissionConstants.ClaimType)
            .Join(dbContext.Roles.AsNoTracking(), claim => claim.RoleId, role => role.Id, (claim, role) => new { claim.ClaimValue, role.Id, role.Name })
            .ToListAsync(cancellationToken);
        return rows.Where(static row => !string.IsNullOrWhiteSpace(row.ClaimValue))
            .GroupBy(row => PermissionDefinition.NormalizeName(row.ClaimValue!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .GroupBy(static row => row.Id)
                    .Select(static roleClaims => roleClaims.First())
                    .Select(row => new RolePermissionAssignmentResponse { RoleId = row.Id, RoleName = row.Name ?? row.Id.ToString(), Assigned = true })
                    .OrderBy(static role => role.RoleName, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyDictionary<Guid, string[]>> GetPermissionsByRoleAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.RoleClaims.AsNoTracking()
            .Where(static claim => claim.ClaimType != null && claim.ClaimType.ToLower() == PermissionConstants.ClaimType)
            .Select(static claim => new { claim.RoleId, claim.ClaimValue })
            .ToListAsync(cancellationToken);
        return rows.Where(static row => !string.IsNullOrWhiteSpace(row.ClaimValue))
            .GroupBy(static row => row.RoleId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(row => PermissionDefinition.NormalizeName(row.ClaimValue!)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static permission => permission, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<IReadOnlyList<string>> GetRolePermissionClaimsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var permissions = await GetPermissionsByRoleAsync(cancellationToken);
        return permissions.TryGetValue(roleId, out var values) ? values : [];
    }

    private IQueryable<ProtectedResource> QueryResources() => dbContext.ProtectedResources.Include(static resource => resource.Permissions).ThenInclude(static permission => permission.ReleaseScopes);
    private IQueryable<PermissionDefinition> QueryPermissions() => dbContext.PermissionDefinitions.Include(static permission => permission.Resource).Include(static permission => permission.ReleaseScopes);

    private static ProtectedResourceResponse ToResponse(ProtectedResource resource, IReadOnlyDictionary<string, RolePermissionAssignmentResponse[]> roleAssignments) => new()
    {
        Key = resource.Key,
        DisplayName = resource.DisplayName,
        Description = resource.Description,
        IsActive = resource.IsActive,
        ETag = ComputeETag(resource),
        Permissions = resource.Permissions.OrderBy(static permission => permission.Name, StringComparer.OrdinalIgnoreCase).Select(permission => ToResponse(permission, roleAssignments)).ToArray(),
    };

    private static PermissionDefinitionResponse ToResponse(PermissionDefinition permission, IReadOnlyDictionary<string, RolePermissionAssignmentResponse[]> roleAssignments) => new()
    {
        ResourceKey = permission.Resource.Key,
        Name = permission.Name,
        DisplayName = permission.DisplayName,
        Description = permission.Description,
        IsActive = permission.IsActive,
        ETag = ComputeETag(permission),
        ReleaseScopes = permission.ReleaseScopes.Select(static scope => scope.Scope).OrderBy(static scope => scope, StringComparer.OrdinalIgnoreCase).ToArray(),
        AssignedRoles = roleAssignments.TryGetValue(permission.Name, out var roles) ? roles : [],
    };

    private static string ComputeETag(ProtectedResource resource)
    {
        var value = $"{resource.Id:N}|{resource.Key}|{resource.DisplayName}|{resource.Description}|{resource.IsActive}";
        return ToETag(value);
    }

    private static string ComputeETag(PermissionDefinition permission)
    {
        var scopes = string.Join(',', permission.ReleaseScopes.Select(static scope => scope.Scope).OrderBy(static scope => scope, StringComparer.OrdinalIgnoreCase));
        var value = $"{permission.Id:N}|{permission.ResourceId:N}|{permission.Name}|{permission.DisplayName}|{permission.Description}|{permission.IsActive}|{scopes}";
        return ToETag(value);
    }

    private static string ToETag(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"\"{Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')}\"";
    }

    private RoleResponse ToRoleResponse(BzsRole role, IReadOnlyDictionary<Guid, string[]> permissionsByRole, bool includePermissions)
    {
        var permissions = permissionsByRole.TryGetValue(role.Id, out var values) ? values : [];
        return new RoleResponse { Id = role.Id, Name = role.Name ?? role.Id.ToString(), NormalizedName = role.NormalizedName ?? string.Empty, IsProtected = policy.IsProtectedRole(role), PermissionCount = permissions.Length, Permissions = includePermissions ? permissions : [], ETag = ComputeETag(role, permissions) };
    }

    private static string ComputeETag(BzsRole role, IReadOnlyCollection<string> permissions)
    {
        var value = $"{role.Id:N}|{role.ConcurrencyStamp}|{string.Join(',', permissions.OrderBy(static permission => permission, StringComparer.OrdinalIgnoreCase))}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"\"{Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')}\"";
    }

    private static PermissionTopologyCommandResult<T> FromIdentityFailure<T>(IdentityResult result) => PermissionTopologyCommandResult<T>.Failure(PermissionTopologyCommandStatus.ValidationFailed, result.Errors.Select(static error => error.Description).ToArray());

    private static IReadOnlyDictionary<string, RolePermissionAssignmentResponse[]> EmptyRoleAssignments { get; } = new Dictionary<string, RolePermissionAssignmentResponse[]>(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyDictionary<Guid, string[]> EmptyRolePermissions { get; } = new Dictionary<Guid, string[]>();
}
