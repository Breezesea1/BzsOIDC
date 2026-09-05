using Microsoft.Extensions.DependencyInjection;

namespace BzsOIDC.Idp.Services.Identity;

internal static class IdentityServiceCollectionExtensions
{
    internal static IServiceCollection AddIdentityPrincipalServices(this IServiceCollection services)
    {
        services.AddScoped<IIdentitySubjectReader, IdentitySubjectReader>();
        services.AddScoped<IIdentityPrincipalFactory, IdentityPrincipalFactory>();
        services.AddScoped<IIdentitySessionReader, IdentitySessionReader>();
        return services;
    }
}
