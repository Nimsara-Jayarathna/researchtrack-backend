# Jira stateless OAuth token protection fix

## Problem

Jira OAuth access/refresh tokens were stored in MySQL after being protected with ASP.NET Core Data Protection. The Data Protection key ring was not explicitly externalized, so a container replacement could leave the database intact while losing the key that encrypted the stored Jira credentials. The next synchronization then failed with `The key {...} was not found in the key ring`.

## Fix

New Jira credentials are protected with AES-256-GCM using a stable, environment-specific secret:

```text
Jira__TokenEncryptionKey
```

Generate the value once per environment:

```bash
openssl rand -base64 32
```

Keep the same value across normal deployments of that environment. Test and Production must use different values. Do not commit real keys to source control.

No Jira-specific Docker volume is required. Jira containers remain disposable/stateless; durable state is the database plus externally supplied deployment secrets.

## Legacy migration

The token protector still understands the old ASP.NET Data Protection ciphertext during migration. New writes always use the `rtj:v2:` AES-GCM format.

If the old Data Protection key ring is still available, existing credentials can continue to be read until token refresh rewrites them using the new format. If the old key ring has already been lost, that credential cannot be recovered cryptographically and the Jira connection must be authorized again once.

Undecryptable credentials are surfaced as `INVALID_AUTH`; reconciliation and webhook-registration refresh skip those connections instead of repeatedly creating doomed background retries.

## Test deployment

Add to the Test `jira.env`:

```text
Jira__TokenEncryptionKey=<TEST_BASE64_32_BYTE_KEY>
```

Deploy the Jira service and reconnect any Test Jira connections that already fail with a missing Data Protection key.

## Production deployment

Before deploying, add a separately generated Production key:

```text
Jira__TokenEncryptionKey=<PRODUCTION_BASE64_32_BYTE_KEY>
```

Do not reuse the Test key.

If Production still has its legacy Data Protection keys, keep them available for the first migration deployment so legacy OAuth records can still be read. After credentials rotate/reconnect into `rtj:v2:` format, runtime token decryption no longer depends on container-local Data Protection state.

## Verification

1. Connect Jira and complete a successful synchronization.
2. Recreate only the Jira container.
3. Keep the same DB and `Jira__TokenEncryptionKey`.
4. Trigger synchronization and verify it succeeds without reauthorization.
5. Trigger/webhook a Jira change and verify the UI receives the new synchronized state.
6. Repeat another container replacement to prove the service is stateless.
