#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 1 ]]; then
  echo "Usage: $0 <base-url>" >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
credential_file="$repo_root/.env.test"
base_url="${1%/}"

if [[ ! -f "$credential_file" ]]; then
  echo "Missing $credential_file." >&2
  exit 1
fi

chmod 600 "$credential_file"
# shellcheck disable=SC1090
source "$credential_file"

telemetry_start="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

for key_variable in KOI_API_KEY_1 KOI_API_KEY_2; do
  token="${!key_variable:-}"
  if [[ -z "$token" || "${#token}" -lt 24 ]]; then
    echo "Missing $key_variable in $credential_file." >&2
    exit 1
  fi

  status="$(curl \
    --silent \
    --show-error \
    --connect-timeout 10 \
    --max-time 30 \
    --header "Authorization: Bearer $token" \
    --output /dev/null \
    --write-out '%{http_code}' \
    "$base_url/api/v1/hello")"

  if [[ "$status" != "200" ]]; then
    echo "$key_variable failed authenticated smoke test with status $status." >&2
    exit 1
  fi

  echo "$key_variable: $status"
done

echo "Authenticated requests sent at or after $telemetry_start."
echo "Verify ingestion and token redaction in Elastic before considering telemetry proven."

unset token
unset KOI_API_KEY_1 KOI_API_KEY_2
