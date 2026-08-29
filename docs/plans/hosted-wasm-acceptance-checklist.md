# Hosted WebAssembly acceptance checklist

This checklist is the release gate for the BzsOIDC frontend migration. Every checked item requires command output, an automated test, a browser trace, a screenshot, or a recorded manual verification result.

## Prerequisite and dependency boundary

- [ ] BzsOIDC references a released, fixed Bzs.Blazor NuGet version.
- [ ] No absolute or local project reference points to `D:\Coding\Bzs.Blazor`.
- [ ] The package supplies the five required contracts listed in the migration plan.
- [ ] Bzs.Blazor CSS and collocated JavaScript load from fingerprinted static assets.
- [ ] Exactly one Bzs overlay host exists in the WASM application root.

## Project topology

- [ ] `BzsOIDC.Idp.Client` owns `wwwroot/index.html`, `App.razor`, Router, layout, and every page.
- [ ] `BzsOIDC.Idp` contains no `.razor` files or root UI component.
- [ ] Client has no reference to Server, EF Core, Identity, OpenIddict server implementations, or `HttpContext`.
- [ ] `BzsOIDC.Contracts` is browser-safe and contains no UI-framework or server-implementation dependency.
- [ ] Controller, API, and OIDC routes are mapped before the SPA fallback.

## Blazor Server removal

- [ ] No `AddInteractiveServerComponents` remains.
- [ ] No `AddInteractiveServerRenderMode` remains.
- [ ] No `InteractiveServer` render declaration remains.
- [ ] No circuit, Hub, reconnect modal, or authentication-state serialization remains.
- [ ] Browser traces contain no `/_blazor/negotiate` request and no Server-circuit WebSocket.
- [ ] Direct navigation and refresh work for every WASM route.

Suggested evidence commands:

```powershell
rg -n "AddInteractiveServerComponents|AddInteractiveServerRenderMode|InteractiveServer|components-reconnect-modal|/_blazor/negotiate" src tests
rg --files src/BzsOIDC.Idp -g "*.razor"
rg -n "IHttpContextAccessor|HttpContext|EntityFrameworkCore|OpenIddict.Server" src/BzsOIDC.Idp.Client
```

All three searches must return no disallowed application references. Documentation and generated output should be excluded when necessary.

## Obsolete frontend removal

- [ ] BootstrapBlazor package, imports, root provider, CSS, and JavaScript are removed.
- [ ] QuickGrid package and imports are removed.
- [ ] Neo components and their JavaScript are removed.
- [ ] Tailwind dependencies, npm manifests, build targets, source CSS, and generated bundle are removed.
- [ ] GSAP dependency, copied asset, scripts, and LoginHero are removed.
- [ ] Old sidebar, preferences, resizable-table, dialog-shell, and page-specific scripts are removed when Bzs.Blazor replaces them.
- [ ] ApexCharts remains only where the migrated Dashboard uses it.

Suggested evidence command:

```powershell
rg -n "BootstrapBlazor|QuickGrid|Neo(Field|Panel|Select|Switch)|tailwind|gsap|LoginHero" src tests
```

## Session and account behavior

- [ ] Anonymous session summary returns 200, `authenticated=false`, and `Cache-Control: no-store`.
- [ ] Authenticated session summary exposes only approved display identity, roles, and permissions.
- [ ] Ordinary login creates a browser-session cookie with a twelve-hour sliding server ticket.
- [ ] Remember-me login creates a persistent cookie with a fourteen-day maximum.
- [ ] Authentication Cookie remains Secure, HttpOnly, SameSite=Lax, and scoped to `/`.
- [ ] Login, registration, and ordinary logout use JSON APIs and structured errors.
- [ ] Unknown, invalid, locked, and disallowed login outcomes are externally indistinguishable.
- [ ] External login uses top-level navigation and returns safely to a validated local URL.
- [ ] Password, security-stamp, and critical permission changes invalidate affected sessions.
- [ ] Account endpoints are rate limited and tested at their boundaries.

## Antiforgery

- [ ] Antiforgery bootstrap returns the request token, header name, and form-field name without caching.
- [ ] The request token is held only in WASM memory.
- [ ] Login, registration, logout, consent, and every administration mutation reject missing or invalid tokens.
- [ ] Login and logout replace the request token after the identity changes.
- [ ] An explicit antiforgery failure may retry once; other write failures never auto-retry.
- [ ] Passwords, secrets, Cookies, and antiforgery tokens do not appear in logs or browser persistence.

## Authorization and errors

- [ ] Protected API authentication failures return 401 Problem Details without redirecting.
- [ ] Permission failures return 403 Problem Details without clearing the valid session.
- [ ] Missing `If-Match` returns 428 `precondition_required`.
- [ ] Stale ETags return 412 `concurrency_conflict`.
- [ ] Validation responses use stable field error codes.
- [ ] Unexpected failures expose a generic 500 response and a usable `traceId`, not exception detail.
- [ ] Foreground 401 navigation preserves only a validated local return URL.
- [ ] Concurrent background 401 responses produce one session-expired transition.
- [ ] A 403 reaches `/account/denied` without logging the user out.

## API contracts and data behavior

- [ ] User administration is available only through secured HTTP APIs from WASM.
- [ ] OIDC topology is available through secured summary/detail APIs from WASM.
- [ ] Users, clients, scopes, and roles use server paging with default 25 and maximum 100.
- [ ] Sort fields are allow-listed and have stable unique-key tiebreakers.
- [ ] Search and filtering occur on the server.
- [ ] HTTP contracts contain no Bzs.Blazor DataGrid types.
- [ ] Current-user delete and self-admin-demotion remain blocked on the server.
- [ ] All mutable resources return opaque ETags and enforce `If-Match`.
- [ ] Conflict UI preserves unsaved input and offers an explicit reload.
- [ ] No generic bulk-write endpoint was introduced.
- [ ] Administration mutations emit secret-free structured audit events.

## Client-secret handling

- [ ] Client list and detail never return a secret.
- [ ] Normal client update cannot set or return a secret.
- [ ] Confidential-client creation returns the secret once.
- [ ] Explicit secret rotation returns the replacement once.
- [ ] Closing or refreshing the one-time display makes the secret unrecoverable.
- [ ] Secrets do not enter URLs, logs, Problem Details, audit attributes, localStorage, or sessionStorage.

## OIDC protocol behavior

- [ ] Authorization code flow succeeds for an authenticated user.
- [ ] Unauthenticated authorization redirects through `/login` and returns safely.
- [ ] Existing permanent consent bypasses the consent UI where permitted.
- [ ] Required consent redirects to the WASM `/consent` route.
- [ ] Consent preview displays the validated OIDC client and requested release scopes.
- [ ] Consent accept performs an antiforgery-protected top-level POST and completes the client redirect.
- [ ] Consent deny returns the correct OIDC access-denied response.
- [ ] `prompt=none` never renders interactive consent.
- [ ] Tampered or invalid authorization parameters are rejected by OpenIddict on final submission.
- [ ] Token, refresh-token, client-credentials, UserInfo, introspection where configured, and OIDC logout behavior remain compatible.
- [ ] Existing `/connect/*` public behavior and registered-client compatibility are preserved.

## Feature parity

- [ ] Login, registration, logout, external login, denied, not-found, and error recovery work.
- [ ] Theme and language selection persist and synchronize across tabs.
- [ ] Dashboard data and charts load with correct 401/403/error handling.
- [ ] Users can be searched, paged, created, edited, and deleted subject to protections.
- [ ] Roles can be created, renamed, deleted subject to protections, and assigned permissions.
- [ ] Protected resources, permissions, and release-scope mappings can be maintained.
- [ ] OIDC clients can be searched, paged, created, edited, deleted, and have secrets rotated.
- [ ] Release scopes can be searched, paged, created, edited, and deleted.
- [ ] OIDC topology summaries and node details load and refresh after mutations.
- [ ] Public page URLs and deep links remain compatible.

## Accessibility and responsive behavior

- [ ] Keyboard-only users can complete login, registration, consent, logout, and primary administration workflows.
- [ ] Dialogs, menus, and the temporary navigation drawer move, contain, and restore focus correctly.
- [ ] Escape and backdrop behavior respect disabled or busy states.
- [ ] Every icon-only command has an accessible name and tooltip where needed.
- [ ] Field errors, loading states, empty states, failures, and success messages have correct live-region semantics.
- [ ] Light and dark themes meet WCAG AA contrast for normal text and controls.
- [ ] Desktop and mobile screenshots show no overlap, clipped controls, or text overflow.
- [ ] Touch targets and mobile navigation remain usable at the supported minimum viewport.

## Loading, performance, and recovery

- [ ] Static startup UI is visible before WASM becomes interactive.
- [ ] Slow-network and startup-failure states provide a reload/recovery action rather than a blank page.
- [ ] The compressed initial payload and cold-start baseline are recorded after the Bzs.Blazor package is pinned.
- [ ] The final release does not regress the accepted baseline by more than ten percent without explicit approval.
- [ ] No PWA manifest or Service Worker is registered.
- [ ] WASM AOT is not enabled in the first release.
- [ ] A version mismatch or dynamic-module failure triggers at most one controlled full reload.

## Static assets, caching, and CSP

- [ ] `index.html` and the version manifest revalidate instead of using long immutable caching.
- [ ] Fingerprinted framework and application assets use long immutable caching.
- [ ] Previous fingerprinted assets remain available through the rollback window.
- [ ] Theme startup uses an external script and avoids inline script allowances.
- [ ] CSP Report-Only produces no unexplained application violations in E2E.
- [ ] Production CSP is enforced with the agreed frame, object, base, form, script, style, image, and connection boundaries.
- [ ] `nosniff`, Referrer-Policy, Permissions-Policy, and HSTS are present where applicable.
- [ ] Account and administration APIs do not enable CORS.

## Automated verification

- [ ] `dotnet restore BzsOIDC.sln`
- [ ] `dotnet build BzsOIDC.sln -c Release`
- [ ] `dotnet format BzsOIDC.sln --verify-no-changes --verbosity minimal`
- [ ] `dotnet test tests/BzsOIDC.Idp.UnitTests/BzsOIDC.Idp.UnitTests.csproj -c Release`
- [ ] `dotnet test tests/BzsOIDC.Idp.IntegrationTests/BzsOIDC.Idp.IntegrationTests.csproj -c Release`
- [ ] `dotnet test tests/BzsOIDC.Idp.E2ETests/BzsOIDC.Idp.E2ETests.csproj -c Release`
- [ ] `dotnet test BzsOIDC.sln -c Release`
- [ ] Playwright desktop and mobile screenshot checks pass.
- [ ] Playwright traces prove no Server-circuit negotiation or WebSocket.
- [ ] CSP report inspection is recorded.
- [ ] No required test is skipped to make the migration pass.

## Deployment and rollback

- [ ] Final artifact passes the complete checklist in staging with representative OIDC clients.
- [ ] Previous artifact is retained and can be redeployed without rebuilding.
- [ ] Previous artifact is verified against the new database and configuration.
- [ ] Database and API changes remain backward compatible through the rollback window.
- [ ] Previous fingerprinted assets remain available through the rollback window.
- [ ] Production has no old/new frontend switch.
- [ ] The temporary Development/E2E frontend mode is deleted.
- [ ] Rollback instructions identify the exact previous artifact and health checks.

## Final review

- [ ] Final diff contains only migration-related changes.
- [ ] Every worker/reviewer finding is resolved or explicitly dispositioned.
- [ ] Documentation and AGENTS.md reflect the final project shape and commands.
- [ ] The Bzs.Blazor gap document records which package version satisfied each blocking item.
- [ ] The user confirms functional and visual acceptance.
