#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"

if [[ ! -f "$env_file" ]]; then
  echo "Missing $env_file. Run ./scripts/create-local-env.sh to create two local keys." >&2
  exit 1
fi

chmod 600 "$env_file"

# shellcheck disable=SC1090
source "$env_file"

base_url="${KOI_BASE_URL:-http://localhost:${KOI_PORT:-7071}}"
temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT

request() {
  local output_file="$1"
  shift
  curl --silent --show-error --output "$output_file" --write-out '%{http_code}' "$@"
}

assert_response() {
  local description="$1"
  local expected_status="$2"
  local expected_body="$3"
  local actual_status="$4"
  local output_file="$5"
  local actual_body
  actual_body="$(tr -d '\r\n' < "$output_file")"

  if [[ "$actual_status" != "$expected_status" || "$actual_body" != "$expected_body" ]]; then
    echo "$description failed: expected $expected_status $expected_body, got $actual_status $actual_body" >&2
    exit 1
  fi

  echo "$description passed"
}

health_file="$temporary_directory/health.json"
health_status="$(request "$health_file" "$base_url/api/health")"
assert_response \
  "Anonymous health" \
  200 \
  '{"status":"healthy","service":"KOI","version":"0.1.1","revision":"local"}' \
  "$health_status" \
  "$health_file"

unauthorized_file="$temporary_directory/unauthorized.json"
unauthorized_headers="$temporary_directory/unauthorized.headers"
unauthorized_status="$(curl \
  --silent \
  --show-error \
  --dump-header "$unauthorized_headers" \
  --output "$unauthorized_file" \
  --write-out '%{http_code}' \
  "$base_url/api/v1/hello")"
assert_response \
  "Missing bearer token" \
  401 \
  '{"error":"unauthorized"}' \
  "$unauthorized_status" \
  "$unauthorized_file"
if ! grep -qi '^WWW-Authenticate: Bearer' "$unauthorized_headers"; then
  echo "Missing bearer token failed: WWW-Authenticate header is absent" >&2
  exit 1
fi

invalid_file="$temporary_directory/invalid.json"
invalid_status="$(request \
  "$invalid_file" \
  --header 'Authorization: Bearer incorrect' \
  "$base_url/api/v1/hello")"
assert_response \
  "Invalid bearer token" \
  401 \
  '{"error":"unauthorized"}' \
  "$invalid_status" \
  "$invalid_file"

for key_variable in KOI_API_KEY_1 KOI_API_KEY_2; do
  hello_file="$temporary_directory/$key_variable.json"
  hello_status="$(request \
    "$hello_file" \
    --header "Authorization: Bearer ${!key_variable}" \
    "$base_url/api/v1/hello")"
  assert_response \
    "$key_variable" \
    200 \
    '{"message":"Hello from KOI"}' \
    "$hello_status" \
    "$hello_file"
done

echo "Local KOI smoke test passed"
