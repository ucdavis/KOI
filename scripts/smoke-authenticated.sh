#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 3 ]]; then
  echo "Usage: $0 <base-url> <resource-group> <application-insights-name>" >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
credential_file="$repo_root/.env.test"
base_url="${1%/}"
resource_group="$2"
application_insights_name="$3"

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

request_count=0
for _ in {1..24}; do
  request_count="$(az monitor app-insights query \
    --resource-group "$resource_group" \
    --app "$application_insights_name" \
    --analytics-query "requests | where timestamp >= datetime($telemetry_start) | where url endswith '/api/v1/hello' and toint(resultCode) == 200 | summarize Count=count()" \
    --output json \
    --query 'tables[0].rows[0][0]' 2>/dev/null || true)"

  if [[ "$request_count" =~ ^[0-9]+$ && "$request_count" -ge 2 ]]; then
    break
  fi

  sleep 5
done

if [[ ! "$request_count" =~ ^[0-9]+$ || "$request_count" -lt 2 ]]; then
  echo "Authenticated request telemetry was not available within 120 seconds." >&2
  exit 1
fi

telemetry_json="$(az monitor app-insights query \
  --resource-group "$resource_group" \
  --app "$application_insights_name" \
  --analytics-query "union isfuzzy=true withsource=TelemetryTable requests, traces, exceptions, dependencies, customEvents, customMetrics, availabilityResults, pageViews, browserTimings | where timestamp >= datetime($telemetry_start) | project TelemetryTable, timestamp, Payload=pack_all()" \
  --output json)"

for key_variable in KOI_API_KEY_1 KOI_API_KEY_2; do
  token="${!key_variable}"
  if [[ "$telemetry_json" == *"$token"* \
    || "$telemetry_json" == *"${token:0:12}"* \
    || "$telemetry_json" == *"${token: -12}"* ]]; then
    echo "Telemetry contains recognizable material from $key_variable." >&2
    exit 1
  fi
done

echo "Telemetry contains both authenticated requests and no recognizable token material."

unset telemetry_json token
unset KOI_API_KEY_1 KOI_API_KEY_2
