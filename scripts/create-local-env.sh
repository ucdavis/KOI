#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_root/.env"

if [[ -e "$env_file" ]]; then
  echo "$env_file already exists; refusing to overwrite local credentials." >&2
  exit 1
fi

umask 077
primary_token="$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=\n')"
secondary_token="$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=\n')"

{
  printf 'KOI_API_KEY_1_ID=koi-local-primary\n'
  printf 'KOI_API_KEY_1=%s\n' "$primary_token"
  printf 'KOI_API_KEY_2_ID=koi-local-secondary\n'
  printf 'KOI_API_KEY_2=%s\n' "$secondary_token"
  printf 'KOI_PORT=7071\n'
} > "$env_file"

unset primary_token secondary_token
echo "Created $env_file with two independent 256-bit local credentials."
