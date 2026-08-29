# Use explicit, versioned administration API contracts

The WebAssembly application maps Bzs.Blazor provider requests to browser-safe BzsOIDC contracts rather than exposing UI-framework types through HTTP. High-cardinality administration resources use explicit, server-side paging, searching, and stable sorting; permission and OIDC topology screens use summaries plus node details instead of downloading or claiming an atomic full-system snapshot.

Every mutable administration aggregate exposes an opaque ETag and requires `If-Match` for update, delete, and relationship synchronization, returning 428 when the precondition is missing and 412 when it is stale. OIDC client secrets are returned only once at creation or explicit rotation and never participate in normal reads or updates. The first migration does not add generic bulk writes, and all administration mutations emit secret-free structured audit events.
