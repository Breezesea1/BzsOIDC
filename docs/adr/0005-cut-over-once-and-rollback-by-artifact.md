# Cut over once and rollback by artifact

The WebAssembly migration will be developed and verified in stages, using a temporary Development/E2E-only frontend mode where necessary, but Production will never expose a permanent old/new UI switch. The final cutover removes the temporary mode, Server UI, Server-circuit infrastructure, and obsolete frontend dependencies in one change after parity checks pass.

Rollback redeploys the previously verified application artifact rather than retaining dead Server UI or rebuilding an old commit. API and database changes remain backward compatible through the rollback window, and fingerprinted assets from the previous release remain available long enough for already-open browser documents to recover.
