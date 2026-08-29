# Hosted WebAssembly migration plan

## Status

Proposed and fully grilled. Implementation must not begin until the user confirms shared understanding and supplies the released Bzs.Blazor package version described in the prerequisite below.

## Objective

Replace every user-visible BzsOIDC page with a same-origin hosted Blazor WebAssembly application built on Bzs.Blazor. Remove Blazor Server circuits, server-rendered Razor UI, BootstrapBlazor, QuickGrid, the Neo component layer, Tailwind, GSAP, and their obsolete assets while preserving BzsOIDC domain capabilities, public page URLs, and OIDC protocol behavior.

The ASP.NET Core IDP remains responsible for Identity, OpenIddict, persistence, authorization, antiforgery, APIs, static asset delivery, security headers, and protocol redirects.

## Related decisions

- [ADR 0001](../adr/0001-use-hosted-webassembly-for-the-user-interface.md): hosted WASM topology and UI boundary.
- [ADR 0002](../adr/0002-route-oidc-consent-through-the-webassembly-ui.md): OIDC consent navigation.
- [ADR 0003](../adr/0003-secure-the-webassembly-api-with-cookie-sessions-and-antiforgery.md): browser session and API security.
- [ADR 0004](../adr/0004-use-explicit-versioned-administration-api-contracts.md): administration API contracts.
- [ADR 0005](../adr/0005-cut-over-once-and-rollback-by-artifact.md): final cutover and rollback.
- [Bzs.Blazor gap list](bzs-blazor-gap-list.md): framework prerequisites and non-blocking improvements.
- [Acceptance checklist](hosted-wasm-acceptance-checklist.md): final evidence required for completion.

## Prerequisite

The user owns changes to `D:\Coding\Bzs.Blazor`. BzsOIDC must consume a released, fixed NuGet version and must not add an absolute or local project reference to that repository.

The released package must provide:

1. A dedicated password input.
2. Text, email, and search input modes with immediate input updates and a tested binding contract outside `EditForm`.
3. Explicit `BzsDataGrid<TItem>` provider refresh.
4. A route-item activation callback for `BzsNavItem`.
5. Escape, focus entry, focus containment, and focus restoration for the responsive navigation drawer's temporary presentation.

DataGrid footer controls, current-page select-all, busy-dialog dismissal, column resizing, link-styled actions, icons, panels, and validation-summary conveniences are not migration gates.

## Target topology

### BzsOIDC.Idp

Owns:

- ASP.NET Core hosting and the middleware pipeline.
- Identity cookies and account operations.
- OpenIddict protocol endpoints.
- Authorization policies and permission enforcement.
- Antiforgery, rate limiting, Problem Details, audit events, and security headers.
- Administration APIs and persistence-backed services.
- Static hosting of the Client output.
- API and protocol routes before the SPA fallback.

Does not own:

- Razor pages or root UI components.
- Interactive Server render modes, circuits, hubs, or authentication-state serialization.
- UI services that directly bridge components to database-backed services.

### BzsOIDC.Idp.Client

Owns:

- `wwwroot/index.html`, `App.razor`, the Router, layouts, and every user-visible page.
- Bzs.Blazor registration, one theme provider, and exactly one overlay host for the application root.
- Authentication state derived from the session API.
- Typed same-origin HTTP clients and antiforgery request decoration.
- Theme, culture, tab synchronization, version recovery, and loading/failure states.
- Application-specific page composition, branding, localization, and icons.

Does not reference:

- `BzsOIDC.Idp`.
- EF Core, Identity, OpenIddict server types, `HttpContext`, or server services.

### BzsOIDC.Contracts

A new browser-safe `net10.0` project owns:

- Request and response DTOs.
- Session and antiforgery responses.
- Stable error codes and Problem Details extensions.
- `PageRequest`, `PageResult<T>`, explicit sort/filter values, and opaque resource versions.

It does not reference ASP.NET Core, EF Core, Identity, OpenIddict implementations, Bzs.Blazor, or either UI project.

## Browser session and security

- Continue using the same-origin `bzs.auth` HttpOnly, Secure, SameSite=Lax cookie.
- Ordinary login uses a browser-session cookie with a twelve-hour sliding server ticket.
- Remember-me login uses a persistent cookie with a fourteen-day maximum and sliding renewal.
- Password, security-stamp, and critical permission changes invalidate affected sessions.
- `GET /api/session` always returns a non-cacheable authenticated or anonymous summary. It exposes only display identity, roles, and permissions needed by the UI.
- The session summary controls presentation and navigation only. Every API and protocol endpoint enforces server authorization independently.
- Protected APIs return 401 or 403 Problem Details and never issue login redirects.
- `/connect/authorize` remains a top-level protocol navigation and may challenge into `/login`.

### Antiforgery

- `GET /api/security/antiforgery` sets the antiforgery cookie and returns the request token, header name, and form-field name.
- The request token exists only in WASM memory.
- Every cookie-authenticated state change validates antiforgery, including login, registration, logout, consent, and administration.
- JSON clients add the token through one same-origin handler.
- Native consent submission uses the returned form field.
- Login and logout discard the old request token and acquire a new one after the identity transition.
- A write may retry once only after an explicit `antiforgery_failed` response proves the action did not execute.

### Errors and authorization responses

- Errors use `application/problem+json` with HTTP status, a stable `code`, `traceId`, and optional field error-code arrays.
- Server responses do not contain localized UI text, exception detail, persistence detail, secrets, cookies, or framework implementation types.
- WASM localizes stable error codes.
- A foreground 401 invalidates the session and routes to `/login` with a validated local return URL.
- A 403 preserves the session and routes to `/account/denied`.
- Concurrent background 401 responses collapse into one session-expired transition.

### Account protection

- Local login, registration, and ordinary logout use JSON APIs.
- External authentication, OIDC authorization, and OIDC logout use top-level browser navigation.
- Anonymous login failure always returns `invalid_credentials`, including unknown, invalid, locked, and disallowed accounts.
- Account endpoints use ASP.NET Core rate limiting.
- Logs may contain result codes, trace IDs, source context, and irreversible user-identifier hashes, but never credentials or security tokens.

### Browser synchronization

- Explicit login, logout, theme, and culture changes publish through `BroadcastChannel` with a `storage` event fallback.
- No session polling is introduced.
- The Client refreshes `/api/session` when a tab becomes visible, protected navigation begins, or an API returns 401.

### Security headers

- Validate CSP in Report-Only mode against Blazor WASM, Bzs.Blazor dynamic modules, and ApexCharts before enforcing it in Production.
- Use external startup scripts; do not permit inline startup code.
- Enforce `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'self'`, and `form-action 'self'`.
- Add `X-Content-Type-Options: nosniff`, a strict Referrer-Policy, and a minimal Permissions-Policy.
- Account and administration APIs remain same-origin and do not enable CORS.

## API surface

### Account and protocol support

- `GET /api/session`
- `GET /api/security/antiforgery`
- JSON endpoints under `/api/account` for local login, registration, and ordinary logout.
- A read endpoint for configured external-login providers.
- `GET /api/oidc/consent-preview` for display-only, server-validated client and release-scope information.

The exact account action names should preserve existing public page URLs while separating UI routes from command endpoints.

### Administration

- `/api/users`: server-paged query and create.
- `/api/users/{id}`: read, update, and delete.
- Existing OIDC clients, scopes, roles, permission, and dashboard APIs remain, with contracts normalized into `BzsOIDC.Contracts`.
- `/api/admin/oidc-topology`: read-only summaries and node-detail queries.

Permissions:

- User-list reads require `users.read.all`.
- User writes require `users.write`.
- Existing role, client, scope, and permission read/write permissions remain authoritative.
- Current-user protections derive the actor ID from the authenticated principal. A request cannot nominate its own actor ID.

### Queries

- High-cardinality users, OIDC clients, release scopes, and roles use server paging.
- Page numbers are one-based, default to 25, and have a maximum size of 100.
- Every sort field is explicitly allowed and includes a stable unique-key tiebreaker.
- Search and filters execute server-side.
- Client code maps BzsDataGrid requests into BzsOIDC contracts; HTTP never exposes Bzs.Blazor types.
- Permission resources use summaries followed by selected-resource detail.
- OIDC topology uses summaries and node details with `observedAt`; it does not claim an atomic cross-store snapshot.

### Concurrency

- Mutable resources expose an opaque ETag.
- Update, delete, and relationship synchronization require `If-Match`.
- Missing preconditions return 428 `precondition_required`.
- Stale versions return 412 `concurrency_conflict`.
- The UI preserves unsaved input and offers explicit reload instead of silently overwriting newer data.
- Public contracts do not expose raw EF, Identity, or OpenIddict concurrency values.

### Client secrets

- Client list, detail, and normal update never return a secret.
- Confidential-client creation may return the new secret once.
- Secret rotation is a dedicated command and returns the replacement once.
- Secrets never enter URLs, browser persistence, logs, audit properties, or Problem Details.

### Bulk operations and audit

- The first migration adds no generic bulk-write endpoint.
- Dangerous operations remain resource-specific until a concrete bulk workflow defines permissions, limits, idempotency, and partial-failure semantics.
- Every administration mutation emits a structured audit event containing actor ID, action, resource type, resource ID, result code, trace ID, and timestamp, with sensitive values excluded.

## OIDC consent flow

1. The browser navigates to `/connect/authorize` normally.
2. The server authenticates the Identity cookie and evaluates consent requirements.
3. Requests that do not require interaction complete normally. `prompt=none` never enters the UI.
4. When consent is required, the server redirects to the WASM `/consent` route while preserving the original OIDC request parameters.
5. The Client calls the consent-preview endpoint and displays only server-validated client and release-scope information.
6. The user decision is submitted as a native, antiforgery-protected, top-level POST to `/connect/authorize`.
7. OpenIddict revalidates the entire request, records consent when accepted, and owns the final redirect.

The preview is never authorization evidence, and protocol completion never occurs inside `fetch`.

## UI and assets

- Use Bzs.Blazor built-in light and dark themes.
- Use compact density for administration and comfortable density for account and consent routes.
- Keep BzsOIDC branding and product language, but remove LoginHero and the Neo visual system.
- Preserve ApexCharts as a specialized visualization dependency.
- Remove BootstrapBlazor, QuickGrid, Tailwind, GSAP, npm manifests, copied GSAP assets, the Tailwind build, and obsolete custom JS/CSS.
- Prefer Bzs.Blazor components and CSS isolation. Retain only small application-owned global styles and external startup scripts.
- Theme and culture persist in localStorage and apply before the WASM host starts.
- Support the latest two stable releases of evergreen browsers.
- Do not add a Service Worker, offline mode, PWA behavior, or WASM AOT in the first release.

## Migration phases

### Phase 0: framework handoff

- User publishes the required Bzs.Blazor package.
- Record and pin the exact version.
- Verify package restore, WASM compatibility, static assets, trimming analysis, and the five required contracts.

Exit: the framework prerequisite is independently verified; BzsOIDC has no local project reference to Bzs.Blazor.

### Phase 1: contracts and API foundation

- Add `BzsOIDC.Contracts` to the solution.
- Move browser-facing transport contracts out of service implementation namespaces.
- Add common paging, Problem Details, version, and error-code contracts.
- Add session, antiforgery, account, user-administration, consent-preview, and topology endpoints.
- Normalize existing administration endpoints and permissions.
- Add ETags, `If-Match`, rate limiting, audit events, and cookie-lifetime behavior.
- Add unit and integration coverage for every contract and authorization boundary.

Exit: the old UI still works; the additive API is deployable and backward compatible.

### Phase 2: WASM platform

- Create the standalone Client root, static index, Router, layouts, loading UI, and recovery UI.
- Register Bzs.Blazor, its theme provider, and one overlay host.
- Implement typed clients, antiforgery handling, custom authentication state, guarded navigation, Problem Details mapping, and tab synchronization.
- Implement theme/culture startup and version-mismatch recovery.
- Add the temporary Development/E2E frontend mode.

Exit: an authenticated and anonymous shell can cold-start, deep-link, refresh, expire, and recover without Server circuits.

### Phase 3: account and protocol UI

- Migrate login, registration, logout confirmation, denied, consent, not-found, and error routes.
- Preserve local return-URL validation and external-login top-level navigation.
- Cover consent accept, deny, `prompt=none`, invalid requests, and WASM startup failure.

Exit: existing account and authorization-code E2E flows pass through WASM pages.

### Phase 4: administration UI

- Migrate Dashboard.
- Migrate users, roles, permissions, OIDC clients, release scopes, and OIDC topology.
- Replace direct service access with typed clients and BzsDataGrid providers.
- Preserve all domain capabilities while adopting the redesigned responsive composition.
- Verify create/update/delete, permission assignment, secret one-time display, concurrency conflicts, and session invalidation.

Exit: the feature-parity matrix and desktop/mobile workflows pass.

### Phase 5: hardening

- Complete keyboard, focus, screen-reader-state, contrast, responsive, and slow-network checks.
- Establish the compressed payload and cold-start baseline, then reject regressions above ten percent.
- Exercise CSP Report-Only, remove violations, and enable the production policy.
- Validate cache headers, version recovery, previous-asset retention, and previous-artifact compatibility.

Exit: every item in the acceptance checklist has evidence except final deletion assertions.

### Phase 6: final cutover and deletion

- Switch the host to controller/API/protocol routes followed by the WASM static fallback.
- Delete Interactive Server registration, render modes, circuit assumptions, server Razor UI, direct UI-service bridges, and authentication-state serialization.
- Delete BootstrapBlazor, QuickGrid, Neo components, Tailwind, GSAP, npm files, old CSS/JS, and obsolete tests.
- Replace Server-circuit E2E readiness with a WASM readiness and network assertion.
- Remove the temporary frontend mode.
- Run the complete Release verification and inspect the final diff.

Exit: every acceptance item passes and the repository contains one user-visible frontend.

## Intermediate merge policy

- Phase 1 may merge independently when it is backward compatible and fully tested.
- Incomplete WASM UI remains on the migration branch and is not deployed to Production.
- Phase commits remain buildable and tested; no phase advances by skipping failures.
- Keep the migration branch synchronized with main to reduce final integration risk.
- The temporary frontend mode exists only while the migration branch needs old/new verification and is deleted at cutover.

## Static caching and version recovery

- `index.html` and the application version manifest revalidate and do not use long immutable caching.
- Fingerprinted framework, Bzs.Blazor, application, CSS, JS, and image assets use long immutable caching.
- A resource-version mismatch or dynamic-module load failure shows a recovery state and performs at most one controlled full reload.
- Keep the previous release's fingerprinted assets available through the rollback window.
- Do not use a Service Worker.

## Deployment and rollback

- Exercise the final artifact in staging with representative OIDC clients and the full E2E suite.
- Verify the previous artifact against the new database and configuration before Production cutover.
- Preserve the previous deployable artifact; rollback redeploys it without rebuilding.
- Keep API and schema changes backward compatible through the rollback window.
- Do not drop or reinterpret data required by the previous artifact until the rollback window closes.
- Do not retain a Production frontend switch or dead Server UI as a rollback mechanism.

## Non-goals

- Replacing Identity or OpenIddict.
- Turning BzsOIDC into its own OIDC public client.
- Cross-origin administration APIs.
- Generic bulk mutation.
- Persistent audit-log UI.
- Atomic cross-store OIDC topology snapshots.
- Offline/PWA support.
- WASM AOT in the first release.
- Modifying Bzs.Blazor from the BzsOIDC task.

## Completion rule

The migration is complete only when the acceptance checklist passes, the final diff contains no unrelated changes, rollback evidence exists, and the user-visible application no longer opens or negotiates a Blazor Server circuit.
