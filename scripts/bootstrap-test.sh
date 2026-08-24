#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
config_file="$repo_root/infra/environments/test.env"
credential_file="$repo_root/.env.test"

required_commands=(az gh jq openssl)
for command_name in "${required_commands[@]}"; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Missing required command: $command_name" >&2
    exit 1
  fi
done

if [[ ! -f "$credential_file" ]]; then
  echo "Missing $credential_file. Run ./scripts/create-test-credentials.sh first." >&2
  exit 1
fi

# shellcheck disable=SC1090
source "$config_file"
# shellcheck disable=SC1090
source "$credential_file"
chmod 600 "$credential_file"

required_variables=(
  AZURE_SUBSCRIPTION_ID
  AZURE_TENANT_ID
  AZURE_LOCATION
  AZURE_RESOURCE_GROUP
  AZURE_ENVIRONMENT_NAME
  GITHUB_OWNER
  GITHUB_REPOSITORY
  GITHUB_ENVIRONMENT
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
deployment_identity_name="id-${repository_name_lower}-${GITHUB_ENVIRONMENT}-deploy"
federated_credential_name="github-${repository_id}-${GITHUB_ENVIRONMENT}"

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
  --template-file "$repo_root/infra/bootstrap.bicep" \
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

jq -n '{wait_timer:0,deployment_branch_policy:{protected_branches:false,custom_branch_policies:true}}' \
  | gh api \
      --method PUT \
      "repos/$GITHUB_OWNER/$GITHUB_REPOSITORY/environments/$GITHUB_ENVIRONMENT" \
      --input - \
      --silent

branch_policy_count="$(gh api \
  "repos/$GITHUB_OWNER/$GITHUB_REPOSITORY/environments/$GITHUB_ENVIRONMENT/deployment-branch-policies" \
  --jq '[.branch_policies[] | select(.name == "main" and .type == "branch")] | length')"
if [[ "$branch_policy_count" -eq 0 ]]; then
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
