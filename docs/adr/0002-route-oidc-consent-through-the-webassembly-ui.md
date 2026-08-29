# Route OIDC consent through the WebAssembly UI

When `/connect/authorize` requires user consent, it will redirect the browser to the WebAssembly consent route while preserving the original OIDC request parameters. The browser application obtains a server-validated, display-only preview of the OIDC client and requested release scopes, then submits the decision with antiforgery protection as a top-level form POST to `/connect/authorize`; OpenIddict revalidates the complete request and owns the final protocol redirect.

The preview is never authorization evidence, `prompt=none` never enters the interactive UI, and consent submission is not performed through `fetch` because protocol redirects must replace the top-level browser document.
