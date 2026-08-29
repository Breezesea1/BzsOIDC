using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.FileProviders;
using BzsOIDC.Idp.Infra.Http;

namespace BzsOIDC.Idp.IntegrationTests;

public sealed class HostedWasmFallbackIntegrationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [Fact]
    public async Task WasmDeepLink_UsesClientIndex_WhileApiRouteWins()
    {
        using var deepLinkResponse = await _client.GetAsync("/admin/clients");
        Assert.Equal(HttpStatusCode.OK, deepLinkResponse.StatusCode);
        Assert.Equal("text/html", deepLinkResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-cache", deepLinkResponse.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.True(deepLinkResponse.Headers.Contains("Content-Security-Policy-Report-Only"));
        var index = await deepLinkResponse.Content.ReadAsStringAsync();
        Assert.Contains("blazor.webassembly.js", index, StringComparison.Ordinal);
        Assert.DoesNotContain("blazor.server.js", index, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/_blazor/", index, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("service-worker", index, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manifest.webmanifest", index, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<base href=\"/\"", index, StringComparison.Ordinal);

        using var apiResponse = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.NoContent, apiResponse.StatusCode);
    }

    public async Task InitializeAsync()
    {
        var clientRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/BzsOIDC.Idp.Client/wwwroot"));

        Assert.True(File.Exists(Path.Combine(clientRoot, "index.html")),
            $"Hosted client index was not found at '{clientRoot}'.");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            WebRootPath = clientRoot,
        });
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.UseStaticAssetHardening();
        _app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(clientRoot),
        });
        _app.MapGet("/api/health", () => Results.NoContent());
        _app.MapFallbackToFile("{*path:nonfile}", "index.html");

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
