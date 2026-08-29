using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace BzsOIDC.Idp.Client.Services.Session;

public static class SessionServiceExtensions
{
    public static IServiceCollection AddClientSessionLifecycle(this IServiceCollection services, Func<IServiceProvider, Uri> baseAddressFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddressFactory);

        services.AddScoped<ISessionClient>(sp => new SessionClient(new HttpClient { BaseAddress = baseAddressFactory(sp) }));
        services.AddScoped<SessionAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<SessionAuthenticationStateProvider>());
        services.AddScoped<SessionLifecycle>();
        services.AddScoped<IAntiforgeryTokenStore>(sp => new AntiforgeryTokenStore(new HttpClient { BaseAddress = baseAddressFactory(sp) }));
        services.AddScoped<ApiRequestHandler>();
        services.AddScoped<IApiHttpClientFactory>(sp =>
            new ApiHttpClientFactory(sp.GetRequiredService<ApiRequestHandler>(), baseAddressFactory, sp));
        return services;
    }
}
