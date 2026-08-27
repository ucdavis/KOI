# Azure deployment

KOI uses Bicep for Azure infrastructure and GitHub Actions for application and
infrastructure deployment. Test and production have separate Azure boundaries,
managed identities, GitHub environments, credentials, and downstream settings.

## Deployment boundaries

The tracked environment files own every non-secret boundary value and review
policy:

- [`infra/environments/test.env`](../infra/environments/test.env)
- [`infra/environments/production.env`](../infra/environments/production.env)

The deployment scripts load these files and verify the selected subscription
and tenant before any Azure write. Do not rely on the Azure CLI's previously
selected subscription. Both GitHub environments admit deployments only from
`main`.

The production entries are configuration only until an operator runs the
production bootstrap. This branch does not create the production resource group,
managed identity, GitHub environment, credentials, or Function App by itself.

## Azure resources

`infra/bootstrap.bicep` creates one resource group and one GitHub deployment
identity per environment. `infra/main.bicep` creates the workload resources:

- .NET 10 isolated Azure Function on a 2 GB Flex Consumption plan
- site-scoped certificate support for the planned custom domain
- system-assigned Function managed identity for future downstream access
- private deployment container in a dedicated Storage account
- direct OpenTelemetry export to the central Elastic collector
- Function settings containing API-key IDs and SHA-256 hashes
- Function settings containing Aggie Enterprise endpoints, scopes, and OAuth
  credentials

Resource names use a stable `uniqueString` suffix derived from the subscription,
resource group, and short environment name. Bicep outputs the Function name,
default URL, managed identity object ID, and Storage account name.

Application Insights and Log Analytics are intentionally not provisioned. The
Function sends telemetry to the central Elastic collector. Azure platform
metrics remain available independently of Application Insights.

The Function host and deployment container use an Azure Storage connection
string stored in encrypted Function settings. Each GitHub deployment identity
has `Contributor` only on its environment's resource group.

## One-time environment bootstrap

The bootstrap is separate because GitHub cannot use Azure OIDC until the
user-assigned managed identity and federated credential exist.

Create two independent credentials for the selected environment:

```bash
./scripts/create-environment-credentials.sh test
./scripts/create-environment-credentials.sh production
```

Each invocation creates a gitignored, mode-`600` handoff file named after the
tracked environment, such as `.env.test` or `.env.production`. Move both
plaintext tokens to the approved password manager. Never put them in GitHub or
Azure configuration. Retain the local file only while an operator needs it for
GitHub configuration or authenticated deployment checks.

Run the idempotent bootstrap for one environment at a time:

```bash
./scripts/bootstrap-environment.sh test
./scripts/bootstrap-environment.sh production
```

The bootstrap performs these bounded operations:

1. Loads and validates `infra/environments/<environment>.env`.
2. Resolves every configured GitHub reviewer team before making an Azure change.
3. Selects and verifies the configured Azure subscription and tenant.
4. Deploys `infra/bootstrap.bicep` to create or synchronize the resource group,
   environment-scoped deployment identity, OIDC credential, and resource-group
   `Contributor` assignment.
5. Creates or synchronizes the matching GitHub environment and restricts it to
   `main`.
6. Applies the configured reviewer teams and self-review policy.
7. Writes the non-secret Azure and API-key ID variables to the GitHub
   environment.
8. Stores only the two SHA-256 API-key hashes as GitHub environment secrets.

The script never sends a plaintext KOI bearer token to GitHub or Azure.

## Aggie Enterprise Financial configuration

The Financial endpoints require six Aggie Enterprise settings. Add the values
for one environment to its local handoff file:

```dotenv
Financial__ApiUrl='https://replace-with-graphql-endpoint'
Financial__ConsumerKey='replace-with-consumer-key'
Financial__ConsumerSecret='replace-with-consumer-secret'
Financial__TokenEndpoint='https://replace-with-token-endpoint'
Financial__ScopeApp='KOI'
Financial__ScopeEnv='replace-with-environment-scope'
```

Synchronize one environment at a time:

```bash
./scripts/configure-environment-financial.sh test
./scripts/configure-environment-financial.sh production
```

The API URL, token endpoint, application scope, and environment scope become
GitHub environment variables. The consumer key and secret become GitHub
environment secrets. The Function validates the complete configuration during
startup.

Do not merge the production deployment until all six production values are
present. Test and production values must come from their respective Aggie
Enterprise environments.

## Elastic OpenTelemetry configuration

Add the Elastic values to the same local environment handoff:

```dotenv
OTEL_EXPORTER_OTLP_ENDPOINT='https://replace-with-collector-endpoint'
OTEL_EXPORTER_OTLP_HEADERS='Authorization=ApiKey replace-with-credential'
OTEL_EXPORTER_OTLP_PROTOCOL='grpc'
```

`OTEL_EXPORTER_OTLP_PROTOCOL` may be `grpc` or `http/protobuf` and defaults to
`grpc`. Synchronize one environment at a time:

```bash
./scripts/configure-environment-otel.sh test
./scripts/configure-environment-otel.sh production
```

The endpoint and protocol become GitHub environment variables. The
authentication header becomes a GitHub environment secret. Bicep supplies these
resource attributes:

```text
service.name=koi
service.version=<version from Koi.Functions.csproj>
deployment.environment=<test or prod>
service.namespace=ucdavis
```

For local telemetry, put the same `OTEL_EXPORTER_OTLP_*` values in the
gitignored `.env`. Development startup uses `deployment.environment=local`.

## Promotion workflow

The workflows use pinned action commit SHAs and minimal job permissions.

- `CI` runs for pull requests and manual checks. It restores locked NuGet
  dependencies, runs the tests, publishes the Function, and compiles both
  Bicep entry points.
- `Deploy` runs after a push to `main` or a manual dispatch. It builds and tests
  once and uploads one immutable artifact named with the full commit SHA.
- `deploy_test` calls the reusable environment workflow with the `test` GitHub
  environment. It runs Bicep what-if and deployment, deploys the Function ZIP,
  and proves the public contract and deployed revision.
- `deploy_production` has `needs: deploy_test`, so it cannot start until the test
  deployment and smoke test succeed. It targets the protected `production`
  environment and waits for a configured reviewer before it receives production
  secrets or an Azure OIDC token.
- After approval, production deploys the exact artifact and revision that passed
  test, then runs the same public smoke test.

The workflow-level concurrency group allows only one promotion run at a time.
An older revision waiting for production approval cannot be leapfrogged by a
newer run.

The reusable `.github/workflows/deploy-environment.yml` owns all Azure and
Function deployment mechanics. Environment-specific workflow copies are not
allowed.

## Verification

The automated smoke test waits for the expected Git revision and verifies:

- `/api/health` returns `200` with the deployed commit SHA
- `/api/v1/hello` returns `401` without credentials
- `/api/v1/hello` returns `401` for an invalid credential
- unauthorized responses advertise `WWW-Authenticate: Bearer`

GitHub never receives plaintext KOI tokens, so the automated workflow cannot
make a successful authenticated request. From the trusted local handoff, verify
both token slots and the live Financial integration with a known valid chart
string:

```bash
./scripts/smoke-authenticated.sh \
  https://<function-app>.azurewebsites.net \
  '<known-valid-chart-string>' \
  test

./scripts/smoke-authenticated.sh \
  https://<function-app>.azurewebsites.net \
  '<known-valid-chart-string>' \
  production
```

The script reads plaintext tokens only from `.env.<environment>`, sends the
authorization header through curl standard input, and never places a token on
the command line. It requires both token slots to return `200`, then requires a
valid Aggie Enterprise response with no errors.

Use the UTC boundary printed by the script to verify Elastic ingestion and
token redaction. A successful deployment or HTTP response does not prove
telemetry delivery.

## Custom domains

Keep the `azurewebsites.net` URL as the deployment and smoke-test endpoint. Add
`koi-test.ucdavis.edu` and `koi.ucdavis.edu` after their Function Apps exist:

1. Deploy the Function and verify its `azurewebsites.net` URL.
2. Configure the DNS records required to validate the custom hostname.
3. Map the custom hostname to the existing Function App.
4. Request an App Service managed certificate for the mapped hostname, then bind
   it to the custom domain.

The custom domains supplement the default URLs. A custom-domain outage must not
hide the state of the underlying Function deployment.

## Rollback

Deployment artifacts are retained for 14 days and named with the full commit
SHA. Re-run the chosen prior `Deploy` workflow. It redeploys that revision to
test first and requires a new production approval. Confirm `/api/health`
reports the intended rollback revision in both environments.
