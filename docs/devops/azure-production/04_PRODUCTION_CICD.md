# Production CI/CD Plan

## Test

`develop` remains unchanged.

## Production

`main` deploys Azure Production.

## Images

Keep GHCR.

Use immutable SHA/version tags. Container Apps always run an image pinned by
digest.

### Which services are built (source fingerprints)

Production does **not** decide from "what changed in this push". A change that
was not deployed in its own push (failed build, cancelled run, a Production
deploy from another branch, a push that ran an older workflow) would otherwise
never reach Production, because unselected apps keep their running image.

Instead `deploy/build/service-impact.py plan` computes, for every service, a
fingerprint of all files that feed its image at the deployed commit and tags
the image `src-<fingerprint>`:

| Change | Services rebuilt |
|---|---|
| file in a service project (any new controller/worker/folder) | that service |
| project referenced via `ProjectReference` (followed transitively, e.g. BuildingBlocks) | every service that references it |
| file a project includes from outside its directory (`Compile`/`Content`/`Import`) | that service |
| `Directory.Build.*`, `Directory.Packages.props`, `NuGet.config`, `global.json`, `.editorconfig` in a project directory or ancestor | services below it |
| `deploy/Dockerfile.service`, `deploy/image/**`, `.dockerignore`, `ResearchTrack.sln`, `tools/ResearchTrack.DbCheck`, image build workflow/model | all |
| docs, tests, `config/`, `scripts/`, other `deploy/` and `.github/` files, root `*.md` | none |
| anything else (unclassified, e.g. a new unreferenced `src/Contracts/`) | all (conservative) |

A service is built when no image exists for its current fingerprint, so
Production converges on the current source regardless of history. The deploy
step targets `src-<fingerprint>` for every service, resolves it to a digest,
and verifies the image's `io.researchtrack.service` /
`io.researchtrack.source-tag` labels; it never falls back to the image an app
already runs. `force_full_build=true` rebuilds all seven.

Every image carries `org.opencontainers.image.revision` (commit it was built
from) and `org.opencontainers.image.source`. The run summary lists, per
service: action, reason, source commit, digest, previous and new revision.

Test keeps per-push selection (`selection: diff`) with the same dependency
model, because Compose there always pulls the moving `:test` tag.

New projects need no workflow edits: they are picked up through
`ProjectReference`. Only a new *service* needs an entry in `SERVICES` in
`deploy/build/service-impact.py`.

## Azure login

Use OIDC.

Required permission:

```text
id-token: write
contents: read
```

No stored Azure password.

## Preflight

Before app deployment check:

- Resource Group,
- VM,
- Container Apps environment,
- expected applications,
- private network readiness.

Fail clearly if infrastructure is missing.

## Configuration

Read variable names from `.env.example` contracts.

Real production values come from GitHub Environment `production`.

Do not read or commit real `.env` files.

## Deployment

For changed services:

```text
build
→ GHCR
→ dbcheck
→ migration
→ new Container App revision
→ readiness
→ activate
```

Unchanged services (running image already the current-source digest, same
env/secrets/resources) are not redeployed. `PLAN_ONLY=true` on
`deploy/azure/scripts/deploy-container-apps.sh` prints the plan without changes.

## Rollback

Keep previous healthy revision until new revision passes health checks.

If new revision fails:
- disable it,
- retain previous revision,
- fail the workflow.

## VM updates

Use Azure Run Command.

Generate temporary runtime configuration from GitHub production secrets.

Do not expose secrets in logs.

## Final verification

Check:

- all seven services,
- public Nginx → Gateway,
- MySQL connectivity,
- Kafka produce/consume,
- Prometheus target state/rules,
- Grafana datasource/dashboards.

## Replicas

Start:

```text
min=1
max=1
```

Do not scale until statelessness/background jobs are reviewed.
