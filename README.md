# KOI

Kuali Operational Integrations provides narrow API endpoints that enrich Kuali Build.

This initial .NET 10 Azure Functions foundation intentionally has no database or
downstream integrations. It proves host health and Function-side API-key
authentication.

## Endpoints

| Method | Route | Authentication | Response |
| --- | --- | --- | --- |
| `GET` | `/api/health` | None | `{"status":"healthy","service":"KOI","version":"0.1.0","revision":"<git-sha-or-local>"}` |
| `GET` | `/api/v1/hello` | Bearer token | `{"message":"Hello from KOI"}` |

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

The generated file is mode `600`. Start the Function host:

```bash
./scripts/run-local.sh
```

In another terminal, exercise the complete local contract:

```bash
./scripts/smoke-local.sh
```

The run helper derives hashes and exports only the key IDs and hashes to the
Function process. It does not export the plaintext `.env` variables.

Run the automated test suite with:

```bash
dotnet test
```

## Azure deployment

KOI test infrastructure is declared in Bicep and deployed from GitHub Actions.
The pipeline builds and tests once, compiles the Bicep, uploads an immutable
artifact, synchronizes Azure infrastructure, deploys that exact Function
package, and verifies the public contract. The Function exports telemetry
directly to the central Elastic collector over OTLP; Application Insights and
Log Analytics are not provisioned.

The one-time bootstrap creates the Bicep-managed resource group and deployment
managed identity, the environment-scoped GitHub OIDC trust, and the GitHub
`test` environment. It does not create an Azure client secret, and GitHub
receives only API-key IDs and SHA-256 hashes, never plaintext tokens.

See [Azure deployment](docs/deployment.md) for the resource boundary,
bootstrap procedure, deployment flow, verification, and rollback.
