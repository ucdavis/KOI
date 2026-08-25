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
primary_hash="$(printf '%s' "$primary_token" | openssl dgst -sha256 -r | awk '{print $1}')"
secondary_hash="$(printf '%s' "$secondary_token" | openssl dgst -sha256 -r | awk '{print $1}')"

{
  printf 'ApiKeys__Credentials__0__Id=koi-local-primary\n'
  printf 'ApiKeys__Credentials__0__Sha256=%s\n' "$primary_hash"
  printf 'ApiKeys__Credentials__0__Enabled=true\n'
  printf 'ApiKeys__Credentials__1__Id=koi-local-secondary\n'
  printf 'ApiKeys__Credentials__1__Sha256=%s\n' "$secondary_hash"
  printf 'ApiKeys__Credentials__1__Enabled=true\n'
  printf '\n'
  printf 'KOI_API_KEY_1=%s\n' "$primary_token"
  printf 'KOI_API_KEY_2=%s\n' "$secondary_token"
  printf 'KOI_PORT=7071\n'
} > "$env_file"

unset primary_token secondary_token primary_hash secondary_hash
echo "Created $env_file with two independent 256-bit local tokens and matching hashes."
