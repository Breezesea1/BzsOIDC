# Use hosted WebAssembly for the user interface

BzsOIDC will use a same-origin hosted Blazor WebAssembly application for every user-visible page, while the ASP.NET Core host continues to own Identity, OpenIddict protocol processing, secured APIs, and static asset delivery. Interactive Server, Blazor circuits, and server-rendered Razor UI will be removed; public page routes and OIDC protocol behavior will remain compatible. Bzs.Blazor is the primary UI system, with specialized libraries retained only where it does not provide the capability; BootstrapBlazor, QuickGrid, and the old Neo component layer will be removed.

The existing `BzsOIDC.Idp` project remains the backend and static host, while `BzsOIDC.Idp.Client` becomes the complete browser application and owns the router, layout, and pages. A minimal static startup document may show loading and recovery states, but the server will own no Razor UI.

The browser application will authenticate with the existing same-origin, HttpOnly ASP.NET Core Identity cookie. It may retrieve a non-sensitive session and permission summary, but it will not act as an OIDC client of its own identity provider or store access tokens. Server authorization remains authoritative at every API and protocol endpoint.

Local account actions use same-origin JSON APIs, while external authentication and OIDC redirects remain top-level browser navigations. A browser-safe `BzsOIDC.Contracts` project owns transport DTOs and stable error codes without referencing ASP.NET Core, Identity, EF Core, or OpenIddict implementation types. The session endpoint returns a non-cacheable authenticated/anonymous summary; protected APIs continue to return authoritative 401 and 403 responses.

The UI will use Bzs.Blazor's built-in light and dark themes, with compact density for administration and comfortable density for account and consent flows. Theme and culture preferences are browser-local and applied before the WASM host starts. The first release targets current evergreen browsers without offline/PWA support or WebAssembly AOT.

The migration will first establish the required HTTP API contracts and WASM pages, then switch the application once and remove the old Server UI. UI components must not call database-backed services or depend on `HttpContext`. UI migration begins after a released Bzs.Blazor package supplies the required password, text/search binding, DataGrid refresh, navigation activation, and responsive-drawer focus contracts.
