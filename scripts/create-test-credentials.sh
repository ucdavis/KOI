#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
destination="$repo_root/.env.test"
issued_at="$(date -u +%Y%m%dT%H%M%SZ)"

if [[ -e "$destination" ]]; then
  echo "Refusing to overwrite existing $destination" >&2
  exit 1
fi

umask 077
key_1="$(openssl rand -hex 32)"
key_2="$(openssl rand -hex 32)"

{
  printf 'KOI_API_KEY_1_ID=kuali-test-%s-a\n' "$issued_at"
  printf 'KOI_API_KEY_1=%s\n' "$key_1"
  printf 'KOI_API_KEY_2_ID=kuali-test-%s-b\n' "$issued_at"
  printf 'KOI_API_KEY_2=%s\n' "$key_2"
} >"$destination"

unset key_1 key_2
chmod 600 "$destination"
echo "Created $destination with mode 600. Move both plaintext tokens to the approved password manager."
