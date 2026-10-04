#!/usr/bin/env bash
set -euo pipefail

# Read-only staging/rollback probe. It never changes containers or deployment state.
BASE_URL="${1:?usage: check-health.sh <base-url> [timeout-seconds]}"
TIMEOUT="${2:-10}"
BASE_URL="${BASE_URL%/}"

# The backend-only Idp exposes no GET login page (only POST /api/account/login).
# The OIDC discovery document is the only reliable GET liveness probe available
# in every environment; /health and /alive are mapped only in Development.
for endpoint in /.well-known/openid-configuration; do
    url="${BASE_URL}${endpoint}"
    response_file="$(mktemp)"
    status="$(curl --silent --show-error --output "${response_file}" --write-out '%{http_code}' --max-time "${TIMEOUT}" "${url}" || true)"
    if [[ "${status}" != 2* ]]; then
        echo "health check failed: ${url} returned ${status:-no-response}" >&2
        [[ -s "${response_file}" ]] && cat "${response_file}" >&2
        rm -f "${response_file}"
        exit 1
    fi
    if [[ ! -s "${response_file}" ]]; then
        echo "health check failed: ${url} returned an empty response" >&2
        rm -f "${response_file}"
        exit 1
    fi
    rm -f "${response_file}"
    echo "health check passed: ${url} (${status})"
done
