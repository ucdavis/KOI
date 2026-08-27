#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 1 ]]; then
  echo "Usage: $0 <environment>" >&2
  exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/deployment-environment.sh
source "$script_dir/deployment-environment.sh"
load_deployment_environment "$1"

destination="$deployment_credential_file"
issued_at="$(date -u +%Y%m%dT%H%M%SZ)"

if [[ -e "$destination" ]]; then
  echo "Refusing to overwrite existing $destination" >&2
  exit 1
fi

umask 077
key_1="$(openssl rand -hex 32)"
key_2="$(openssl rand -hex 32)"

{
  printf 'KOI_API_KEY_1_ID=kuali-%s-%s-a\n' "$AZURE_ENVIRONMENT_NAME" "$issued_at"
  printf 'KOI_API_KEY_1=%s\n' "$key_1"
  printf 'KOI_API_KEY_2_ID=kuali-%s-%s-b\n' "$AZURE_ENVIRONMENT_NAME" "$issued_at"
  printf 'KOI_API_KEY_2=%s\n' "$key_2"
} >"$destination"

unset key_1 key_2
chmod 600 "$destination"
echo "Created $destination with mode 600. Move both plaintext tokens to the approved password manager."
echo "Add the six Financial__* settings before configuring the GitHub $GITHUB_ENVIRONMENT environment."
