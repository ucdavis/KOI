#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
config_file="$repo_root/infra/environments/test.env"

for command_name in gh; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Missing required command: $command_name" >&2
    exit 1
  fi
done

# shellcheck disable=SC1090
source "$config_file"

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

unset otel_headers OTEL_EXPORTER_OTLP_HEADERS

echo "Configured Elastic OTLP settings for $GITHUB_OWNER/$GITHUB_REPOSITORY / $GITHUB_ENVIRONMENT."
