namespace BzsOIDC.Idp.Client.Services.Session;

public interface IApiHttpClientFactory
{
    HttpClient CreateClient();
}

public sealed class ApiHttpClientFactory(
    ApiRequestHandler handler,
    Func<IServiceProvider, Uri> baseAddressFactory,
    IServiceProvider serviceProvider) : IApiHttpClientFactory
{
    public HttpClient CreateClient()
    {
        handler.InnerHandler ??= new HttpClientHandler();
        return new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = baseAddressFactory(serviceProvider)
        };
    }
}
