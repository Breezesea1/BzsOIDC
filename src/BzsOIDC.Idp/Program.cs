using BzsOIDC.Idp.Infra;
using BzsOIDC.Idp.Infra.Http;
using BzsOIDC.Idp.Services;
using BzsOIDC.Idp.Services.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Threading.RateLimiting;
using BzsOIDC.Contracts;
using BzsOIDC.Shared.Infrastructure.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddIdpService(builder.Configuration, builder.Environment);
builder.Services.AddIdpAuthorization();
builder.EnrichFromAspire();

// Add services to the container.
builder.Services.AddControllersWithViews(options => options.Filters.Add<ApiProblemDetailsResultFilter>())
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(static item => item.Value?.Errors.Count > 0)
                .ToDictionary(
                    static item => item.Key,
                    static _ => new[] { ApiErrorCodes.ValidationFailed },
                    StringComparer.Ordinal);

            return new ObjectResult(new ApiProblemDetails(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationFailed,
                context.HttpContext.TraceIdentifier,
                errors))
            {
                StatusCode = StatusCodes.Status400BadRequest,
                ContentTypes = { "application/problem+json" },
            };
        };
    });
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy<string>("account", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

var app = builder.Build();

// Apply the backend security headers before any API or protocol response.
app.UseStaticAssetHardening();

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
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        await ApiProblemDetailsWriter.WriteAsync(
            context,
            StatusCodes.Status500InternalServerError,
            ApiErrorCodes.Unexpected);
    }));
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// Development intentionally keeps the detailed developer exception page for HTML routes,
// but API callers must receive the same safe envelope in every environment.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException) when (ApiProblemDetailsWriter.IsApiRequest(context.Request))
    {
        if (!context.Response.HasStarted)
        {
            await ApiProblemDetailsWriter.WriteAsync(
                context,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.AntiforgeryFailed);
        }
    }
    catch (Exception) when (ApiProblemDetailsWriter.IsApiRequest(context.Request))
    {
        if (!context.Response.HasStarted)
        {
            await ApiProblemDetailsWriter.WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.Unexpected);
        }
    }
});

app.UseWhen(
    context => ApiProblemDetailsWriter.IsApiRequest(context.Request),
    apiBranch => apiBranch.UseStatusCodePages(async statusContext =>
    {
        var context = statusContext.HttpContext;
        if (context.Response.StatusCode >= StatusCodes.Status400BadRequest && !context.Response.HasStarted)
        {
            await ApiProblemDetailsWriter.WriteAsync(
                context,
                context.Response.StatusCode,
                ApiProblemDetailsWriter.CodeForStatus(context.Response.StatusCode));
        }
    }));
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.UseAntiforgery();

app.MapControllers();

app.Run();
