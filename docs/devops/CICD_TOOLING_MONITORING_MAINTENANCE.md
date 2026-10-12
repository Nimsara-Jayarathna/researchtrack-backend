# TASK 12 — CI/CD tooling and monitoring image maintenance

Date: 2026-10-12. Branch: `integration/main-kafka-production-20261011`.
Base HEAD: `1810f09535d6a6d00947bcf84d18fde473e40c39`; existing TASK 02–11 edits preserved.

## Changes prepared locally

- Update **only** `actions/setup-dotnet` in Backend CI, isolated Kafka CI and mutation
  testing to verified `v6.0.0`, pinned at
  `a98b56852c35b8e3190ac28c8c2271da59106c68`. Its immutable source manifest declares
  Node 24 and accepts every existing configured input. SDK selection, caching inputs
  and permissions are unchanged. Actual action execution on GitHub is **NOT EXECUTED**.
- Group five sets of repeated environment/summary redirects into four workflow shell
  bodies. This removes all five SC2129 findings while preserving output content, append
  behavior, validation failures and recovery paths. Three new regression methods execute
  the actual shell bodies with synthetic inputs and present/missing k6 reports.
- Leave monitoring images, storage, resources and configuration unchanged. Actual deployed
  versions/digests are **NOT VERIFIED**. Read-only identity capture and a conditional
  immutable-pinning plan appear below; no monitoring upgrade is proposed as a ready change.

All 14 workflows were inspected. A before/after parsed YAML audit confirmed identical
triggers, job dependencies, permissions, environments, concurrency, action inputs and
all other step properties. Only three `.NET` references and four shell bodies differ.
The audit used the system Ruby YAML parser solely for local comparison; no Ruby dependency
was added to CI. No workflow dispatch, commit, push, deployment or production restart.

## Action runtime and compatibility review

At task start, 37 external action uses referred to nine mutable major-version tags.
All nine upstream **current-reference manifests** declared Node 20. After this task,
three uses are pinned Node 24 `.NET` setup and 34 external references remain mutable.
Repository-local reusable workflow paths are not third-party major tags; their source
is the caller's validated commit.

The historical Node warnings do not prove those actions failed. GitHub now runs actions
declaring Node 20 on Node 24; use maintained action code rather than inferring compatibility
from the old runtime declaration alone. See [GitHub's Node 20 removal announcement](https://github.blog/changelog/2026-09-23-node-20-is-no-longer-available-in-github-actions/).

For the selected `.NET` update, the [v6 README](https://github.com/actions/setup-dotnet/blob/v6.0.0/README.md)
documents Node 24's minimum runner version, **2.327.1**, and v6's ESM migration without
input/output/behavior changes from v5. Its [pinned manifest](https://github.com/actions/setup-dotnet/blob/a98b56852c35b8e3190ac28c8c2271da59106c68/action.yml)
was fetched and compared with the actual workflow inputs. The repository uses GitHub-hosted
`ubuntu-latest`; no self-hosted runner is declared. Runner execution/version and cache
restore/save still require the next separately authorized unprivileged CI run.

`global.json` still selects .NET 10.0.300 with compatible feature-band roll-forward.
Backend CI and mutation keep their `global-json-file`, `cache` and dependency paths;
isolated Kafka keeps `dotnet-version: '10.0.x'`. The local Release build used SDK 10.0.400.
The action's internal Node runtime is independent of application SDK and the separate
`setup-node` application/tool version, which remains Node 22.

Candidate versions below were checked on the date above using upstream releases, tag
commit identities and `action.yml`. They are **review candidates**, not all tested upgrades.
Full commit/manifests and release notes are retained in the local evidence directory.

| Current reference at task start | Warning/risk | Proposed change | Verification | Deployment impact | Rollback |
|---|---|---|---|---|---|
| `actions/setup-dotnet@v4` (3 uses) | Node 20 declaration, floating tag | **APPLIED LOCALLY:** pinned v6.0.0 SHA above | Upstream inputs/runtime verified; YAML preservation, offline regressions and Release build PASS; actual installation/cache action NOT EXECUTED | Unprivileged SDK setup only; affects required CI before release gates, not Azure credentials | Revert only these three reference lines to reviewed previous reference; rerun CI before release |
| `actions/checkout@v4` (14) | Node 20 declaration, floating tag | Deferred candidate [v7.0.1](https://github.com/actions/checkout/releases/tag/v7.0.1); review full-SHA pin | Manifest Node 24; actual checkout NOT EXECUTED | Review privileged-event source safeguards, credential persistence/location, sparse checkout and full-history image planning | Restore reviewed prior reference and checkout inputs; do not bypass SHA eligibility |
| `actions/setup-node@v4` (1) | Node 20 action runtime, floating tag | Deferred [v7.1.0](https://github.com/actions/setup-node/releases/tag/v7.1.0) | Manifest Node 24; execution NOT EXECUTED | Review automatic package-manager caching/defaults; keep requested Node 22 and offline k6-source validation behavior | Restore prior action; preserve `node-version` and cache policy |
| `actions/setup-python@v5` (1) | Node 20 declaration, floating tag | Deferred [v7.0.0](https://github.com/actions/setup-python/releases/tag/v7.0.0) | Manifest Node 24; mutation workflow NOT EXECUTED | Review v6/v7 changes and `python-version: '3.x'`; avoid changing mutation tooling/interpreter policy in this small update | Restore prior action/input; rerun unprivileged mutation tooling checks |
| `actions/upload-artifact@v4` (8) | Node 20 declaration, floating tag | Deferred [v7.0.2](https://github.com/actions/upload-artifact/releases/tag/v7.0.2) | Manifest Node 24; upload/download semantics NOT EXECUTED | Recovery snapshots depend on successful uploads; verify names, paths, retention, hidden-file handling, failure behavior and backend compatibility | Restore prior uploader; retain already-uploaded artifacts and exact run/attempt identities |
| `azure/login@v2` (5) | Node 20 declaration, floating tag | Deferred [v3.1.0](https://github.com/Azure/login/releases/tag/v3.1.0) | Manifest Node 24; no OIDC login executed | Preserve client/tenant/subscription inputs, environment-scoped identity, audience, cleanup and federation subject; requires separately approved real OIDC compatibility check | Restore prior action; do not change federation, identity or secrets |
| `docker/login-action@v3` (3) | Node 20 declaration, floating tag | Deferred [v4.6.0](https://github.com/docker/login-action/releases/tag/v4.6.0) | Manifest Node 24; registry login NOT EXECUTED | Verify registry/username/password and cleanup behavior without printing credentials | Restore prior action with identical inputs |
| `docker/build-push-action@v6` (1) | Node 20 declaration, floating tag | Deferred [v7.4.0](https://github.com/docker/build-push-action/releases/tag/v7.4.0) | Manifest Node 24; publish/provenance execution NOT EXECUTED | Validate immutable image tags/digests, OCI fingerprint labels, cache/provenance defaults and deployment image selection | Restore prior action; use recorded immutable application images, never arbitrary latest |
| `docker/setup-buildx-action@v3` (1) | Node 20 declaration, floating tag | Deferred [v4.4.1](https://github.com/docker/setup-buildx-action/releases/tag/v4.4.1) | Manifest Node 24; builder creation NOT EXECUTED | Builder/BuildKit compatibility and tool-version defaults need a disposable build check | Restore prior action/builder policy; no production-image rollback implied |

Further action pinning should resolve a reviewed upstream release to its full commit,
retain a readable version comment and have a deliberate update process. Do not substitute
unverified commit strings or turn a version-maintenance task into changed permissions or
new permanent branch restrictions.

## Complete workflow inventory and permissions

External names below omit repository prefixes for readability; `.NET` means the pinned
`setup-dotnet` v6.0.0 above. This inventory covers current working-tree files, not old
branches or live GitHub settings.

| Workflow file | External actions after local change | Permission assessment |
|---|---|---|
| `backend-ci.yml` | checkout v4, setup-node v4, .NET, upload-artifact v4 | Contents read; Kafka caller contents read; no Azure credential scope |
| `kafka-integration-tests.yml` | checkout v4, .NET, upload-artifact v4 | Contents read only |
| `backend-mutation.yml` | checkout v4, .NET, setup-python v5, upload-artifact v4 | Contents read only; default checkout credential persistence remains a future review item |
| `backend-release-validation.yml` | checkout v4 | Contents read; required CI is unprivileged |
| `backend-build-images.yml` | checkout v4, Docker login v3, setup-buildx v3, build-push v6 | Detection narrows packages to read; build packages write is needed to publish |
| `backend-deploy-production.yml` | checkout v4 for summary | Caller delegates packages/OIDC only where needed; summary explicitly contents/actions read; smoke has contents/OIDC |
| `backend-deploy-azure-production.yml` | checkout v4, upload-artifact v4, Azure login v2, Docker login v3 | Validation contents read; build packages write; deploy contents/packages read plus OIDC, protected production environment |
| `backend-deploy-test.yml` | Repository-local reusable only | Contents read/packages write delegated to existing Test build path |
| `backend-deploy-reusable.yml` | checkout v4 | Target validation has empty permissions; build packages write; Test deploy packages read |
| `azure-infrastructure.yml` | checkout v4, Azure login v2 | Eligibility/validation contents read; only production deploy job has OIDC write |
| `azure-runtime-power.yml` | checkout v4, Azure login v2 | Contents read + OIDC for protected production operation |
| `azure-runtime-session.yml` | checkout v4, Azure login v2, upload-artifact v4 | Contents read + OIDC for protected production operation |
| `azure-performance-k6.yml` | Repository-local reusable only | Contents read + OIDC delegated to protected operation |
| `azure-performance-k6-reusable.yml` | checkout v4, Azure login v2, upload-artifact v4 | Contents read + OIDC for protected production performance session |

No confirmed excessive permission requiring a change was found in this repository-side
review. Caller maximum scopes support their reusable jobs; explicit narrower job scopes
remain intact. OIDC permissions are not granted to required CI. Source authorization,
same-SHA checks, production Environment references, runtime restoration and shared lock
remain covered by existing offline tests. Live Environment protection, Azure federation
and repository settings are not revalidated or modified by TASK 12.

## Tooling findings

| Current reference/configuration | Warning/risk | Proposed change | Verification | Deployment impact | Rollback |
|---|---|---|---|---|---|
| actionlint 1.7.12 | Five `concurrency.queue` schema findings | Retain valid queue; narrowly qualify this known diagnostic until the tool supports it | Latest upstream release also 1.7.12; raw FAIL, qualified PASS | None; removing queue would change deployment serialization/pending-run behavior | Preserve original non-cancelling lock/queue |
| Repeated workflow redirects | Five SC2129 style findings | **APPLIED:** group redirects in runtime-session, Azure app deployment and k6 reusable bodies | All SC2129 findings removed; valid/invalid config and present/missing-report tests PASS | Same env paths and summaries; no changed credentials or restoration policy | Revert only grouped redirect blocks; retain previous task safeguards |
| ShellCheck 0.11.0 | Existing indirect-trap SC2329/caller-env SC2153 informational findings in standalone scripts | Do not rewrite working signal handlers or validated env variables to satisfy static analysis | Workflow embedded-shell checks have no remaining ShellCheck finding; standalone warning/error severity PASS | None | No code change needed |
| `ubuntu-latest` | Mutable hosted OS/toolchain baseline | Review actual runner OS/version on the next authorized CI run; OS pin is a separate choice | Runner label preserved; real runner metadata NOT VERIFIED | Future libc/native dependencies/Docker CLI may change | Use an independently reviewed runner baseline; do not disable CI to pass |

GitHub supports `queue: max`; this is a local actionlint schema limitation, not proof of
unsupported server syntax. See [GitHub concurrency](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency).
Upstream tooling identities were checked at [actionlint releases](https://github.com/rhysd/actionlint/releases/tag/v1.7.12)
and [ShellCheck releases](https://github.com/koalaman/shellcheck/releases/tag/v0.11.0).
No global Homebrew/tool installation was changed. Raw lint failures remain recorded;
qualified PASS is not presented as unfiltered PASS.

## Monitoring image and persistence review

All references below are **configured**, not proof of what is deployed.

| Current reference | Warning/risk | Proposed change | Verification | Deployment impact | Rollback |
|---|---|---|---|---|---|
| `prom/prometheus:latest` in Azure VM Compose | Mutable; pulling/recreating can change binary and TSDB behavior | **DEFERRED:** pin exact captured, compatible deployed version + registry digest after identity/storage review | Local rules/config/queries PASS under test promtool; actual production image/version/digest NOT VERIFIED | Pinning string can cause Compose recreation even with identical image bytes; TSDB, flags, scrape formats, retention and resource limits need review | Reviewed prior digest and compatible persistent state; never blind downgrade/delete TSDB |
| `grafana/grafana:latest` | Mutable; database/plugin migrations and provisioning compatibility | **DEFERRED:** capture current identity/version, verify persistence/backup method, then pin the same reviewed image | Dashboard/provisioning static validation PASS; actual Grafana version/database engine/digest NOT VERIFIED | Possible restart, database/plugin changes if content differs; UID/GID and mounts must remain compatible | Reviewed prior image plus compatible database/plugin/config backup, not image-only downgrade |
| `danielqsj/kafka-exporter:v1.10.0@sha256:346a53c2489a3530790d01f7b96632496d0d58926662f4c35bb65f569db2ab42` | Already immutable; configured pin does not prove live identity | Preserve existing pin; compare actual image/registry identity before considering any upgrade | Existing pinned-exporter Compose/metric-family checks PASS; live digest NOT VERIFIED | No persistent volume, no host port or secret/socket mount; changing exporter may alter metric families/labels/scrape behavior | Restore approved previous digest/flags; no broker/topic/offset changes |
| `prom/prometheus:v3.15.0` in local monitoring validator/isolated fixture | Version tag is also mutable, and not the deployed server identity | Future pin of the verified test image is a separate supply-chain step | This task ran existing validation using that test image; no registry digest selected | Test-only; do not use it as justification to upgrade production Prometheus to 3.15.0 | Restore reviewed validator image/binary; preserve production version |

Configured storage is `/data/researchtrack/prometheus:/prometheus` and
`/data/researchtrack/grafana:/var/lib/grafana`, with configuration/provisioning mounts
read-only. Both monitoring services retain their 512 MiB/0.25 CPU limits and loopback
9090/3000 bindings. Exporter retains its 96 MiB/0.10 CPU budget and private Kafka
inspection settings. Compose, retention settings, mounts and permissions were not edited.

Existing `reconcile-monitoring.sh` deliberately uses the currently deployed Prometheus/
Grafana **local image IDs** with `--pull never`, and only pulls the pinned exporter.
Its mocked reconciliation suite passed. That safeguard does not make `latest` immutable
in other future Compose operations, and a local image ID is not a registry manifest digest.
Nginx `1.27-alpine`, MySQL `8.4` and Kafka `3.9.1` also remain tag-only references; no
unrelated infrastructure version or persistence change was made here.

## Read-only operator identity capture — commands prepared, NOT EXECUTED

Use an existing authorized session on the **already-running** production infrastructure
VM. Do not wake, pull, recreate or restart anything for this capture. Record timestamp,
VM identity and run/release context with the following selected fields; do not print
`docker inspect` in full, `Config.Env`, resolved Compose configuration or runtime env files.

```bash
set -euo pipefail
for service in prometheus grafana kafka-exporter; do
  cid="$(/opt/researchtrack/scripts/compose.sh ps -q "$service")"
  if [[ -z "$cid" || "$cid" == *$'\n'* ]]; then
    echo "$service: NOT VERIFIED (expected one existing container)"
    continue
  fi
  echo "Service: $service"
  docker inspect --type container "$cid" \
    --format 'container={{.Id}} running={{.State.Running}} configured={{.Config.Image}} image_id={{.Image}}'
  image_id="$(docker inspect --type container "$cid" --format '{{.Image}}')"
  docker image inspect "$image_id" \
    --format 'RepoDigests={{json .RepoDigests}} platform={{.Os}}/{{.Architecture}} user={{.Config.User}}'
  docker inspect --type container "$cid" --format '{{json .Mounts}}' | \
    jq '[.[] | {Type,Source,Destination,RW}]'
done

# Build/version metadata only; these HTTP GETs do not change configuration.
curl -fsS --max-time 5 http://127.0.0.1:9090/api/v1/status/buildinfo
curl -fsS --max-time 5 http://127.0.0.1:3000/api/health

# Existing exporter binary --version, only when its container is running.
cid="$(/opt/researchtrack/scripts/compose.sh ps -q kafka-exporter)"
if [[ -n "$cid" && "$cid" != *$'\n'* && \
      "$(docker inspect "$cid" --format '{{.State.Running}}')" == true ]]; then
  docker exec "$cid" /bin/kafka_exporter --version
fi
```

The exporter binary path is documented by the [v1.10.0 Dockerfile](https://github.com/danielqsj/kafka_exporter/blob/v1.10.0/Dockerfile).
Do not start stopped containers to obtain a version. If an HTTP/binary check fails,
record NOT VERIFIED; do not infer version from `latest`, local tool versions, dates
or dashboard appearance. Where needed, inspect ownership/mode of the **actual captured**
data directories using read-only `stat`; do not run recursive `chown`/`chmod`.

Retain container ID, configured reference, content/config image ID, RepoDigests, platform,
binary/build version and actual mounts as separate fields. A `sha256` local image ID is
not interchangeable with a registry manifest/index digest. If RepoDigests is empty,
record the registry identity as missing. Recover it from trusted registry/provenance
evidence and verify the platform manifest/config digest matches the running image ID;
do not resolve today's `latest` and assume it is the already-running image.

Also record Actions runner version/OS, resolved action commits and setup/cache results
from the next separately authorized **unprivileged CI** run. No privileged OIDC/build/
deployment smoke should be dispatched merely to remove tooling warnings.

## Conditional immutable-pinning and rollback plan

1. Obtain the live identity evidence above. Review the exact upstream version's release
   notes, configuration/flags, storage format, UID/GID, plugins, metrics and provisioning
   against the current stack. Choose pinning of the **same verified deployed content**
   first; treat a numeric version upgrade as a separate change.
2. Confirm storage type and a recoverable, consistent backup strategy without modifying
   live state in this task. Grafana's database type is not inferred from its default.
   SQLite file-copy consistency can require a separately authorized stop; MySQL/Postgres
   need the matching backup method. Back up configuration/plugins too. See
   [Grafana backup guidance](https://grafana.com/docs/grafana/latest/administration/back-up-grafana/).
3. Evaluate Prometheus TSDB compatibility and rule/scrape behavior on disposable copies.
   Prometheus 3 introduces query/scrape changes and downgrade constraints; a prior image
   is not automatically safe for data written by a newer server. See
   [Prometheus migration guidance](https://prometheus.io/docs/prometheus/latest/migration/).
   No production TSDB deletion, restoration or permission repair is authorized here.
4. On a review workstation, verify the captured registry digest/platform against the
   running image/config identity. Proposed future references have the form
   `prom/prometheus:<captured-version>@sha256:<verified-registry-digest>` and
   `grafana/grafana:<captured-version>@sha256:<verified-registry-digest>`. These are
   **placeholders**, not values to paste into Compose now. Digests pin content rather
   than floating tags; see [Docker image digest documentation](https://docs.docker.com/reference/cli/docker/image/pull/#pull-an-image-by-digest-immutable-identifier).
5. Test the reviewed binaries/configuration on disposable storage, including Prometheus
   rules/alert firing and recovery, exporter families, Grafana dashboard/data-source/plugin
   compatibility, backup/restore, data ownership and the existing resource limits. Local
   checks with one promtool version alone cannot verify live Grafana/database upgrades.
6. After review and separate deployment authorization, use the existing monitoring-only
   maintenance path/window. Account for possible container recreation and scrape/UI gaps
   even when pinning identical bytes. Preserve data paths, TLS, exporter isolation, Kafka
   cluster identity and unrelated containers. This task executes none of those operations.
7. If verification fails, stop further rollout and retain evidence. Restore only a reviewed
   previous image/config and **compatible** persistent state through the approved recovery
   process. Do not assume changing an image back reverses database migrations. Never clear
   volumes, Kafka groups or outbox/inbox data to force a successful rollback.

## Actual validation results and limits

| Check | Actual result |
|---|---|
| Official action releases/tag commits/manifests | **PASS**, nine current references and nine candidates examined read-only; all used `.NET` inputs supported by the pinned Node 24 manifest |
| Before/after parsed YAML audit | **PASS**, all 14 workflow configurations; only intended three references/four shell bodies changed |
| `test_workflow_release_gates.py` | **PASS**, 34 unittest methods, including three new output/failure/report regressions |
| `test_release_policy.py` | **PASS**, 25 methods; trust/SHA gates, skipped smoke and failure reporting retained |
| `test_monitoring_reconciliation.py` | **PASS**, 12 methods; mocked monitoring scope/image preservation/safety |
| `ResearchTrack.sln` Release build, `--no-restore` | **PASS**, 0 warnings, 0 errors using local SDK 10.0.400 |
| `scripts/test-kafka-observability.sh` | **PASS**, 159 assertions/checks, 33 Prometheus scenarios, 55 dashboard expressions; synthetic config only |
| `tests/performance/validate.sh` | **PASS**, shell/JS source and workflow contracts |
| Raw actionlint 1.7.12 | **FAIL**, only five existing unsupported queue-schema findings remain; all five SC2129 findings removed |
| actionlint with only queue-schema exclusion | **PASS**, no other findings; no SC2129 exclusion needed |
| Source-aware standalone ShellCheck, warning/error severity | **PASS**; existing informational findings retained separately |
| `git diff --check` | **PASS** |
| Real setup-dotnet action/cache, other candidate Actions, Kafka suite/mutation execution | **NOT EXECUTED**, no workflow dispatch; local build is not action-runtime installation proof |
| Production image/version capture, Grafana migration, TSDB upgrade/downgrade, live monitoring | **NOT EXECUTED / NOT VERIFIED** |

Local ignored evidence: `artifacts/tooling-maintenance-task12/`. Includes upstream manifests,
release metadata, input-compatibility JSON, parsed YAML snapshots/preservation audit,
regression/build/observability logs, raw/qualified lint and final result/source hashes.
The fixed validation image is not asserted to be the production Prometheus version.

Reproduce offline checks from this worktree:

```bash
python3 -m unittest discover -s deploy/azure/validation -p test_workflow_release_gates.py -v
python3 -m unittest discover -s deploy/azure/validation -p test_release_policy.py -v
python3 -m unittest discover -s deploy/azure/validation -p test_monitoring_reconciliation.py -v
dotnet build ResearchTrack.sln -c Release --no-restore
./scripts/test-kafka-observability.sh
./tests/performance/validate.sh
actionlint
# Separate qualified check; raw failures must still be reported:
actionlint -ignore 'unexpected key "queue"'
```

## Exact TASK 12 files changed

- `.github/workflows/backend-ci.yml`, `backend-mutation.yml`, `kafka-integration-tests.yml`:
  the three pinned `.NET` setup references.
- `.github/workflows/azure-runtime-session.yml`, `backend-deploy-azure-production.yml`,
  `azure-performance-k6-reusable.yml`: grouped redirects only for this task.
- `deploy/azure/validation/test_workflow_release_gates.py`: three output regressions.
- `docs/devops/CICD_TOOLING_MONITORING_MAINTENANCE.md`: new report/table/operator plan.

All eight changes are local. Prior authorization/source-validation changes remain intact;
no GitHub/Azure settings, identities, federated credentials, monitoring image references,
production resources or persistent data were changed. No commit, staging, push or deployment.
