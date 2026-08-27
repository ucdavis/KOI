#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 1 ]]; then
  echo "Usage: $0 <environment>" >&2
  exit 1
fi

readonly requested_environment="$1"

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/deployment-environment.sh
source "$script_dir/deployment-environment.sh"
load_deployment_environment "$requested_environment"

if ! command -v gh >/dev/null 2>&1; then
  echo "Missing required command: gh" >&2
  exit 1
fi

if [[ -f "$deployment_credential_file" ]]; then
  chmod 600 "$deployment_credential_file"
  # shellcheck disable=SC1090
  source "$deployment_credential_file"
  # Keep the tracked Azure and GitHub boundary authoritative over the local handoff.
  load_deployment_environment "$requested_environment"
fi

otel_endpoint="${OTEL_EXPORTER_OTLP_ENDPOINT:-}"
otel_headers="${OTEL_EXPORTER_OTLP_HEADERS:-}"
otel_protocol="${OTEL_EXPORTER_OTLP_PROTOCOL:-grpc}"

if [[ ! "$otel_endpoint" =~ ^https://[^[:space:]]+$ ]]; then
  echo "OTEL_EXPORTER_OTLP_ENDPOINT must be a nonempty HTTPS URL." >&2
  exit 1
fi

if [[ -z "$otel_headers" || "$otel_headers" == *$'\n'* || "$otel_headers" == *$'\r'* ]]; then
  echo "OTEL_EXPORTER_OTLP_HEADERS must be nonempty and contain no newlines." >&2
  exit 1
fi

if [[ ! "$otel_protocol" =~ ^(grpc|http/protobuf)$ ]]; then
  echo "OTEL_EXPORTER_OTLP_PROTOCOL must be grpc or http/protobuf." >&2
  exit 1
fi

gh variable set OTEL_EXPORTER_OTLP_ENDPOINT \
  --repo "$GITHUB_OWNER/$GITHUB_REPOSITORY" \
  --env "$GITHUB_ENVIRONMENT" \
  --body "$otel_endpoint"
gh variable set OTEL_EXPORTER_OTLP_PROTOCOL \
  --repo "$GITHUB_OWNER/$GITHUB_REPOSITORY" \
  --env "$GITHUB_ENVIRONMENT" \
  --body "$otel_protocol"
printf '%s' "$otel_headers" | gh secret set OTEL_EXPORTER_OTLP_HEADERS \
  --repo "$GITHUB_OWNER/$GITHUB_REPOSITORY" \
  --env "$GITHUB_ENVIRONMENT"

unset otel_headers OTEL_EXPORTER_OTLP_HEADERS KOI_API_KEY_1 KOI_API_KEY_2

echo "Configured Elastic OTLP settings for $GITHUB_OWNER/$GITHUB_REPOSITORY / $GITHUB_ENVIRONMENT."
