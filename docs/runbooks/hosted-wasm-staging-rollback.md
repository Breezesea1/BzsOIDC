# Hosted WASM staging and artifact rollback runbook

This runbook is the executable evidence path for T17/#48. It validates a
candidate and the previously verified artifact without deploying either one.
Production has one frontend after cut-over; this procedure never introduces an
old/new frontend switch.

## 1. Build and capture immutable artifacts

Build the release once in CI (the release tag supplies `X.Y.Z`) and copy the
published output to an immutable store. Record the image digest or archive
checksum, commit, configuration revision, database migration level, and the
exact previous artifact identifier. Do not rebuild an old commit for rollback.

```bash
dotnet publish src/BzsOIDC.Idp/BzsOIDC.Idp.csproj -c Release -o ./staging/artifacts/1.2.3 -p:Version=1.2.3
python3 deploy/artifact-manifest.py create ./staging/artifacts/1.2.3 \
  --output ./staging/manifests/1.2.3.json --version 1.2.3 --commit "$GITHUB_SHA" \
  --api-contract v1 --database-schema 20260505 \
  --image-digest "${IMAGE_DIGEST:-}" \
  --asset-retention-until 2026-09-12T00:00:00Z \
  --required-file wwwroot/index.html --required-config IdpIssuer \
  --required-config ConnectionStrings__DefaultConnection
python3 deploy/artifact-manifest.py verify ./staging/artifacts/1.2.3 ./staging/manifests/1.2.3.json \
  --expected-version 1.2.3 --database-schema 20260505 --config-file deploy/.env
```

The manifest records SHA-256 and size for every file. Store it beside the
artifact with write-once retention. A staging promotion is blocked if any
digest, required file, version, configuration key, or schema range check fails.

## 2. Staging acceptance evidence

Deploy only the immutable candidate to the isolated staging environment using
the normal deployment system. Run the read-only health probe first; it verifies
the deployed login route and OIDC discovery document, both of which are exposed
in Production (unlike Development-only Aspire health endpoints). Then run the
complete hosted-WASM checklist and E2E suite with representative clients:

```bash
deploy/check-health.sh https://idp-staging.example.test
dotnet test tests/BzsOIDC.Idp.UnitTests/BzsOIDC.Idp.UnitTests.csproj -c Release
dotnet test tests/BzsOIDC.Idp.IntegrationTests/BzsOIDC.Idp.IntegrationTests.csproj -c Release
dotnet test tests/BzsOIDC.Idp.E2ETests/BzsOIDC.Idp.E2ETests.csproj -c Release
```

The staging record must include authorization-code, consent accept/deny,
`prompt=none`, token and refresh-token, client-credentials, UserInfo,
configured introspection, logout, direct navigation/refresh, and WASM startup
network assertions. Attach the test run IDs, artifact manifest, image digest,
database migration level, and health output to the release record.

## 3. Previous-artifact compatibility gate

Before production cut-over, verify the previous artifact against the *new*
database schema and the exact staging configuration. Both artifacts must expose
the same API contract version; the previous manifest's rollback range must
include the new schema; and fingerprinted assets must remain retained until the
declared UTC deadline.

```bash
python3 deploy/artifact-manifest.py verify ./staging/artifacts/1.2.2 ./staging/manifests/1.2.2.json \
  --database-schema 20260505 --config-file deploy/.env
python3 deploy/artifact-manifest.py compatibility \
  ./staging/manifests/1.2.2.json ./staging/manifests/1.2.3.json \
  --database-schema 20260505 --config-file deploy/.env
```

Do not advance if this gate fails. Schema migrations must remain additive and
backward compatible for the whole rollback window; do not drop or reinterpret
data needed by the previous artifact.

## 4. Rollback (redeploy, never rebuild)

Declare an incident, stop promotion, and identify the previous manifest and
immutable image/archive digest from the release record. Redeploy that exact
artifact through the ordinary deployment mechanism, then run the health probe
and smoke/E2E checks. Do not check out source, run `dotnet publish`, or enable a
runtime frontend toggle.

```bash
export IMAGE_TAG=1.2.2                 # tag resolves to the retained digest
export EXPECTED_ARTIFACT_MANIFEST=./staging/manifests/1.2.2.json
deploy/deploy.sh                         # existing deployment path; no rebuild
deploy/check-health.sh https://idp.example.test
dotnet test tests/BzsOIDC.Idp.E2ETests/BzsOIDC.Idp.E2ETests.csproj -c Release \
  --filter "FullyQualifiedName~AuthExperienceE2ETests"
```

Record the selected manifest, image digest, deployment timestamp, migration
level, health responses, and test result. Keep previous fingerprinted assets and
the manifest immutable until the rollback window closes; only then may they be
retired according to retention policy.

## Evidence checklist

- [ ] Candidate manifest and immutable digest are attached to the staging run.
- [ ] Full hosted-WASM flow checklist and E2E results are green.
- [ ] Previous manifest verifies against the new database and configuration.
- [ ] API contract and schema compatibility output is retained.
- [ ] Health checks pass after promotion and (if needed) rollback.
- [ ] Rollback used the retained artifact; no source rebuild occurred.
- [ ] No Production old/new frontend switch exists.
