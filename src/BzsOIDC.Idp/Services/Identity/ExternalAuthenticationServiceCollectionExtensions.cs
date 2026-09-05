using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace BzsOIDC.Idp.Services.Identity;

internal static class ExternalAuthenticationServiceCollectionExtensions
{
    private const string AuthenticationSectionName = "Authentication";

    internal static IServiceCollection AddExternalAuthenticationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExternalAuthenticationOptions>()
            .Bind(configuration.GetSection(AuthenticationSectionName))
            .Validate(static options =>
                options.GitHub.IsConfigured ||
                (string.IsNullOrWhiteSpace(options.GitHub.ClientId) && string.IsNullOrWhiteSpace(options.GitHub.ClientSecret)),
                "Authentication:GitHub must specify both ClientId and ClientSecret.")
            .ValidateOnStart();

        services.AddSingleton<IExternalLoginProviderStore, ExternalLoginProviderStore>();
        services.AddScoped<IExternalLoginService, ExternalLoginService>();

        return services;
    }

    internal static AuthenticationBuilder AddConfiguredExternalAuthenticationProviders(
        this AuthenticationBuilder authenticationBuilder,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(AuthenticationSectionName).Get<ExternalAuthenticationOptions>() ?? new ExternalAuthenticationOptions();

        if (options.GitHub.IsConfigured)
        {
            authenticationBuilder.AddGitHub(gitHubOptions =>
            {
                gitHubOptions.SignInScheme = IdentityConstants.ExternalScheme;
                gitHubOptions.ClientId = options.GitHub.ClientId;
                gitHubOptions.ClientSecret = options.GitHub.ClientSecret;
                gitHubOptions.CallbackPath = options.GitHub.CallbackPath;
                gitHubOptions.Scope.Add("user:email");
                gitHubOptions.ClaimActions.MapJsonKey("urn:github:name", "name");
                gitHubOptions.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
                gitHubOptions.Events.OnRemoteFailure = async context =>
                {
                    var loggerFactory = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>();
                    var logger = loggerFactory.CreateLogger("GitHubAuthentication");
                    logger.LogWarning(context.Failure, "GitHub remote authentication failed.");

                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "https://httpstatuses.com/502",
                        title = "External authentication failed",
                        status = StatusCodes.Status502BadGateway,
                        code = "external_login_failed",
                        traceId = context.HttpContext.TraceIdentifier,
                    });
                    context.HandleResponse();
                    return;
                };
            });
        }

        return authenticationBuilder;
    }
}
