#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 2 ]]; then
  echo "Usage: $0 <base-url> <expected-revision>" >&2
  exit 1
fi

base_url="${1%/}"
expected_revision="$2"
health_body="$(mktemp)"
response_headers="$(mktemp)"
trap 'rm -f "$health_body" "$response_headers"' EXIT

if [[ ! "$expected_revision" =~ ^[0-9a-fA-F]{40}$ ]]; then
  echo "Expected revision must be a full 40-character Git commit SHA." >&2
  exit 1
fi

health_status=""
for attempt in $(seq 1 30); do
  health_status="$(curl \
    --silent \
    --show-error \
    --connect-timeout 10 \
    --max-time 30 \
    --output "$health_body" \
    --write-out '%{http_code}' \
    "$base_url/api/health" || true)"

  if [[ "$health_status" == "200" ]] \
    && jq -e \
      --arg revision "$expected_revision" \
      '.status == "healthy" and .service == "KOI" and .revision == $revision' \
      "$health_body" >/dev/null; then
    break
  fi

  if [[ "$attempt" -eq 30 ]]; then
    echo "Health check did not return the expected revision after 30 attempts." >&2
    cat "$health_body" >&2
    exit 1
  fi

  sleep 5
done

unauthenticated_status="$(curl \
  --silent \
  --show-error \
  --connect-timeout 10 \
  --max-time 30 \
  --dump-header "$response_headers" \
  --output /dev/null \
  --write-out '%{http_code}' \
  "$base_url/api/v1/hello")"

if [[ "$unauthenticated_status" != "401" ]]; then
  echo "Expected unauthenticated hello request to return 401; received $unauthenticated_status." >&2
  exit 1
fi

if ! awk 'BEGIN { IGNORECASE=1 } /^WWW-Authenticate:[[:space:]]*Bearer\r?$/ { found=1 } END { exit !found }' "$response_headers"; then
  echo "Unauthenticated hello response is missing WWW-Authenticate: Bearer." >&2
  exit 1
fi

invalid_status="$(curl \
  --silent \
  --show-error \
  --connect-timeout 10 \
  --max-time 30 \
  --header 'Authorization: Bearer invalid-deployment-smoke-token' \
  --output /dev/null \
  --write-out '%{http_code}' \
  "$base_url/api/v1/hello")"

if [[ "$invalid_status" != "401" ]]; then
  echo "Expected invalid-token hello request to return 401; received $invalid_status." >&2
  exit 1
fi

jq . "$health_body"
echo "Unauthenticated hello: $unauthenticated_status"
echo "Invalid-token hello: $invalid_status"
