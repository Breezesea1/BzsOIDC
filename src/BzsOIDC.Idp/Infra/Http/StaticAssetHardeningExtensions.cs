using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BzsOIDC.Idp.Infra.Http;

/// <summary>
/// Applies backend security headers and conservative cache policy to responses.
/// </summary>
public static partial class StaticAssetHardeningExtensions
{
    private const string ImmutableCacheControl = "public,max-age=31536000,immutable";
    private const string DocumentCacheControl = "no-cache,must-revalidate";

    public const string ContentSecurityPolicyReportOnly =
        "default-src 'none'; base-uri 'none'; object-src 'none'; frame-ancestors 'none'; " +
        "form-action 'self'; connect-src 'self'";

    /// <summary>
    /// Adds report-only CSP and baseline security headers, and gives
    /// fingerprinted static assets immutable caching while allowing the HTML
    /// shell to revalidate on every deployment.
    /// </summary>
    public static IApplicationBuilder UseStaticAssetHardening(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var httpContext = (HttpContext)state;
                ApplyHeaders(httpContext);
                return Task.CompletedTask;
            }, context);

            await next();
        });

        return app;
    }

    public static bool IsImmutableAssetPath(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return FingerprintedFileNameRegex().IsMatch(Path.GetFileName(value));
    }

    public static bool IsDocumentPath(PathString path)
    {
        var value = path.Value;
        return !string.IsNullOrEmpty(value) &&
            (value.Equals("/", StringComparison.Ordinal) ||
             value.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyHeaders(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.TryAdd("X-Content-Type-Options", "nosniff");
        headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
        headers.TryAdd("Permissions-Policy", "camera=(), geolocation=(), microphone=()");
        headers.TryAdd("X-Frame-Options", "DENY");
        headers.TryAdd("Content-Security-Policy-Report-Only", ContentSecurityPolicyReportOnly);

        if (context.Response.StatusCode is < StatusCodes.Status200OK or >= StatusCodes.Status400BadRequest ||
            headers.ContainsKey("Cache-Control"))
        {
            return;
        }

        if (IsImmutableAssetPath(context.Request.Path))
        {
            headers.CacheControl = ImmutableCacheControl;
        }
        else if (IsDocumentPath(context.Request.Path))
        {
            headers.CacheControl = DocumentCacheControl;
        }
    }

    [GeneratedRegex(@"(?:^|[._-])[0-9a-f]{8,}(?:[._-]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintedFileNameRegex();
}
