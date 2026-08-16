# Local configuration and secret management

## Policy

Never commit real secrets. `appsettings.json`, `appsettings.Development.json`, and `.env.example` must remain safe to publish.

### `.env.local`

`setup.sh` creates `.env.local` and generates random local-only database passwords. This file is ignored by Git and used by Bash scripts to construct local service/test connection strings. It is for local developer convenience only, not staging/production.

### ASP.NET User Secrets

Every Gateway/service project contains a unique `UserSecretsId`. Store future application development secrets such as JWT signing keys, OAuth client secrets, and mail/API credentials using ASP.NET User Secrets.

From repository root:

```bash
./scripts/secrets-set.sh auth "Jwt:SigningKey"
./scripts/secrets-list.sh auth
./scripts/secrets-remove.sh auth "Jwt:SigningKey"
```

User Secrets are stored outside the repository in the developer profile. They are not encrypted and must not be treated as a production secret vault.

### Production/staging

Production/staging secret management is intentionally deferred with the deployment architecture. Use a controlled deployment secret mechanism (for example a cloud secret vault/environment secret provider); never reuse local development secrets.

## Incident rule

If a secret is committed to Git:

1. Rotate/revoke it immediately.
2. Remove it from current source.
3. Treat Git history/caches as potentially containing the old value.
4. Document the incident if required by the team/course process.
