using BootstrapBlazor.Components;
using BzsOIDC.Idp.Components;
using BzsOIDC.Idp.Client.Services.Dashboard;
using BzsOIDC.Idp.Infra;
using BzsOIDC.Idp.Infra.Preferences;
using BzsOIDC.Idp.Services.Admin;
using BzsOIDC.Idp.Services;
using BzsOIDC.Idp.Services.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddIdpService(builder.Configuration, builder.Environment);
builder.Services.AddIdpAuthorization();
builder.EnrichFromAspire();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAdminDashboardClient(serviceProvider =>
{
    var httpContextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
    var request = httpContextAccessor.HttpContext?.Request
        ?? throw new InvalidOperationException("The current HTTP request is unavailable for dashboard client initialization.");

    return new UriBuilder
    {
        Scheme = request.Scheme,
        Host = request.Host.Host,
        Port = request.Host.Port ?? -1,
        Path = request.PathBase.HasValue
            ? $"{request.PathBase.Value!.TrimEnd('/')}/"
            : "/"
    }.Uri;
});
builder.Services.AddScoped<IAdminDashboardClient, ServerAdminDashboardClient>();
builder.Services.AddBootstrapBlazor();
builder.Services.AddLocalization(options => { options.ResourcesPath = "Resources"; });
builder.Services.AddCascadingAuthenticationState();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = UiPreferences.SupportedCultureNames
        .Select(static name => new CultureInfo(name))
        .ToArray();

    options.DefaultRequestCulture = new RequestCulture(UiPreferences.DefaultCulture);
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    options.RequestCultureProviders =
    [
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider(),
    ];
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();
builder.Services.AddControllers();

var app = builder.Build();

if (builder.Configuration.IsSmokeTestingEnabled())
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<IdpDbContext>();
    var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();

    await dbContext.Database.EnsureDeletedAsync();
    await dbContext.Database.EnsureCreatedAsync();
    await seeder.SeedAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BzsOIDC.Idp.Client._Imports).Assembly);

app.Run();
