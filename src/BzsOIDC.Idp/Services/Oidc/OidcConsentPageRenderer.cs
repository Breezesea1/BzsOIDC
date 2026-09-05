using System.Net;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace BzsOIDC.Idp.Services.Oidc;

public interface IOidcConsentPageRenderer
{
    ContentResult Render(
        HttpContext httpContext,
        IEnumerable<KeyValuePair<string, StringValues>> query,
        string clientDisplayName,
        IReadOnlyList<string> scopes);
}

internal sealed class OidcConsentPageRenderer(IAntiforgery antiforgery) : IOidcConsentPageRenderer
{
    // Keep this as a protocol-owned minimal document: the authorization endpoint must be able to
    // return a consent form without depending on Blazor circuit state or client-side assets.
    public ContentResult Render(
        HttpContext httpContext,
        IEnumerable<KeyValuePair<string, StringValues>> query,
        string clientDisplayName,
        IReadOnlyList<string> scopes)
    {
        var tokens = antiforgery.GetAndStoreTokens(httpContext);
        var formAction = httpContext.Request.PathBase.Add(httpContext.Request.Path).Value ?? "/connect/authorize";
        var builder = new StringBuilder();

        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("<title>Authorize application - BzsOIDC</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("""
:root {
    color-scheme: light;
    --canvas: #e2e7ee;
    --surface: #f1f4f8;
    --surface-inset: #d9dfe8;
    --surface-overlay: #f7f9fc;
    --text: #1d2735;
    --muted: #5d6b7b;
    --border: #9eabbc;
    --primary: #315ea8;
    --on-primary: #ffffff;
    --success: #2f8067;
    --shadow-raised: 8px 8px 16px rgb(157 169 185 / .62), -8px -8px 16px rgb(255 255 255 / .86);
    --shadow-inset: inset 4px 4px 8px rgb(157 169 185 / .58), inset -4px -4px 8px rgb(255 255 255 / .78);
}

@media (prefers-color-scheme: dark) {
    :root {
        color-scheme: dark;
        --canvas: #151b24;
        --surface: #222b37;
        --surface-inset: #111720;
        --surface-overlay: #283341;
        --text: #edf1f6;
        --muted: #a8b3c2;
        --border: #657487;
        --primary: #9cb9ef;
        --on-primary: #172235;
        --success: #69d0a7;
        --shadow-raised: 8px 8px 16px rgb(5 8 13 / .72), -8px -8px 16px rgb(54 67 83 / .42);
        --shadow-inset: inset 4px 4px 8px rgb(5 8 13 / .72), inset -4px -4px 8px rgb(54 67 83 / .34);
    }
}

* { box-sizing: border-box; }
body {
    min-block-size: 100dvh;
    margin: 0;
    background: var(--canvas);
    color: var(--text);
    font-family: "Segoe UI Variable", "Segoe UI", ui-sans-serif, system-ui, sans-serif;
    line-height: 1.5;
    -webkit-font-smoothing: antialiased;
}
.consent-page {
    position: relative;
    isolation: isolate;
    display: grid;
    min-block-size: 100dvh;
    place-items: center;
    overflow: hidden;
    padding: clamp(1rem, 5vw, 3rem);
    background: var(--canvas);
}
.consent-page::before,
.consent-page::after {
    position: absolute;
    z-index: -1;
    border-radius: 50%;
    content: "";
    filter: blur(80px);
    pointer-events: none;
}
.consent-page::before {
    inline-size: 28rem;
    block-size: 28rem;
    inset-block-start: -12rem;
    inset-inline-start: -10rem;
    background: rgb(49 94 168 / .18);
}
.consent-page::after {
    inline-size: 24rem;
    block-size: 24rem;
    inset-block-end: -12rem;
    inset-inline-end: -8rem;
    background: rgb(47 128 103 / .15);
}
.consent-card {
    display: grid;
    inline-size: min(46rem, 100%);
    gap: 1.25rem;
    align-content: start;
    border-radius: 1.125rem;
    padding: clamp(1.5rem, 4vw, 2.5rem);
    background: var(--surface);
    box-shadow: var(--shadow-raised);
}
.consent-brand {
    display: flex;
    align-items: center;
    gap: .75rem;
    color: var(--text);
    font-weight: 700;
    letter-spacing: .01em;
}
.consent-brand__mark {
    display: grid;
    inline-size: 2.5rem;
    block-size: 2.5rem;
    place-items: center;
    border-radius: .8rem;
    background: var(--surface-inset);
    box-shadow: var(--shadow-inset);
    color: var(--primary);
    font-size: 1.125rem;
}
.consent-kicker {
    margin: 0;
    color: var(--primary);
    font-size: .75rem;
    font-weight: 700;
    letter-spacing: .08em;
    text-transform: uppercase;
}
.consent-card h1 {
    margin: 0;
    font-size: clamp(1.35rem, 3vw, 1.75rem);
    line-height: 1.25;
}
.consent-card p { margin: 0; color: var(--muted); }
.consent-card__header { display: grid; gap: .4rem; }
.scope-list {
    display: grid;
    gap: .6rem;
    margin: 0;
    padding: 0;
    list-style: none;
}
.scope {
    display: flex;
    align-items: flex-start;
    gap: .7rem;
    border-radius: .75rem;
    padding: .75rem .875rem;
    background: var(--surface-inset);
    box-shadow: var(--shadow-inset);
}
.scope::before {
    flex: 0 0 1rem;
    inline-size: 1rem;
    block-size: 1rem;
    margin-block-start: .2rem;
    border: 2px solid currentColor;
    border-radius: 50%;
    color: var(--success);
    content: "";
}
.actions {
    display: flex;
    flex-wrap: wrap;
    gap: .75rem;
    margin-block-start: .25rem;
}
.actions button {
    min-block-size: 2.75rem;
    flex: 1 1 12rem;
    border: 0;
    border-radius: .75rem;
    padding: .55rem 1.15rem;
    font: inherit;
    font-weight: 650;
    cursor: pointer;
    transition: box-shadow 180ms ease, transform 180ms ease, filter 180ms ease;
}
.actions button:hover { filter: brightness(1.05); }
.actions button:active { box-shadow: var(--shadow-inset); transform: translateY(1px); }
.actions button:focus-visible { outline: 2px solid var(--primary); outline-offset: 2px; }
.primary { background: var(--primary); color: var(--on-primary); box-shadow: var(--shadow-raised); }
.secondary { background: var(--surface-overlay); color: var(--text); box-shadow: var(--shadow-raised); }
@media (max-width: 38rem) {
    .actions { display: grid; }
    .actions button { flex-basis: auto; }
}
@media (prefers-reduced-motion: reduce) {
    .actions button { transition-duration: .01ms; }
}
""");
        builder.AppendLine("</style>");
        builder.AppendLine("</head><body><main class=\"consent-page\"><section class=\"consent-card\" aria-labelledby=\"consent-heading\">");
        builder.AppendLine("<div class=\"consent-brand\"><span class=\"consent-brand__mark\" aria-hidden=\"true\">B</span><span>BzsOIDC</span></div>");
        builder.Append("<div class=\"consent-card__header\"><p class=\"consent-kicker\">BzsOIDC authorization request</p><h1 id=\"consent-heading\">Allow ")
            .Append(WebUtility.HtmlEncode(clientDisplayName))
            .AppendLine(" to access your account?</h1></div>");
        builder.AppendLine("<p>The application is requesting the following scopes.</p><ul class=\"scope-list\">");

        foreach (var scope in scopes)
        {
            builder.Append("<li class=\"scope\">")
                .Append(WebUtility.HtmlEncode(scope))
                .AppendLine("</li>");
        }

        builder.Append("</ul><form method=\"post\" action=\"")
            .Append(WebUtility.HtmlEncode(formAction))
            .AppendLine("\">");
        builder.Append("<input name=\"")
            .Append(WebUtility.HtmlEncode(tokens.FormFieldName))
            .Append("\" type=\"hidden\" value=\"")
            .Append(WebUtility.HtmlEncode(tokens.RequestToken))
            .AppendLine("\">");

        foreach (var pair in query)
        {
            foreach (var value in pair.Value)
            {
                builder.Append("<input type=\"hidden\" name=\"")
                    .Append(WebUtility.HtmlEncode(pair.Key))
                    .Append("\" value=\"")
                    .Append(WebUtility.HtmlEncode(value))
                    .AppendLine("\">");
            }
        }

        builder.AppendLine("<div class=\"actions\"><button class=\"primary\" type=\"submit\" name=\"consent\" value=\"accept\">Allow access</button>");
        builder.AppendLine("<button class=\"secondary\" type=\"submit\" name=\"consent\" value=\"deny\">Deny</button></div>");
        builder.AppendLine("</form></section></main></body></html>");

        return new ContentResult
        {
            Content = builder.ToString(),
            ContentType = "text/html; charset=utf-8",
        };
    }
}
