# Hosted WASM static-asset hardening baseline

This migration-stage baseline keeps the existing Server UI and its circuits in
place while the hosted `/wasm/` frontend is additive. It does not add a PWA,
service worker, offline cache, or WebAssembly AOT.

## Response policy

- The HTML shell (`/` and `/wasm/` deep links) uses `Cache-Control: no-cache,
  must-revalidate` so a deployment can publish a new asset manifest immediately.
- Generated framework/static-web-asset URLs (`/_framework/`, `/_content/`) and
  application files containing a content hash use
  `public,max-age=31536000,immutable`.
- Existing API `no-store` responses are preserved; the middleware never
  overwrites an explicit cache directive.
- Security headers include `X-Content-Type-Options`, `Referrer-Policy`,
  `Permissions-Policy`, `X-Frame-Options`, and a report-only CSP. The CSP is
  intentionally not enforced until the migration has collected compatibility
  reports for Blazor WASM, Bzs.Blazor modules, and ApexCharts.

## Verification baseline

The focused tests cover immutable-path recognition, shell revalidation, CSP
report-only delivery, and the absence of service-worker/PWA references in the
hosted client index:

```text
dotnet test tests/BzsOIDC.Idp.UnitTests/BzsOIDC.Idp.UnitTests.csproj \
  --filter "FullyQualifiedName~StaticAssetHardeningExtensionsTests"
dotnet test tests/BzsOIDC.Idp.IntegrationTests/BzsOIDC.Idp.IntegrationTests.csproj \
  --filter "FullyQualifiedName~HostedWasmFallbackIntegrationTests"
```

Browser network traces remain a cutover acceptance item: the Server UI may
still negotiate a circuit during this additive phase, while WASM-only runs
must show no `/_blazor/negotiate` or circuit WebSocket requests.
