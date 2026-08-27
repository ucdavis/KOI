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

required_commands=(az gh jq openssl)
for command_name in "${required_commands[@]}"; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Missing required command: $command_name" >&2
    exit 1
  fi
done

if [[ ! -f "$deployment_credential_file" ]]; then
  echo "Missing $deployment_credential_file. Run ./scripts/create-environment-credentials.sh $deployment_environment first." >&2
  exit 1
fi

chmod 600 "$deployment_credential_file"
# shellcheck disable=SC1090
source "$deployment_credential_file"
# Reload the tracked boundary in case the local handoff contains a conflicting key.
load_deployment_environment "$requested_environment"

required_credential_variables=(
  KOI_API_KEY_1_ID
  KOI_API_KEY_1
  KOI_API_KEY_2_ID
  KOI_API_KEY_2
)

for variable_name in "${required_credential_variables[@]}"; do
  if [[ -z "${!variable_name:-}" ]]; then
    echo "Missing required variable in $deployment_credential_file: $variable_name" >&2
    exit 1
  fi
done

if [[ "$KOI_API_KEY_1" == "$KOI_API_KEY_2" ]]; then
  echo "The two API keys must be independently generated." >&2
  exit 1
fi

sha256() {
  printf '%s' "$1" | openssl dgst -sha256 -r | awk '{print $1}'
}

api_key_1_sha256="$(sha256 "$KOI_API_KEY_1")"
api_key_2_sha256="$(sha256 "$KOI_API_KEY_2")"

repository_json="$(gh api "repos/$GITHUB_OWNER/$GITHUB_REPOSITORY")"
repository_id="$(jq -r '.id' <<<"$repository_json")"
oidc_configuration="$(gh api "repos/$GITHUB_OWNER/$GITHUB_REPOSITORY/actions/oidc/customization/sub")"
subject_prefix="$(jq -r '.sub_claim_prefix' <<<"$oidc_configuration")"
federated_subject="${subject_prefix}:environment:${GITHUB_ENVIRONMENT}"
repository_name_lower="$(printf '%s' "$GITHUB_REPOSITORY" | tr '[:upper:]' '[:lower:]')"
deployment_identity_name="id-${repository_name_lower}-${AZURE_ENVIRONMENT_NAME}-deploy"
federated_credential_name="github-${repository_id}-${GITHUB_ENVIRONMENT}"

required_reviewers='[]'
reviewer_team_count=0
if [[ -n "$GITHUB_REQUIRED_REVIEWER_TEAMS" ]]; then
  IFS=',' read -r -a reviewer_teams <<<"$GITHUB_REQUIRED_REVIEWER_TEAMS"
  for reviewer_team in "${reviewer_teams[@]}"; do
    if [[ ! "$reviewer_team" =~ ^[A-Za-z0-9_.-]+$ ]]; then
      echo "Invalid GitHub reviewer team slug: $reviewer_team" >&2
      exit 1
    fi

    reviewer_team_id="$(gh api "orgs/$GITHUB_OWNER/teams/$reviewer_team" --jq '.id')"
    if [[ -z "$reviewer_team_id" || "$reviewer_team_id" == "null" ]]; then
      echo "Could not resolve GitHub reviewer team: $reviewer_team" >&2
      exit 1
    fi

    required_reviewers="$(jq \
      --argjson reviewer_id "$reviewer_team_id" \
      '. + [{type: "Team", id: $reviewer_id}]' \
      <<<"$required_reviewers")"
    reviewer_team_count=$((reviewer_team_count + 1))
  done
fi

if [[ "$GITHUB_REQUIRE_REVIEW" == "true" && "$reviewer_team_count" -eq 0 ]]; then
  echo "The $GITHUB_ENVIRONMENT environment requires at least one reviewer team." >&2
  exit 1
fi

if [[ "$reviewer_team_count" -gt 6 ]]; then
  echo "GitHub environments support at most six required reviewer users or teams." >&2
  exit 1
fi

az account set --subscription "$AZURE_SUBSCRIPTION_ID"
selected_subscription="$(az account show --query id --output tsv)"
selected_tenant="$(az account show --query tenantId --output tsv)"
if [[ "$selected_subscription" != "$AZURE_SUBSCRIPTION_ID" || "$selected_tenant" != "$AZURE_TENANT_ID" ]]; then
  echo "Azure CLI context does not match the configured subscription and tenant." >&2
  exit 1
fi

echo "Creating or synchronizing $AZURE_RESOURCE_GROUP with Bicep"
bootstrap_outputs="$(az deployment sub create \
  --name "koi-${AZURE_ENVIRONMENT_NAME}-bootstrap" \
  --location "$AZURE_LOCATION" \
  --template-file "$deployment_repo_root/infra/bootstrap.bicep" \
  --parameters \
    deploymentIdentityName="$deployment_identity_name" \
    environmentName="$AZURE_ENVIRONMENT_NAME" \
    federatedCredentialName="$federated_credential_name" \
    githubOidcSubject="$federated_subject" \
    location="$AZURE_LOCATION" \
    resourceGroupName="$AZURE_RESOURCE_GROUP" \
  --query properties.outputs \
  --output json)"

application_id="$(jq -r '.deploymentIdentityClientId.value' <<<"$bootstrap_outputs")"
service_principal_object_id="$(jq -r '.deploymentIdentityPrincipalId.value' <<<"$bootstrap_outputs")"
resource_group_scope="/subscriptions/$AZURE_SUBSCRIPTION_ID/resourceGroups/$AZURE_RESOURCE_GROUP"

if [[ -z "$application_id" || "$application_id" == "null" \
  || -z "$service_principal_object_id" || "$service_principal_object_id" == "null" ]]; then
  echo "Bicep did not return the deployment managed identity IDs." >&2
  exit 1
fi

jq -n \
  --argjson prevent_self_review "$GITHUB_PREVENT_SELF_REVIEW" \
  --argjson reviewers "$required_reviewers" \
  '{
    wait_timer: 0,
    prevent_self_review: $prevent_self_review,
    reviewers: $reviewers,
    deployment_branch_policy: {
      protected_branches: false,
      custom_branch_policies: true
    }
  }' \
  | gh api \
      --method PUT \
      "repos/$GITHUB_OWNER/$GITHUB_REPOSITORY/environments/$GITHUB_ENVIRONMENT" \
      --input - \
      --silent

main_branch_policy_id=""
while IFS=$'\t' read -r policy_id policy_name policy_type; do
  if [[ "$policy_name" == "main" && "$policy_type" == "branch" \
    && -z "$main_branch_policy_id" ]]; then
    main_branch_policy_id="$policy_id"
    continue
  fi

  gh api \
    --method DELETE \
    "repos/$GITHUB_OWNER/$GITHUB_REPOSITORY/environments/$GITHUB_ENVIRONMENT/deployment-branch-policies/$policy_id" \
    --silent
done < <(gh api \
  --paginate \
  "repos/$GITHUB_OWNER/$GITHUB_REPOSITORY/environments/$GITHUB_ENVIRONMENT/deployment-branch-policies?per_page=100" \
  --jq '.branch_policies[] | [.id, .name, .type] | @tsv')

if [[ -z "$main_branch_policy_id" ]]; then
  gh api \
    --method POST \
    "repos/$GITHUB_OWNER/$GITHUB_REPOSITORY/environments/$GITHUB_ENVIRONMENT/deployment-branch-policies" \
    -f name=main \
    -f type=branch \
    --silent
fi

set_environment_variable() {
  gh variable set "$1" \
    --repo "$GITHUB_OWNER/$GITHUB_REPOSITORY" \
    --env "$GITHUB_ENVIRONMENT" \
    --body "$2"
}

set_environment_variable AZURE_CLIENT_ID "$application_id"
set_environment_variable AZURE_TENANT_ID "$AZURE_TENANT_ID"
set_environment_variable AZURE_SUBSCRIPTION_ID "$AZURE_SUBSCRIPTION_ID"
set_environment_variable AZURE_LOCATION "$AZURE_LOCATION"
set_environment_variable AZURE_RESOURCE_GROUP "$AZURE_RESOURCE_GROUP"
set_environment_variable AZURE_ENVIRONMENT_NAME "$AZURE_ENVIRONMENT_NAME"
set_environment_variable KOI_API_KEY_1_ID "$KOI_API_KEY_1_ID"
set_environment_variable KOI_API_KEY_2_ID "$KOI_API_KEY_2_ID"

printf '%s' "$api_key_1_sha256" | gh secret set KOI_API_KEY_1_SHA256 \
  --repo "$GITHUB_OWNER/$GITHUB_REPOSITORY" \
  --env "$GITHUB_ENVIRONMENT"
printf '%s' "$api_key_2_sha256" | gh secret set KOI_API_KEY_2_SHA256 \
  --repo "$GITHUB_OWNER/$GITHUB_REPOSITORY" \
  --env "$GITHUB_ENVIRONMENT"

unset KOI_API_KEY_1 KOI_API_KEY_2 api_key_1_sha256 api_key_2_sha256

echo "Bootstrap complete"
echo "Deployment managed identity: $deployment_identity_name ($application_id)"
echo "Federated subject: $federated_subject"
echo "Azure scope: $resource_group_scope"
echo "GitHub environment: $GITHUB_OWNER/$GITHUB_REPOSITORY / $GITHUB_ENVIRONMENT"
if [[ "$GITHUB_REQUIRE_REVIEW" == "true" ]]; then
  echo "Required reviewer teams: $GITHUB_REQUIRED_REVIEWER_TEAMS"
  echo "Self-review prevented: $GITHUB_PREVENT_SELF_REVIEW"
fi
