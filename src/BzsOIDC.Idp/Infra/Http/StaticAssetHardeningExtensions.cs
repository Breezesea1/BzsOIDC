using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace BzsOIDC.Idp.Infra.Http;

/// <summary>
/// Applies the host-level security and cache policy shared by the Server and
/// hosted WebAssembly frontends. This remains middleware (rather than a
/// service-worker policy) so rollback and an old browser document continue to
/// use the server's current asset manifest.
/// </summary>
public static partial class StaticAssetHardeningExtensions
{
    private const string ImmutableCacheControl = "public,max-age=31536000,immutable";
    private const string DocumentCacheControl = "no-cache,must-revalidate";

    public const string ContentSecurityPolicyReportOnly =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
        "script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; connect-src 'self' ws: wss:; worker-src 'self'";

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

        // ASP.NET Core's framework and static web asset endpoints are content
        // addressed by the generated manifest. Keep these URLs valid for the
        // entire rollback window.
        if (value.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("/_content/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
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
