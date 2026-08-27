#!/usr/bin/env bash

# Values assigned by this sourced helper are consumed by its caller.
# shellcheck disable=SC2034

# Shared loader for tracked deployment configuration and its local secret handoff.
load_deployment_environment() {
  if [[ "$#" -ne 1 ]]; then
    echo "Expected one deployment environment name." >&2
    return 1
  fi

  local environment_name="$1"
  if [[ ! "$environment_name" =~ ^[a-z][a-z0-9-]{0,31}$ ]]; then
    echo "Deployment environment names must use lowercase letters, digits, and hyphens." >&2
    return 1
  fi

  deployment_environment="$environment_name"
  deployment_repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
  deployment_config_file="$deployment_repo_root/infra/environments/$deployment_environment.env"
  deployment_credential_file="$deployment_repo_root/.env.$deployment_environment"

  if [[ ! -f "$deployment_config_file" ]]; then
    echo "Missing deployment configuration: $deployment_config_file" >&2
    return 1
  fi

  # shellcheck disable=SC1090
  source "$deployment_config_file"

  local required_variables=(
    AZURE_SUBSCRIPTION_ID
    AZURE_TENANT_ID
    AZURE_LOCATION
    AZURE_RESOURCE_GROUP
    AZURE_ENVIRONMENT_NAME
    GITHUB_OWNER
    GITHUB_REPOSITORY
    GITHUB_ENVIRONMENT
    GITHUB_REQUIRE_REVIEW
    GITHUB_PREVENT_SELF_REVIEW
  )

  local variable_name
  for variable_name in "${required_variables[@]}"; do
    if [[ -z "${!variable_name:-}" ]]; then
      echo "Missing required variable in $deployment_config_file: $variable_name" >&2
      return 1
    fi
  done

  if [[ ! "$GITHUB_REQUIRE_REVIEW" =~ ^(true|false)$ ]]; then
    echo "GITHUB_REQUIRE_REVIEW must be true or false." >&2
    return 1
  fi

  if [[ ! "$GITHUB_PREVENT_SELF_REVIEW" =~ ^(true|false)$ ]]; then
    echo "GITHUB_PREVENT_SELF_REVIEW must be true or false." >&2
    return 1
  fi

  GITHUB_REQUIRED_REVIEWER_TEAMS="${GITHUB_REQUIRED_REVIEWER_TEAMS:-}"
  if [[ "$GITHUB_REQUIRE_REVIEW" == "true" && -z "$GITHUB_REQUIRED_REVIEWER_TEAMS" ]]; then
    echo "A protected environment must name at least one required reviewer team." >&2
    return 1
  fi

  if [[ "$GITHUB_REQUIRE_REVIEW" == "false" && -n "$GITHUB_REQUIRED_REVIEWER_TEAMS" ]]; then
    echo "Reviewer teams require GITHUB_REQUIRE_REVIEW=true." >&2
    return 1
  fi

  if [[ "$GITHUB_REQUIRE_REVIEW" == "false" && "$GITHUB_PREVENT_SELF_REVIEW" == "true" ]]; then
    echo "Self-review prevention requires environment review." >&2
    return 1
  fi
}
