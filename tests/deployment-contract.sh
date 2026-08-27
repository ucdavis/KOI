#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
test_root="$(mktemp -d "${TMPDIR:-/tmp}/koi-deployment-contract.XXXXXX")"
trap 'rm -rf "$test_root"' EXIT

main_arm="$test_root/main.json"
bootstrap_arm="$test_root/bootstrap.json"

az bicep build --file "$repo_root/infra/main.bicep" --stdout >"$main_arm"
az bicep build --file "$repo_root/infra/bootstrap.bicep" --stdout >"$bootstrap_arm"

ruby -ryaml -rjson - "$repo_root" "$main_arm" "$bootstrap_arm" <<'RUBY'
repo_root, main_arm_path, bootstrap_arm_path = ARGV

deploy = YAML.safe_load(File.read(File.join(repo_root, ".github/workflows/deploy.yml")), aliases: true)
reusable = YAML.safe_load(File.read(File.join(repo_root, ".github/workflows/deploy-environment.yml")), aliases: true)

jobs = deploy.fetch("jobs")
build = jobs.fetch("build")
test_deploy = jobs.fetch("deploy_test")
production_deploy = jobs.fetch("deploy_production")

raise "test must wait for the build" unless test_deploy.fetch("needs") == "build"
raise "production must wait for build and test" unless production_deploy.fetch("needs") == ["build", "deploy_test"]
raise "environment deployments must share one workflow" unless test_deploy.fetch("uses") == production_deploy.fetch("uses")
raise "unexpected reusable deployment workflow" unless test_deploy.fetch("uses") == "./.github/workflows/deploy-environment.yml"
raise "test environment input is wrong" unless test_deploy.fetch("with").fetch("deployment_environment") == "test"
raise "production environment input is wrong" unless production_deploy.fetch("with").fetch("deployment_environment") == "production"
raise "test environment secrets are not inherited" unless test_deploy.fetch("secrets") == "inherit"
raise "production environment secrets are not inherited" unless production_deploy.fetch("secrets") == "inherit"
raise "test revision is not the triggering commit" unless test_deploy.fetch("with").fetch("revision") == "${{ github.sha }}"
raise "production revision differs from test" unless production_deploy.fetch("with").fetch("revision") == test_deploy.fetch("with").fetch("revision")
raise "service version is not reused" unless production_deploy.fetch("with").fetch("service_version") == test_deploy.fetch("with").fetch("service_version")

upload = build.fetch("steps").find { |step| step["name"] == "Upload immutable deployment artifact" }
raise "immutable artifact upload step is missing" unless upload
raise "artifact is not keyed by the full revision" unless upload.fetch("with").fetch("name") == "koi-deployment-${{ github.sha }}"

reusable_jobs = reusable.fetch("jobs")
raise "reusable workflow must own one deployment job" unless reusable_jobs.keys == ["deploy"]
reusable_deploy = reusable_jobs.fetch("deploy")
raise "GitHub environment is not bound before deployment" unless reusable_deploy.fetch("environment").fetch("name") == "${{ inputs.deployment_environment }}"

download = reusable_deploy.fetch("steps").find { |step| step["name"] == "Download immutable deployment artifact" }
raise "artifact download step is missing" unless download
raise "download does not use the promoted revision" unless download.fetch("with").fetch("name") == "koi-deployment-${{ inputs.revision }}"

infrastructure = reusable_deploy.fetch("steps").find { |step| step["name"] == "Synchronize Bicep infrastructure" }
raise "infrastructure deployment step is missing" unless infrastructure
raise "deployment does not reject a non-default Function URL" unless infrastructure.fetch("run").include?("\\.azurewebsites\\.net")

smoke = reusable_deploy.fetch("steps").find { |step| step["name"] == "Verify deployed contract" }
raise "deployed smoke step is missing" unless smoke
raise "smoke test does not use the Bicep default Function URL" unless smoke.fetch("env").fetch("FUNCTION_URL") == "${{ steps.infrastructure.outputs.function_url }}"

login = reusable_deploy.fetch("steps").find { |step| step["name"] == "Sign in to Azure with GitHub OIDC" }
raise "Azure OIDC login step is missing" unless login
login_inputs = login.fetch("with")
raise "Azure client ID is not environment-scoped" unless login_inputs.fetch("client-id") == "${{ vars.AZURE_CLIENT_ID }}"
raise "Azure tenant ID is not environment-scoped" unless login_inputs.fetch("tenant-id") == "${{ vars.AZURE_TENANT_ID }}"
raise "Azure subscription ID is not environment-scoped" unless login_inputs.fetch("subscription-id") == "${{ vars.AZURE_SUBSCRIPTION_ID }}"

main_arm = JSON.parse(File.read(main_arm_path))
app_settings = main_arm.fetch("resources").find { |resource| resource["type"] == "Microsoft.Web/sites/config" }
raise "compiled Function settings are missing" unless app_settings
otel_attributes = app_settings.fetch("properties").fetch("OTEL_RESOURCE_ATTRIBUTES")
raise "telemetry environment is not derived from the deployment environment" unless otel_attributes.include?("parameters('environmentName')")
raise "KOI service metadata is missing" unless otel_attributes.include?("service.name=koi") && otel_attributes.include?("service.namespace=ucdavis")

function_app = main_arm.fetch("resources").find { |resource| resource["type"] == "Microsoft.Web/sites" }
raise "compiled Function App is missing" unless function_app
raise "Flex custom-domain certificate support is disabled" unless function_app.fetch("properties").fetch("siteScopedCertificatesEnabled") == true

function_url = main_arm.fetch("outputs").fetch("functionAppUrl").fetch("value")
raise "deployment URL is not based on the Azure default hostname" unless function_url.include?("defaultHostName")

bootstrap_arm = JSON.parse(File.read(bootstrap_arm_path))
module_deployment = bootstrap_arm.fetch("resources").find { |resource| resource["type"] == "Microsoft.Resources/deployments" }
raise "compiled bootstrap module is missing" unless module_deployment
raise "bootstrap identity is not scoped to the configured resource group" unless module_deployment.fetch("resourceGroup") == "[parameters('resourceGroupName')]"

inner_template = module_deployment.fetch("properties").fetch("template")
contributor_id = inner_template.fetch("variables").fetch("contributorRoleDefinitionId")
raise "deployment identity does not use Contributor" unless contributor_id.include?("b24988ac-6180-42a0-ab88-20f7382dd24c")
role_assignment = inner_template.fetch("resources").find { |resource| resource["type"] == "Microsoft.Authorization/roleAssignments" }
raise "resource-group Contributor assignment is missing" unless role_assignment
raise "role assignment escaped the module resource group" if role_assignment.key?("scope")

puts "PASS workflow promotes one revision through test before protected production"
puts "PASS one reusable environment job binds OIDC and secrets to its GitHub environment"
puts "PASS compiled Bicep scopes Contributor to the environment resource group"
puts "PASS compiled Function settings derive deployment.environment from environmentName"
puts "PASS deployment output remains the Function App default hostname"
RUBY

sandbox="$test_root/repository"
capture_dir="$test_root/captured"
mkdir -p "$sandbox/scripts" "$sandbox/infra/environments" "$capture_dir"
cp "$repo_root/scripts/deployment-environment.sh" "$sandbox/scripts/"
cp "$repo_root/scripts/bootstrap-environment.sh" "$sandbox/scripts/"
cp "$repo_root/scripts/configure-environment-financial.sh" "$sandbox/scripts/"
cp "$repo_root/scripts/configure-environment-otel.sh" "$sandbox/scripts/"
cp "$repo_root/infra/environments/production.env" "$sandbox/infra/environments/"
cp "$repo_root/infra/bootstrap.bicep" "$sandbox/infra/"
cp "$repo_root/infra/bootstrap-resources.bicep" "$sandbox/infra/"

credential_file="$sandbox/.env.production"
printf '%s\n' \
  'AZURE_SUBSCRIPTION_ID=00000000-0000-0000-0000-000000000000' \
  'AZURE_TENANT_ID=00000000-0000-0000-0000-000000000000' \
  'AZURE_RESOURCE_GROUP=rg-attacker' \
  'AZURE_ENVIRONMENT_NAME=attacker' \
  'GITHUB_OWNER=attacker' \
  'GITHUB_REPOSITORY=attacker' \
  'GITHUB_ENVIRONMENT=attacker' \
  'GITHUB_REQUIRE_REVIEW=false' \
  'GITHUB_PREVENT_SELF_REVIEW=false' \
  'GITHUB_REQUIRED_REVIEWER_TEAMS=' \
  'KOI_API_KEY_1_ID=kuali-prod-a' \
  'KOI_API_KEY_1=production-token-one-0123456789abcdef' \
  'KOI_API_KEY_2_ID=kuali-prod-b' \
  'KOI_API_KEY_2=production-token-two-0123456789abcdef' \
  'Financial__ApiUrl=https://financial.example.test/graphql' \
  'Financial__ConsumerKey=financial-key' \
  'Financial__ConsumerSecret=financial-secret' \
  'Financial__TokenEndpoint=https://financial.example.test/oauth/token' \
  'Financial__ScopeApp=KOI' \
  'Financial__ScopeEnv=Production' \
  'OTEL_EXPORTER_OTLP_ENDPOINT=https://otel.example.test:443' \
  "OTEL_EXPORTER_OTLP_HEADERS='Authorization=ApiKey test-collector-key'" \
  'OTEL_EXPORTER_OTLP_PROTOCOL=grpc' >"$credential_file"
chmod 600 "$credential_file"

export CAPTURE_DIR="$capture_dir"

az() {
  {
    printf 'az'
    printf ' %q' "$@"
    printf '\n'
  } >>"$CAPTURE_DIR/azure-requests.log"

  if [[ "$1" == "account" && "$2" == "set" ]]; then
    [[ "$3" == "--subscription" && "$4" == "003283b1-cc5e-417a-b037-01ff3c05537b" ]]
    return
  fi

  if [[ "$1" == "account" && "$2" == "show" && "$3" == "--query" && "$4" == "id" ]]; then
    printf '%s\n' '003283b1-cc5e-417a-b037-01ff3c05537b'
    return
  fi

  if [[ "$1" == "account" && "$2" == "show" && "$3" == "--query" && "$4" == "tenantId" ]]; then
    printf '%s\n' 'a8046f64-66c0-4f00-9046-c8daf92ff62b'
    return
  fi

  if [[ "$1" == "deployment" && "$2" == "sub" && "$3" == "create" ]]; then
    local joined=" $* "
    [[ "$joined" == *' deploymentIdentityName=id-koi-prod-deploy '* ]]
    [[ "$joined" == *' environmentName=prod '* ]]
    [[ "$joined" == *' federatedCredentialName=github-1345245163-production '* ]]
    [[ "$joined" == *' githubOidcSubject=repo:ucdavis@573450/KOI@1345245163:environment:production '* ]]
    [[ "$joined" == *' location=westus2 '* ]]
    [[ "$joined" == *' resourceGroupName=rg-koi-prod '* ]]
    printf '%s\n' '{"deploymentIdentityClientId":{"value":"9f446bcf-d25b-49fc-af68-bee610039a4f"},"deploymentIdentityPrincipalId":{"value":"da127ee2-5402-4ff3-b53a-9b1632ee5b0d"}}'
    return
  fi

  printf 'Unexpected az call: %s\n' "$*" >&2
  return 1
}
export -f az

gh() {
  if [[ "$1" == "api" ]]; then
    shift
    local method='GET'
    local endpoint=''
    local input=''
    local argument
    local -a fields=()

    while [[ "$#" -gt 0 ]]; do
      argument="$1"
      case "$argument" in
        --method)
          method="$2"
          shift 2
          ;;
        --input)
          input="$2"
          shift 2
          ;;
        --jq)
          shift 2
          ;;
        --paginate|--silent)
          shift
          ;;
        -f)
          fields+=("$2")
          shift 2
          ;;
        *)
          if [[ -z "$endpoint" ]]; then
            endpoint="$argument"
          fi
          shift
          ;;
      esac
    done

    printf 'gh api %s %s' "$method" "$endpoint" >>"$CAPTURE_DIR/github-requests.log"
    if [[ "${#fields[@]}" -gt 0 ]]; then
      printf ' %s' "${fields[@]}" >>"$CAPTURE_DIR/github-requests.log"
    fi
    printf '\n' >>"$CAPTURE_DIR/github-requests.log"

    case "$method $endpoint" in
      'GET repos/ucdavis/KOI')
        printf '%s\n' '{"id":1345245163}'
        ;;
      'GET repos/ucdavis/KOI/actions/oidc/customization/sub')
        printf '%s\n' '{"sub_claim_prefix":"repo:ucdavis@573450/KOI@1345245163"}'
        ;;
      'GET orgs/ucdavis/teams/caesdo-devs')
        printf '%s\n' '3593473'
        ;;
      'PUT repos/ucdavis/KOI/environments/production')
        [[ "$input" == '-' ]]
        jq -c . >"$CAPTURE_DIR/environment-payload.json"
        ;;
      'GET repos/ucdavis/KOI/environments/production/deployment-branch-policies?per_page=100')
        ;;
      'POST repos/ucdavis/KOI/environments/production/deployment-branch-policies')
        [[ " ${fields[*]} " == *' name=main '* ]]
        [[ " ${fields[*]} " == *' type=branch '* ]]
        ;;
      *)
        printf 'Unexpected gh api call: %s %s\n' "$method" "$endpoint" >&2
        return 1
        ;;
    esac
    return
  fi

  if [[ "$1" == "variable" && "$2" == "set" ]]; then
    local name="$3"
    shift 3
    local repo=''
    local environment=''
    local body=''
    while [[ "$#" -gt 0 ]]; do
      case "$1" in
        --repo)
          repo="$2"
          shift 2
          ;;
        --env)
          environment="$2"
          shift 2
          ;;
        --body)
          body="$2"
          shift 2
          ;;
        *)
          return 1
          ;;
      esac
    done
    [[ "$repo" == 'ucdavis/KOI' && "$environment" == 'production' ]]
    printf '%s\t%s\n' "$name" "$body" >>"$CAPTURE_DIR/environment-variables.tsv"
    return
  fi

  if [[ "$1" == "secret" && "$2" == "set" ]]; then
    local name="$3"
    shift 3
    local repo=''
    local environment=''
    while [[ "$#" -gt 0 ]]; do
      case "$1" in
        --repo)
          repo="$2"
          shift 2
          ;;
        --env)
          environment="$2"
          shift 2
          ;;
        *)
          return 1
          ;;
      esac
    done
    [[ "$repo" == 'ucdavis/KOI' && "$environment" == 'production' ]]
    local secret_value=''
    IFS= read -r secret_value || true
    printf '%s\t%s\n' "$name" "$secret_value" >>"$CAPTURE_DIR/environment-secrets.tsv"
    return
  fi

  printf 'Unexpected gh call: %s\n' "$*" >&2
  return 1
}
export -f gh

bash "$sandbox/scripts/bootstrap-environment.sh" production
bash "$sandbox/scripts/configure-environment-financial.sh" production
bash "$sandbox/scripts/configure-environment-otel.sh" production

jq -e '
  .reviewers == [{"type":"Team","id":3593473}]
  and .deployment_branch_policy == {"protected_branches":false,"custom_branch_policies":true}
' "$capture_dir/environment-payload.json" >/dev/null

expected_key_1_hash="$(printf '%s' 'production-token-one-0123456789abcdef' | openssl dgst -sha256 -r | awk '{print $1}')"
expected_key_2_hash="$(printf '%s' 'production-token-two-0123456789abcdef' | openssl dgst -sha256 -r | awk '{print $1}')"
actual_key_1_hash="$(awk -F '\t' '$1 == "KOI_API_KEY_1_SHA256" { print $2 }' "$capture_dir/environment-secrets.tsv")"
actual_key_2_hash="$(awk -F '\t' '$1 == "KOI_API_KEY_2_SHA256" { print $2 }' "$capture_dir/environment-secrets.tsv")"
[[ "$actual_key_1_hash" == "$expected_key_1_hash" ]]
[[ "$actual_key_2_hash" == "$expected_key_2_hash" ]]
if grep -F 'production-token-' "$capture_dir/environment-secrets.tsv" >/dev/null; then
  echo 'Plaintext KOI token reached the captured GitHub secret request.' >&2
  exit 1
fi

awk -F '\t' '$1 == "AZURE_RESOURCE_GROUP" && $2 == "rg-koi-prod" { found=1 } END { exit !found }' "$capture_dir/environment-variables.tsv"
awk -F '\t' '$1 == "AZURE_ENVIRONMENT_NAME" && $2 == "prod" { found=1 } END { exit !found }' "$capture_dir/environment-variables.tsv"
awk -F '\t' '$1 == "FINANCIAL_SCOPE_ENV" && $2 == "Production" { found=1 } END { exit !found }' "$capture_dir/environment-variables.tsv"
awk -F '\t' '$1 == "OTEL_EXPORTER_OTLP_PROTOCOL" && $2 == "grpc" { found=1 } END { exit !found }' "$capture_dir/environment-variables.tsv"
awk -F '\t' '$1 == "FINANCIAL_CONSUMER_SECRET" && $2 == "financial-secret" { found=1 } END { exit !found }' "$capture_dir/environment-secrets.tsv"
awk -F '\t' '$1 == "OTEL_EXPORTER_OTLP_HEADERS" && $2 == "Authorization=ApiKey test-collector-key" { found=1 } END { exit !found }' "$capture_dir/environment-secrets.tsv"

echo 'PASS production loader overrides conflicting local boundary values'
echo 'PASS bootstrap synchronizes production Azure and GitHub environment boundaries'
echo 'PASS bootstrap sends only KOI SHA-256 hashes to GitHub'
echo 'PASS Financial and Elastic values stay scoped to the production environment'
