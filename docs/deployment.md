# Azure deployment

KOI uses Bicep for Azure infrastructure and GitHub Actions for application and
infrastructure deployment. The initial target is the `test` environment.

## Deployment boundary

| Setting | Value |
| --- | --- |
| Subscription | `UC Davis CAES Test` (`105dede4-4731-492e-8c28-5121226319b0`) |
| Tenant | `a8046f64-66c0-4f00-9046-c8daf92ff62b` |
| Region | West US 2 (`westus2`) |
| Resource group | `rg-koi-test` |
| GitHub environment | `test` |
| Public hostname | Direct `azurewebsites.net` hostname |

The deployment scripts and workflow verify the subscription and tenant before
making changes. Do not rely on the Azure CLI's previously selected
subscription.

## Azure resources

`infra/bootstrap.bicep` creates the resource group and GitHub deployment
identity. `infra/main.bicep` creates all workload resources inside it:

- .NET 10 isolated Azure Function on a 2 GB Flex Consumption plan
- system-assigned Function managed identity for future downstream access
- private deployment container in a dedicated Storage account
- direct OpenTelemetry export to the central Elastic collector
- Function application settings containing API-key IDs and SHA-256 hashes
- Function application settings containing Aggie Enterprise endpoints, scopes,
  and OAuth credentials

Resource names use a stable `uniqueString` suffix derived from the subscription,
resource group, and environment. Bicep outputs the Function name, URL, managed
identity object ID, and Storage account name.

Application Insights and Log Analytics are intentionally not provisioned. Their
log ingestion can incur Azure Monitor charges and would duplicate the central
Elastic destination. Azure platform metrics remain available independently of
Application Insights.

The Function host and deployment container currently use an Azure Storage
connection string stored in encrypted Function application settings. This keeps
the GitHub deployment identity limited to `Contributor` on `rg-koi-test` and
avoids granting it role-assignment administration. Moving host storage to
managed identity is possible later, but requires Storage data-plane roles and a
separate, tightly scoped RBAC deployment path.

## One-time bootstrap

The bootstrap is intentionally separate because GitHub cannot use Azure OIDC
until the user-assigned managed identity and federated credential already
exist.

Create two independent test credentials:

```bash
./scripts/create-test-credentials.sh
```

This creates a gitignored, mode-`600` `.env.test` handoff file. Move both
plaintext tokens to the approved password manager. Never put them in GitHub or
Azure configuration. Retain the local file only while an operator needs it for
authenticated deployment checks or the local Elastic exporter handoff; remove
it after the tokens are safely stored and configured in Kuali and those checks
are complete.

Run the idempotent bootstrap:

```bash
./scripts/bootstrap-test.sh
```

It performs these bounded operations:

1. Selects and verifies the configured Azure subscription and tenant.
2. Deploys `infra/bootstrap.bicep` to create or synchronize `rg-koi-test`.
3. Creates or synchronizes the `id-koi-test-deploy` user-assigned managed
   identity with Bicep.
4. Derives the repository's current immutable GitHub OIDC subject and creates
   an environment-scoped federated credential on that identity with Bicep.
5. Assigns the managed identity `Contributor` only on `rg-koi-test` with
   Bicep.
6. Creates the GitHub `test` environment, limits it to `main`, and configures
   non-secret deployment variables.
7. Stores only the two SHA-256 hashes as GitHub environment secrets.

The Azure identity, federated credential, role assignment, resource group, and
workload resources are Bicep-managed. The bootstrap script uses `gh` only for
the GitHub environment, variables, branch policy, and hashed API-key secrets.

## Aggie Enterprise Financial configuration

The Financial endpoints require six Aggie Enterprise settings. Add the real
values to the gitignored, mode-`600` `.env.test` handoff file:

```dotenv
Financial__ApiUrl='https://replace-with-graphql-endpoint'
Financial__ConsumerKey='replace-with-consumer-key'
Financial__ConsumerSecret='replace-with-consumer-secret'
Financial__TokenEndpoint='https://replace-with-token-endpoint'
Financial__ScopeApp='KOI'
Financial__ScopeEnv='Test'
```

Synchronize those values into the GitHub `test` environment:

```bash
bash ./scripts/configure-test-financial.sh
```

The API URL, token endpoint, application scope, and environment scope are
stored as GitHub environment variables. The consumer key and consumer secret
are stored as GitHub environment secrets. The deployment workflow passes both
credentials as secure Bicep parameters and writes all six values to the
Function application settings under their `Financial__*` names. The Function
validates the complete Financial configuration during startup.

Do not deploy until all six GitHub values are present. Neither credential is
written to source control, workflow output, deployment output, or
documentation.

## Elastic OpenTelemetry configuration

The Function sends logs, traces, and metrics directly to the central Elastic
collector using standard OTLP settings. The collector endpoint and protocol are
GitHub environment variables; the authentication header is a GitHub environment
secret and becomes a secure Bicep parameter and Function application setting.

Add the real values to the gitignored, mode-`600` `.env.test` handoff file. This
keeps the header value off the command line, shell history, and Git repository:

```dotenv
OTEL_EXPORTER_OTLP_ENDPOINT='https://replace-with-collector-endpoint'
OTEL_EXPORTER_OTLP_HEADERS='Authorization=ApiKey replace-with-credential'
```

Elastic commonly supplies a header in the form
`Authorization=ApiKey <credential>`. Use the exact endpoint, protocol, and
header issued for the central collector. `OTEL_EXPORTER_OTLP_PROTOCOL` is
optional and defaults to `grpc`, matching the upstream .NET SDK. Set it to
`http/protobuf` only when the Elastic endpoint specifically requires OTLP/HTTP.
Then synchronize the values into the GitHub `test` environment:

```bash
./scripts/configure-test-otel.sh
```

The script stores the endpoint and protocol as GitHub environment variables and
the authentication header as a GitHub environment secret. Bicep supplies these
resource attributes to every deployed environment:

```text
service.name=koi
service.version=<version from Koi.Functions.csproj>
deployment.environment=<test or production>
service.namespace=ucdavis
```

For local telemetry, copy the same three `OTEL_EXPORTER_OTLP_*` values into the
gitignored `.env`. Development startup loads them for both Visual Studio and
`./scripts/run-local.sh`. The application derives `service.version` from its
assembly and supplies the same resource attributes with
`deployment.environment=local`, so `OTEL_RESOURCE_ATTRIBUTES` is not required.
`.env.test` remains the secure deployment handoff used by the GitHub
configuration and authenticated deployed smoke scripts. Do not deploy until
the endpoint, resolved protocol, and authentication header are present in
GitHub.

## GitHub Actions

The workflows use pinned action commit SHAs and minimal job permissions.

- `CI` runs for pull requests and manual checks. It restores locked NuGet
  dependencies, runs the automated tests in a dedicated step, publishes, and
  compiles both Bicep files. Any failing test returns a nonzero status, fails the
  workflow, and reports its name, assertion, and stack trace in the test step.
- `Deploy` runs after a push to `main` or a manual dispatch. Its build job
  creates one immutable deployment artifact. Its `test` environment job signs
  in through OIDC, runs Bicep what-if and deployment, deploys the exact Function
  ZIP, and runs public smoke tests.

The deployment workflow receives `id-token: write` only in the environment job.
No Azure client secret or plaintext KOI bearer token is stored in GitHub. The
Aggie Enterprise consumer key and consumer secret are stored only as GitHub
environment secrets and encrypted Function application settings.

## Verification

The automated smoke test waits for the expected Git revision and verifies:

- `/api/health` returns `200` with the deployed commit SHA
- `/api/v1/hello` returns `401` without credentials
- `/api/v1/hello` returns `401` for an invalid credential
- unauthorized responses advertise `WWW-Authenticate: Bearer`

Because GitHub never receives plaintext KOI tokens, the automated workflow
cannot make a successful authenticated request. From the trusted local handoff,
verify both active slots and the live Aggie Enterprise Financial integration
after deployment with a known valid test chart string:

```bash
./scripts/smoke-authenticated.sh \
  https://<function-app>.azurewebsites.net \
  '<known-valid-test-chart-string>'
```

The script reads both plaintext tokens only from the gitignored `.env.test`,
sends authorization headers to KOI through curl standard input, and never
passes a token on the command line. It requires both token slots to return `200`
from `/api/v1/hello`, then calls `/api/v1/financial/details/{value}` with the first slot
and requires `200`, the requested chart string, `isValid: true`, and no errors.
That final check proves the deployed Function can authenticate to Aggie
Enterprise and obtain a valid Financial response without placing a plaintext
KOI bearer token in GitHub.

The script prints the UTC start time for those three requests. Use that boundary
to verify in Elastic that all requests arrived and that neither plaintext token
nor a recognizable token prefix or suffix was recorded. The smoke test does not
claim telemetry success until an Elastic query path is configured and checked.

For a direct call with the first test token:

```bash
source .env.test
curl --silent --show-error --fail-with-body \
  --header "Authorization: Bearer $KOI_API_KEY_1" \
  https://<function-app>.azurewebsites.net/api/v1/hello
```

## Rollback

Every deployment artifact is retained for 14 days and named with its full Git
commit SHA. Re-running a successful prior `Deploy` workflow run rebuilds and
redeploys that commit's Bicep and Function package. Confirm the health
`revision` equals the intended rollback commit afterward.

## Future production bootstrap

Production should be a separate GitHub environment, Azure resource group,
deployment managed identity, immutable OIDC credential, API-key pair, and
Elastic telemetry configuration. Before adding it:

1. Confirm the production subscription, region, resource-group name, and GitHub
   environment reviewers.
2. Generalize the test-specific bootstrap and configuration scripts to accept a
   reviewed environment file; do not copy and hand-edit the test scripts.
3. Bootstrap the production identity with `Contributor` scoped only to the
   production resource group and verify the immutable repository/environment
   OIDC subject.
4. Generate production API keys into an approved password manager, then store
   only their IDs and SHA-256 hashes in GitHub.
5. Configure the production Aggie Enterprise endpoints, scopes, consumer key,
   and consumer secret in the protected GitHub environment.
6. Configure the production Elastic endpoint, protocol, and authentication
   header, then add a production deploy job protected by the GitHub environment.
7. Validate Bicep what-if, deploy, run public and authenticated smoke tests, and
   prove ingestion and token redaction in Elastic.

The current workflow intentionally deploys only `test`; production remains
unreachable until that environment-specific path is implemented and reviewed.
