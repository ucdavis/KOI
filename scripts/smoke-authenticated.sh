#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -lt 2 || "$#" -gt 3 ]]; then
  echo "Usage: $0 <base-url> <financial-chart-string> [environment]" >&2
  exit 1
fi

for required_command in curl jq; do
  if ! command -v "$required_command" >/dev/null 2>&1; then
    echo "Missing required command: $required_command" >&2
    exit 1
  fi
done

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
base_url="${1%/}"
financial_chart_string="$2"
deployment_environment="${3:-test}"
credential_file="$repo_root/.env.$deployment_environment"

if [[ ! "$deployment_environment" =~ ^[a-z][a-z0-9-]{0,31}$ ]]; then
  echo "Deployment environment names must use lowercase letters, digits, and hyphens." >&2
  exit 1
fi

if [[ -z "$financial_chart_string" \
  || "$financial_chart_string" == *$'\n'* \
  || "$financial_chart_string" == *$'\r'* ]]; then
  echo "Financial chart string must be nonempty and contain no newlines." >&2
  exit 1
fi

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

  status="$(printf 'Authorization: Bearer %s\n' "$token" | curl \
    --silent \
    --show-error \
    --connect-timeout 10 \
    --max-time 30 \
    --header @- \
    --output /dev/null \
    --write-out '%{http_code}' \
    "$base_url/api/v1/hello")"

  if [[ "$status" != "200" ]]; then
    echo "$key_variable failed authenticated smoke test with status $status." >&2
    exit 1
  fi

  echo "$key_variable: $status"
done

financial_body="$(mktemp)"
trap 'rm -f "$financial_body"' EXIT
encoded_chart_string="$(jq \
  --null-input \
  --raw-output \
  --arg value "$financial_chart_string" \
  '$value | @uri')"

financial_status="$(printf 'Authorization: Bearer %s\n' "$KOI_API_KEY_1" | curl \
  --silent \
  --show-error \
  --connect-timeout 10 \
  --max-time 60 \
  --header @- \
  --output "$financial_body" \
  --write-out '%{http_code}' \
  "$base_url/api/v1/financial/details/$encoded_chart_string")"

if [[ "$financial_status" != "200" ]]; then
  echo "Financial integration smoke test failed with status $financial_status." >&2
  exit 1
fi

if ! jq --exit-status \
  --arg chart_string "$financial_chart_string" \
  '.chartString == $chart_string and .isValid == true and (.errors | length == 0)' \
  "$financial_body" >/dev/null; then
  echo "Financial integration returned 200 without a valid Aggie Enterprise response." >&2
  jq '{chartString, chartStringType, isValid, error, warning}' "$financial_body" >&2
  exit 1
fi

echo "Financial integration: $financial_status with a valid Aggie Enterprise response"

echo "Authenticated requests sent at or after $telemetry_start."
echo "Verify ingestion and token redaction in Elastic before considering telemetry proven."

unset token encoded_chart_string financial_chart_string
unset KOI_API_KEY_1 KOI_API_KEY_2
