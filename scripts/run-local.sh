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
  ApiKeys__Credentials__0__Id
  ApiKeys__Credentials__0__Sha256
  ApiKeys__Credentials__0__Enabled
  ApiKeys__Credentials__1__Id
  ApiKeys__Credentials__1__Sha256
  ApiKeys__Credentials__1__Enabled
  KOI_API_KEY_1
  KOI_API_KEY_2
)

for variable in "${required_variables[@]}"; do
  if [[ -z "${!variable:-}" ]]; then
    echo "Missing required variable: $variable" >&2
    exit 1
  fi
done

export FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
export AzureWebJobsStorage="${AzureWebJobsStorage:-}"

port="${KOI_PORT:-7071}"
unset KOI_API_KEY_1 KOI_API_KEY_2 variable

cd "$repo_root/src/Koi.Functions"
exec func start --port "$port"
