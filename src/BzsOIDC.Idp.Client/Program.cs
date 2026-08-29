using System.Globalization;
using ApexCharts;
using Bzs.Blazor;
using BzsOIDC.Idp.Client.Services.Dashboard;
using BzsOIDC.Idp.Client.Services.Session;
using BzsOIDC.Idp.Client.Services.Users;
using BzsOIDC.Idp.Client.Services.Scopes;
using BzsOIDC.Idp.Client.Services.Permissions;
using BzsOIDC.Idp.Client.Services.Oidc;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddLocalization(options =>
{
    options.ResourcesPath = "Resources";
});
builder.Services.AddAuthorizationCore();
builder.Services.AddBzsBlazor();
builder.Services.AddAuthenticationStateDeserialization();
builder.Services.AddAdminDashboardClient(_ => new Uri(builder.HostEnvironment.BaseAddress));
builder.Services.AddClientSessionLifecycle(_ => new Uri(builder.HostEnvironment.BaseAddress));
builder.Services.AddUsersClient();
builder.Services.AddScopesClient();
builder.Services.AddOidcClientsClient();
builder.Services.AddPermissionTopologyClient();
builder.Services.AddOidcTopologyClient();
builder.Services.AddApexCharts();

var host = builder.Build();

var jsRuntime = host.Services.GetRequiredService<IJSRuntime>();
var requestCultureCookie = await jsRuntime.InvokeAsync<string?>("bzsPreferences.getCultureCookie");
var cultureName = ResolveCultureName(requestCultureCookie);
var culture = new CultureInfo(cultureName);

CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
CultureInfo.CurrentCulture = culture;
CultureInfo.CurrentUICulture = culture;

await host.RunAsync();

static string ResolveCultureName(string? requestCultureCookie)
{
    const string defaultCulture = "zh-CN";

    if (string.IsNullOrWhiteSpace(requestCultureCookie))
    {
        return defaultCulture;
    }

    var segments = requestCultureCookie.Split('|', StringSplitOptions.RemoveEmptyEntries);

    foreach (var segment in segments)
    {
        var kv = segment.Split('=', 2);
        if (kv.Length != 2)
        {
            continue;
        }

        if (string.Equals(kv[0], "c", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(kv[1], "en-US", StringComparison.OrdinalIgnoreCase))
        {
            return "en-US";
        }

        if (string.Equals(kv[0], "c", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(kv[1], "zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-CN";
        }
    }

    return defaultCulture;
}
