using Microsoft.Extensions.DependencyInjection;
using BzsOIDC.Idp.Client.Services.Session;

namespace BzsOIDC.Idp.Client.Services.Dashboard;

public static class DashboardServiceExtensions
{
    public static IServiceCollection AddAdminDashboardClient(this IServiceCollection services, Func<IServiceProvider, Uri> baseAddressFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddressFactory);

        services.AddScoped<IAdminDashboardClient>(serviceProvider =>
        {
            var apiFactory = serviceProvider.GetService<IApiHttpClientFactory>();
            if (apiFactory is not null)
            {
                return new AdminDashboardClient(apiFactory.CreateClient());
            }

            var handler = serviceProvider.GetService<ApiRequestHandler>();
            HttpClient httpClient;
            if (handler is not null)
            {
                handler.InnerHandler ??= new HttpClientHandler();
                httpClient = new HttpClient(handler);
            }
            else
            {
                httpClient = new HttpClient();
            }

            httpClient.BaseAddress = baseAddressFactory(serviceProvider);

            return new AdminDashboardClient(httpClient);
        });

        return services;
    }
}
