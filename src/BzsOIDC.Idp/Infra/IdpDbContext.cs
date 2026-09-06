using BzsOIDC.Idp.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BzsOIDC.Idp.Infra;

public sealed class IdpDbContext(DbContextOptions<IdpDbContext> options)
    : IdentityDbContext<BzsUser, BzsRole, Guid>(options)
{
    public DbSet<ProtectedResource> ProtectedResources => Set<ProtectedResource>();
    public DbSet<PermissionDefinition> PermissionDefinitions => Set<PermissionDefinition>();

    /// <summary>
    /// 配置实体模型并应用程序集中的实体配置。
    /// </summary>
    /// <param name="builder">模型构建器。</param>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // 先调用基类以配置 Identity 模型
        base.OnModelCreating(builder);

        // 从当前程序集自动应用实体类型配置
        builder.ApplyConfigurationsFromAssembly(typeof(IdpDbContext).Assembly);
    }
}
