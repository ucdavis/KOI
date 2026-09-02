# KOI

Kuali Operational Integrations provides narrow API endpoints that enrich Kuali
Build, including financial chart details from Aggie Enterprise.

## Endpoints

| Method | Route | Authentication | Response |
| --- | --- | --- | --- |
| `GET` | `/api/health` | None | `{"status":"healthy","service":"KOI","version":"0.1.3","revision":"<git-sha-or-local>"}` |
| `GET` | `/api/v1/hello` | Bearer token | `{"message":"Hello from KOI"}` |
| `GET` | `/api/v1/financial/details/{value}` | Bearer token | Flattened financial details for one chart string |

The financial details endpoint returns a flattened subset of the Aggie Enterprise
data. Its response includes `chartType` and the display-ready `validationStatus`
for Kuali Build. Chart types are returned as `GL`, `PPM`, or `INVALID`.
Valid GL and PPM results return
`This is a valid GL chart string.` and `This is a valid PPM chart string.`,
respectively. Invalid results return `This is not a valid chart string.`

All HTTP functions require authentication by default. The health function is
the only explicit anonymous exception.

## Authentication

Clients send exactly one credential using the standard authorization header:

```http
Authorization: Bearer <token>
```

KOI requires exactly two configured credential slots, with at least one enabled,
so a new key can be introduced and tested before the previous key is disabled.
Tokens should contain at least 256 bits of randomness. Only SHA-256 hashes are
supplied to the application; comparisons are constant-time. Missing, malformed,
disabled, and incorrect credentials all receive the same `401 Unauthorized`
response.

Plaintext development keys belong only in the gitignored `.env`. Never put a
real token in source, tests, GitHub configuration, logs, or documentation.
See [API key management](docs/api-key-management.md) for production key
generation, configuration, and rotation.

## Local development

Prerequisites:

- .NET SDK 10.0.201 or a compatible patch release
- Azure Functions Core Tools 4
- `openssl`

Create two fresh local credentials if `.env` does not already exist:

```bash
./scripts/create-local-env.sh
```

The generated file is mode `600` and contains Azure-shaped credential IDs and
hashes plus the matching plaintext local client tokens. Add the six
`Financial__*` settings shown in `.env.example`, then start the Function host:

```bash
./scripts/run-local.sh
```

In another terminal, exercise the complete local contract:

```bash
./scripts/smoke-local.sh
```

The Function loads the gitignored `.env` only in the local Development
environment. Environment variables take precedence, so deployed Azure settings
remain authoritative.

### Visual Studio on Windows

Copy
`src/Koi.Functions/local.settings.json.example` to
`src/Koi.Functions/local.settings.json`, place the correctly generated `.env`
at the repository root, set `Koi.Functions` as the startup project, and press
F5. `local.settings.json` supplies only the Functions host settings; application
configuration comes from `.env`. The health endpoint is then available at
`http://localhost:7071/api/health`. Use either plaintext local token from `.env`
as a Bearer token when calling authenticated endpoints from an API client.

Run the automated test suite with:

```bash
dotnet test
```

## Azure deployment

KOI test and production infrastructure is declared in Bicep and deployed from
GitHub Actions. The pipeline builds and tests once, deploys the immutable
artifact to test, verifies it, and then waits for production approval. After a
required reviewer approves the protected `production` environment, the same
artifact is deployed and verified there. The Function exports telemetry directly
to the central Elastic collector over OTLP; Application Insights and Log
Analytics are not provisioned.

The one-time bootstrap creates the Bicep-managed resource group and deployment
managed identity, the environment-scoped GitHub OIDC trust, and the matching
GitHub environment. The bootstrap does not create an Azure client secret.
GitHub receives only KOI API-key IDs and SHA-256 hashes, never plaintext KOI
bearer tokens; Aggie Enterprise credentials are stored as GitHub environment
secrets.

See [Azure deployment](docs/deployment.md) for the resource boundary,
bootstrap procedure, deployment flow, verification, and rollback.
