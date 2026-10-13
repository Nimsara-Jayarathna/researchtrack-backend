# Secrets and runtime configuration

Complete [local setup](local-setup.md) first. `config/env` is the canonical contract:
each component commits `.env.example`; actual local values belong in the adjacent,
gitignored `.env.local`. Root `.env` files are not the service startup entry point.

## Configuration ownership and loading

Local scripts load the selected service file, then load the shared JWT contract
for Auth, Project, GitHub, Jira, Meeting and Submission. Shared JWT values take
precedence over conflicting service-local values so issuer, audience and signing
key stay consistent. Gateway loads its own configuration only.

`appsettings.json` holds stable structure such as routes and logging; environment
variables supply runtime endpoints, credentials and configurable policy. ASP.NET
hierarchical keys use double underscores: `Jwt__Issuer` maps to `Jwt:Issuer`.
Stable roles are domain invariants; institutional domains, password policy,
synchronization intervals and storage limits are external configuration.

The shared file supplies `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey`; use at
least 32 UTF-8 bytes for the signing key and distinct keys per deployed environment.
Auth separately owns token lifetimes and cookie settings. Never publish the key
in frontend configuration or examples.

| Environment | Delivery |
| --- | --- |
| Local | `config/env/<component>/.env.local` |
| Test | GitHub `test` Environment bundles validated for VPS/Compose |
| Production | GitHub `production` Environment bundles validated for Azure |

Use the same keys with environment-specific values, not developer env files
copied into deployment artifacts. See [service configuration](service-configuration.md)
and [deployment contracts](../devops/configuration/README.md).

## Environment file syntax

The env loader deliberately treats env files as **data rather than executable shell scripts**.

Supported:

```text
KEY=value
KEY="value"
KEY='value'
# comments
```

Blank lines are also supported. Keys must be valid environment-variable names;
use no spaces around `=` and put comments on their own lines. The parser removes
one pair of surrounding quotes, preserves literal value contents, and does not
expand variables, run substitutions or strip inline comments. Keep multiline
private keys in supported Base64 or mounted-file form.

Do not use executable shell syntax such as:

```text
export KEY=value
KEY=$(command)
KEY=`command`
```

The scripts do not `source` the service `.env.local` files as arbitrary shell programs; they parse them as key/value configuration data.

This design also accepts Windows CRLF line endings when files are edited from Windows/WSL workflows.

ASP.NET's `__` convention is used for nested configuration:

```text
Registration__StudentEmailDomain
```

maps to:

```text
Registration:StudentEmailDomain
```

inside `IConfiguration` / `IOptions<T>`.

---

## Secret handling

Never commit or share publicly:

```text
config/env/**/.env.local
service database passwords
MySQL administrator password
private keys
JWT signing keys
GitHub/Jira OAuth secrets
GitHub webhook secrets
email/provider credentials
storage access/secret keys
```

Safe repository contracts:

```text
config/env/**/.env.example
```

The `.env.example` files contain placeholders, comments and safe defaults. Keep real credentials, confidential provider settings and deployed institutional policy in environment configuration rather than replacing template placeholders with production values.

Additional rules:

1. Give the database administrator password only to people who actually provision the DB server.
2. Normal application processes use service-specific DB accounts, never the MySQL administrator account.
3. Each service DB account receives access only to its own development/test databases.
4. Do not print secrets in scripts, exceptions, responses, or normal application logs.
5. Rotate credentials immediately if a secret enters Git history or another uncontrolled location.
6. Do not reuse development secrets in Test/Production.
7. Do not hard-code configurable business policy in C# or `appsettings.json` simply for convenience.
8. Strongly typed `Options` classes define the configuration contract; ENV supplies the actual runtime values.
9. Required configuration should fail fast during startup rather than silently falling back to insecure or misleading defaults.

### Why ASP.NET User Secrets are not used as the primary project model

ResearchTrack deliberately uses the same environment-key contract across local development, CI/CD, containers, and deployed hosting platforms. That avoids one configuration model for local development and a different one for deployment.

Local values are kept in gitignored service `.env.local` files; deployment values are injected through the deployment platform's secure environment/secrets capability.

---

## Optional User Secrets helpers

The supported startup path is service-owned environment files. Compatibility
helpers exist for `secrets-set.sh <service> <Configuration:Key>`,
`secrets-list.sh <service>`, `secrets-remove.sh <service> <Configuration:Key>`,
and `secrets-clear.sh <service>`. The setter prompts without echoing the value;
the list helper masks values. These do not replace environment configuration
required by the local scripts. See the [command reference](commands.md).
