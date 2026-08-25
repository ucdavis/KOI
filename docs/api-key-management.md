# API key management

KOI authenticates callers with high-entropy bearer tokens. Each configured
credential has three parts:

- **ID:** A unique, non-secret label used in configuration and telemetry.
- **Token:** The plaintext secret presented by the caller. KOI never stores it.
- **SHA-256 hash:** The value KOI stores and compares against presented tokens.

KOI requires exactly two configured credential slots, with at least one enabled,
so keys can be rotated without downtime.

## Generate a production token

Generate 256 random bits as a 64-character hexadecimal token:

```bash
openssl rand -hex 32
```

Store the complete output immediately in the approved password manager and
configure it in the calling system. Do not reuse a development token or place
the production token in source control, GitHub configuration, deployment
output, logs, documentation, chat, or a URL.

Generate each rotation token independently. Do not derive one token from
another.

## Choose a credential ID

The ID is a readable label, not a secret. Include the caller, environment, and
issuance date so telemetry makes rotation state clear:

```text
kuali-prod-20260824-a
```

IDs must be unique and contain 1-64 ASCII letters, digits, periods,
underscores, or hyphens. Prefer dated IDs over labels such as `primary` and
`secondary`, which become ambiguous after several rotations.

## Calculate the stored hash

Read the token without echoing it, calculate its SHA-256 hash, and then remove
the plaintext shell variable:

```bash
read -r -s TOKEN
printf '\n'
printf '%s' "$TOKEN" | openssl dgst -sha256 -r | awk '{print $1}'
unset TOKEN
```

Paste the token at the hidden prompt. The 64-character result is the hash
supplied to KOI. Azure and the Function process receive the ID, hash, and
enabled state—not the plaintext token.

A credential slot uses this configuration shape:

```text
ApiKeys__Credentials__0__Id=kuali-prod-20260824-a
ApiKeys__Credentials__0__Sha256=<64-character-sha256-hash>
ApiKeys__Credentials__0__Enabled=true
```

The second active slot uses index `1`.

## Configure the caller

The caller sends the plaintext token in exactly one request header:

```http
Authorization: Bearer <token>
```

The credential ID is not sent by the caller. KOI identifies the matching ID
after comparing hashes and logs only that non-secret ID.

## Rotate a key

KOI must always have exactly two unique, structurally valid credential slots,
with at least one slot enabled.

1. Select the slot that is not used by the caller and disable it if necessary.
2. Generate a new independent token and a new dated ID.
3. Store the plaintext token in the approved password manager.
4. Replace the disabled slot's ID and hash, then enable the slot.
5. Verify the new token against an authenticated KOI endpoint.
6. Update Kuali Build to use the new plaintext token.
7. Confirm KOI telemetry reports successful requests using the new ID.
8. Disable the retired slot and verify Kuali continues to succeed.
9. Keep the retired slot configured and disabled. During the next rotation,
   replace that slot's ID and hash before enabling it.

If a plaintext token might have been disclosed, treat it as compromised and
disable that credential immediately. Do not wait for the normal rotation
observation period.

## Local-only keys

For development, `./scripts/create-local-env.sh` creates two independent
256-bit tokens and their matching SHA-256 hashes in a gitignored, mode-`600`
`.env`. The hashes use the same `ApiKeys__Credentials__*` configuration names
as Azure; the plaintext `KOI_API_KEY_*` values are available only for local
smoke tests and API clients. Never promote those local tokens to a deployed
environment.

The Function loads `.env` only when the Functions environment is Development.
This deliberately allows disposable plaintext local tokens to exist in the
local Function process configuration for a simple Visual Studio F5 workflow.
Azure deployments continue to receive only IDs, hashes, and enabled state.
