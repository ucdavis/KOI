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

required_variables=(
  KOI_API_KEY_1_ID
  KOI_API_KEY_1
  KOI_API_KEY_2_ID
  KOI_API_KEY_2
)

for variable in "${required_variables[@]}"; do
  if [[ -z "${!variable:-}" ]]; then
    echo "Missing required variable: $variable" >&2
    exit 1
  fi
done

sha256() {
  printf '%s' "$1" | openssl dgst -sha256 -r | awk '{print $1}'
}

export FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
export AzureWebJobsStorage="${AzureWebJobsStorage:-}"
export ApiKeys__Credentials__0__Id="$KOI_API_KEY_1_ID"
export ApiKeys__Credentials__0__Sha256="$(sha256 "$KOI_API_KEY_1")"
export ApiKeys__Credentials__0__Enabled=true
export ApiKeys__Credentials__1__Id="$KOI_API_KEY_2_ID"
export ApiKeys__Credentials__1__Sha256="$(sha256 "$KOI_API_KEY_2")"
export ApiKeys__Credentials__1__Enabled=true

test_env_file="$repo_root/.env.test"
if [[ -f "$test_env_file" ]]; then
  chmod 600 "$test_env_file"
  while IFS= read -r -d '' variable && IFS= read -r -d '' value; do
    printf -v "$variable" '%s' "$value"
    export "$variable"
  done < <(
    unset OTEL_EXPORTER_OTLP_ENDPOINT OTEL_EXPORTER_OTLP_HEADERS OTEL_EXPORTER_OTLP_PROTOCOL
    # shellcheck disable=SC1090
    source "$test_env_file"
    for variable in OTEL_EXPORTER_OTLP_ENDPOINT OTEL_EXPORTER_OTLP_HEADERS OTEL_EXPORTER_OTLP_PROTOCOL; do
      if [[ -n "${!variable+x}" ]]; then
        printf '%s\0%s\0' "$variable" "${!variable}"
      fi
    done
  )
fi

if [[ -n "${OTEL_EXPORTER_OTLP_ENDPOINT:-}" ]]; then
  service_version="$(dotnet msbuild \
    "$repo_root/src/Koi.Functions/Koi.Functions.csproj" \
    -getProperty:Version \
    -nologo)"
  export OTEL_EXPORTER_OTLP_ENDPOINT
  export OTEL_EXPORTER_OTLP_HEADERS="${OTEL_EXPORTER_OTLP_HEADERS:-}"
  export OTEL_EXPORTER_OTLP_PROTOCOL="${OTEL_EXPORTER_OTLP_PROTOCOL:-grpc}"
  custom_resource_attributes_joined=
  resource_attributes=()
  IFS=',' read -ra resource_attributes <<<"${OTEL_RESOURCE_ATTRIBUTES:-}"
  for resource_attribute in "${resource_attributes[@]-}"; do
    case "${resource_attribute%%=*}" in
      service.name|service.version|deployment.environment|service.namespace) ;;
      *)
        if [[ -n "$resource_attribute" ]]; then
          custom_resource_attributes_joined+=",$resource_attribute"
        fi
        ;;
    esac
  done
  export OTEL_RESOURCE_ATTRIBUTES="service.name=koi,service.version=$service_version,deployment.environment=local${custom_resource_attributes_joined},service.namespace=ucdavis"
  export OTEL_SERVICE_NAME=koi
fi

port="${KOI_PORT:-7071}"
unset KOI_API_KEY_1 KOI_API_KEY_2 service_version resource_attributes resource_attribute custom_resource_attributes_joined variable value

cd "$repo_root/src/Koi.Functions"
exec func start --port "$port"
