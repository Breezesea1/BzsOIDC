using BzsOIDC.Idp.Infra;
using BzsOIDC.Idp.Infra.Oidc;
using BzsOIDC.Idp.Services.Admin;
using BzsOIDC.Idp.Services.Authorization;
using BzsOIDC.Idp.Services.Identity;
using BzsOIDC.Idp.Services.Oidc;
using BzsOIDC.Shared.Infrastructure.Cache;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BzsOIDC.Idp.Services;

internal static class ServiceExtensions
{
    /// <summary>
    /// 执行EnrichFromAspire。
    /// </summary>
    /// <param name="builder">参数builder。</param>
    internal static void EnrichFromAspire(this WebApplicationBuilder builder)
    {
        if (builder.Configuration.ShouldEnrichIdpDbFromAspire())
        {
            builder.EnrichNpgsqlDbContext<IdpDbContext>();
        }
    }


    /// <summary>
    /// 向服务集合注册 Identity Provider (IDP) 服务。
    /// </summary>
    /// <param name="sc">服务集合。</param>
    /// <param name="configuration">应用程序配置。</param>
    /// <returns>服务集合，用于链式调用。</returns>
    internal static IServiceCollection AddIdpService(this IServiceCollection sc, IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        sc.AddForwardedHeaders();
        sc.AddAntiforgery();
        sc.AddMemoryCache();
        sc.AddBzsCache(configuration);

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString) && configuration.IsSmokeTestingEnabled())
        {
            connectionString = "Data Source=smoke-idp.db";
        }

        ArgumentException.ThrowIfNullOrEmpty(connectionString);

        sc.AddInfraServices(connectionString);
        sc.AddExternalAuthenticationServices(configuration);

        var registrar = new IdpServiceRegistrar(sc, configuration, hostEnvironment);
        registrar.AddIdpOptions();
        registrar.AddDataProtection();
        registrar.AddOidc();

        sc.AddScoped<IRoleService, RoleService>();
        sc.AddScoped<RoleManagementPolicy>();
        sc.AddScoped<IRoleManagementService, RoleManagementService>();
        sc.AddScoped<IUserService, UserService>();
        sc.AddScoped<IRolePermissionService, RolePermissionService>();
        sc.AddScoped<IPermissionCatalogService, PermissionCatalogService>();
        sc.AddScoped<IOidcPrincipalFactory, OidcPrincipalFactory>();
        sc.AddScoped<IOidcClientService, OidcClientService>();
        sc.AddScoped<IOidcScopeService, OidcScopeService>();
        sc.AddScoped<IAdminDashboardService, AdminDashboardService>();
        sc.AddScoped<IdentitySeeder>();

        return sc;
    }

    internal static IServiceCollection AddIdpAuthorization(this IServiceCollection sc)
    {
        sc.AddAuthorization();
        sc.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        sc.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        return sc;
    }
}
