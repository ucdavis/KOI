#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
config_file="$repo_root/infra/environments/test.env"
credential_file="$repo_root/.env.test"

if ! command -v gh >/dev/null 2>&1; then
  echo "Missing required command: gh" >&2
  exit 1
fi

if [[ ! -f "$credential_file" ]]; then
  echo "Missing $credential_file. Add the Financial settings to a gitignored .env.test file." >&2
  exit 1
fi

# shellcheck disable=SC1090
source "$config_file"
chmod 600 "$credential_file"
# shellcheck disable=SC1090
source "$credential_file"

financial_api_url="${Financial__ApiUrl:-}"
financial_consumer_key="${Financial__ConsumerKey:-}"
financial_consumer_secret="${Financial__ConsumerSecret:-}"
financial_token_endpoint="${Financial__TokenEndpoint:-}"
financial_scope_app="${Financial__ScopeApp:-}"
financial_scope_env="${Financial__ScopeEnv:-}"

if [[ ! "$financial_api_url" =~ ^https://[^[:space:]]+$ ]]; then
  echo "Financial__ApiUrl must be a nonempty HTTPS URL." >&2
  exit 1
fi

if [[ ! "$financial_token_endpoint" =~ ^https://[^[:space:]]+$ ]]; then
  echo "Financial__TokenEndpoint must be a nonempty HTTPS URL." >&2
  exit 1
fi

for financial_value in \
  "$financial_consumer_key" \
  "$financial_consumer_secret" \
  "$financial_scope_app" \
  "$financial_scope_env"; do
  if [[ -z "$financial_value" \
    || "$financial_value" == *$'\n'* \
    || "$financial_value" == *$'\r'* ]]; then
    echo "Financial credentials and scopes must be nonempty and contain no newlines." >&2
    exit 1
  fi
done

gh variable set FINANCIAL_API_URL \
  --repo "$GITHUB_OWNER/$GITHUB_REPOSITORY" \
  --env "$GITHUB_ENVIRONMENT" \
  --body "$financial_api_url"
gh variable set FINANCIAL_TOKEN_ENDPOINT \
  --repo "$GITHUB_OWNER/$GITHUB_REPOSITORY" \
  --env "$GITHUB_ENVIRONMENT" \
  --body "$financial_token_endpoint"
gh variable set FINANCIAL_SCOPE_APP \
  --repo "$GITHUB_OWNER/$GITHUB_REPOSITORY" \
  --env "$GITHUB_ENVIRONMENT" \
  --body "$financial_scope_app"
gh variable set FINANCIAL_SCOPE_ENV \
  --repo "$GITHUB_OWNER/$GITHUB_REPOSITORY" \
  --env "$GITHUB_ENVIRONMENT" \
  --body "$financial_scope_env"
printf '%s' "$financial_consumer_key" | gh secret set FINANCIAL_CONSUMER_KEY \
  --repo "$GITHUB_OWNER/$GITHUB_REPOSITORY" \
  --env "$GITHUB_ENVIRONMENT"
printf '%s' "$financial_consumer_secret" | gh secret set FINANCIAL_CONSUMER_SECRET \
  --repo "$GITHUB_OWNER/$GITHUB_REPOSITORY" \
  --env "$GITHUB_ENVIRONMENT"

unset \
  Financial__ConsumerKey \
  Financial__ConsumerSecret \
  financial_consumer_key \
  financial_consumer_secret \
  financial_value

echo "Configured Financial API settings for $GITHUB_OWNER/$GITHUB_REPOSITORY / $GITHUB_ENVIRONMENT."
