# Main / Kafka production integration review

Prepared locally on 2026-10-11. Nothing was committed, pushed, deployed or changed in Azure or GitHub settings/secrets. Existing branches and production data are untouched.

## Review location and merge state

- Worktree: `/Users/dev/Desktop/z/researchtrack-backend-integration-main-kafka`.
- New branch: `integration/main-kafka-production-20261011`, created from fetched `origin/main`.
- Main: `141181575e7b7fdda75e31356bfe9a980deadb7c`.
- Kafka source: `911cd5a40bfca0c9c5a8fb7151390e2a8c206e2b` on `origin/devops/sprint4-kafka-infrastructure-verification`.
- Merge base: `54dcbae67793a2103b6ef78502401bf4ab04b336`.
- Divergence: **32 main-only commits, nine feature-only commits**. Full commit lists are in local `artifacts/main-kafka-integration/source-preservation.json`.
- Merge used `--no-autostash --no-commit --no-ff`. There were no textual conflicts. The merge remains in progress, HEAD remains main, and MERGE_HEAD is the Kafka source. The automatic merge and resolved corrections are staged for review; no merge commit exists. The new branch has no upstream.
- Original checkout `/Users/dev/Desktop/z/researchtrack-backend` stays clean on the original Kafka feature branch.

Review without publishing:

```bash
cd /Users/dev/Desktop/z/researchtrack-backend-integration-main-kafka
git status
git diff --cached --stat
git diff --cached
git log --left-right --oneline origin/main...origin/devops/sprint4-kafka-infrastructure-verification
```

Committing, publishing, merging a PR and deploying require the owner's subsequent decision. Do not run `git merge --continue` during local review.

## Cause and resulting behavior

Fetched main lacks `src/BuildingBlocks/Kafka/KafkaMetrics.cs` and the Kafka application integration. Its independently deploying workflow can replace feature-based GitHub/Jira images with main-based images lacking those features. This is a source and deployment-policy conflict. No live image digest or Azure runtime state was inspected.

The integration brings Kafka into current main and makes main the only automatic production source in the reviewed workflow definitions. Both production entrypoints guard manual deployment to `refs/heads/main`. Azure reusable build/deploy jobs enforce the same guard. The legacy reusable workflow guards production while preserving develop/Test deployment.

Existing secret names/materialization, OIDC permissions/login, `environment: production`, immutable source-image selection, seven-service rollout, database checks/migrations, What-If refusal, monitoring-only mode, rollback, runtime restoration and shared concurrency are preserved byte-for-byte after normalizing only the new branch filters/guards. Regression tests verify that preservation.

## Semantic conflict resolutions

Six files changed on both sides:

| File | Resolution |
|---|---|
| `.github/workflows/backend-ci.yml` | Keep main coverage gates/artifacts and Kafka/monitoring checks; add production-policy checks. |
| `.gitignore` | Keep both sets of ignores. |
| `Directory.Packages.props` | Remove four duplicate declarations; keep main transitive pinning, mutation xUnit v2 and Kafka packages. Versions unchanged. |
| `src/Services/ResearchTrack.JiraService/Features/JiraWebhookService.cs` | Keep main JSON type/bearer-expiration hardening together with atomic Kafka outbox creation. |
| `tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj` | Deduplicate four references; preserve main mock/SQLite infrastructure and Kafka fixture links. |
| `tests/ResearchTrack.JiraService.Tests/ResearchTrack.JiraService.Tests.csproj` | Deduplicate two references; preserve both sets of tests/fixture links. |

All **167 files changed only on main** match fetched main byte-for-byte. All **85 feature-only changed files outside the shared files and intentional corrections** match the feature source byte-for-byte. Retained work includes unit/mock coverage, GitHub reconciliation policy extraction, submission hardening, all seven mutation harnesses, Stryker settings, reporting tools/docs and final mutation-policy fallback. No quality gate was lowered.

Kafka durable webhook references, transactional outboxes, deduplication, commit-after-handoff, producers/consumers, topic contracts, TLS CA validation, metrics, exporter, dashboards, alerts, VM integration and isolated fixtures are retained. Disabled/empty Kafka CA omission remains for both apps and migration jobs. Provided public certificates retain secretRefs; private-key rejection and TLS validation remain.

Additional corrections beyond the automatic merge are in the four deployment workflows, Backend CI, the package manifest/two test project files, `test_monitoring_reconciliation.py`, new `test_production_branch_policy.py`, and current branch-policy docs (`DECISIONS.md`, `RUNBOOK.md`, `KAFKA_INTEGRATION_TESTING.md`, index and this report). Historical task reports retain their dated feature-branch context.

## Executed validation

Logs/evidence are local ignored files under `artifacts/main-kafka-integration/`; coverage is under `artifacts/coverage/`.

| Check | Actual result |
|---|---|
| .NET restore | PASS, all 18 solution projects. |
| Release build, no restore | PASS, zero warnings/errors. |
| Existing coverage script, CI floors 65% line / 50% branch | PASS: **822 tests**, zero failures, **three Kafka tests skipped**. Business coverage **78.3% / 64.4%**; all-source **66.7% / 51.5%**. Filter `Category!=DatabaseIntegration`. |
| Seven mutation-harness projects, Release tests | PASS: **541 tests**, zero failures/skips. Harness tests, not a Stryker campaign. |
| Production branch-policy regressions | PASS: seven tests, including security/deployment-body preservation. |
| Kafka topic/configuration/CA/renderer regressions | PASS: 21 tests. |
| Azure environment validation / service and migration-job composition | PASS: 22 / ten synthetic cases. |
| Monitoring-only VM reconciliation | PASS: ten mocked tests. |
| Native Prometheus 3.15.0 observability checks | PASS: 158 configuration assertions, 30 alert scenarios, 55 dashboard expressions parsed. |
| Kafka operational safety / smoke / network probe | PASS: 43 / nine / 12 offline cases. Broker/Azure operations stubbed. |
| Runtime power / readiness / preflight | PASS: 11 / four / five mocked cases. |
| Private-IP check / image selection and rollback | PASS: ten / 11 mocked cases. |
| Shared JWT wiring | PASS: Test/local/Azure Production. |
| Native Bicep 0.48.1 compilation | PASS: `deploy/azure/main.bicep` and `deploy/azure/modules/container-app.bicep`, no diagnostics. |
| Shell syntax | PASS: 66 scripts. |
| EF model snapshots, both services | PASS: no pending model changes. |
| Offline migration listing / forward SQL | PASS: 12 GitHub / six Jira migration IDs; additive SQL reviewed below. Applied production status not queried. |
| Source/package preservation | PASS: 167 main-only files intact, prior migrations unchanged, no duplicate package references. |
| Actionlint 1.7.12, all workflows | Raw run FAILS with exactly the same ten diagnostics as main: five unsupported `queue` keys and five existing SC2129 style diagnostics. PASS excluding only those diagnostics; no new diagnostics. |
| Staged / working-tree whitespace | PASS: `git diff --cached --check` / `git diff --check`. |

Commands: `dotnet restore ResearchTrack.sln --verbosity minimal`, `dotnet build ResearchTrack.sln -c Release --no-restore --verbosity minimal`, `scripts/coverage.sh --no-build` with actual CI floors, `dotnet test` for each `.MutationTests` project, Python validation suites, and all relevant `deploy/azure/validation/test-*.sh` suites. The unchanged coverage script ran with a temporary `python` alias to installed Python 3. Each mutation harness has a TRX result. Native promtool was supplied through `PROMTOOL`; Docker CLI Compose parsing does not need a daemon.

Qualified lint command: `actionlint -no-color -ignore 'unexpected key "queue" for "concurrency" section' -ignore 'SC2129:'`. The Bicep compiler was downloaded to a temporary directory from the official Azure/bicep v0.48.1 release and verified against its published SHA-256 `62cd5958c62fa1b738e9b69f24799fabddb90ed83fdb7d6d2563dde376ce479d`. Builds wrote only ignored local JSON artifacts; no Azure call was made.

Current [GitHub concurrency documentation](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency) supports `queue: max`; it remains unchanged. Raw lint failure is recorded, not represented as an unqualified pass.

### Not executed

- `scripts/test-kafka-integration.sh` was invoked and exited **2: Docker daemon unavailable**, before creating containers, topics, databases or volumes. Three real TLS Kafka/MySQL tests and live fixture monitoring checks did not run. Run this suite on a Docker-capable machine before production approval.
- DatabaseIntegration-category tests and actual MySQL migration application: no database service available. Production migration history unknown.
- Full Stryker analysis: not run; all seven harnesses' ordinary tests passed.
- Azure CLI What-If and live OIDC/RBAC/network/deployment verification: Azure CLI unavailable. Both Bicep templates were compiled offline with the native compiler.
- Service-impact synthetic-repository suite: not run because it creates git commits, forbidden for this task. Its source is unchanged; Kafka configuration tests exercised current-repository dependency selection.
- No workflow dispatch, Docker application-image build/publish, or live webhook/Grafana/alert transition verification.

## Migration compatibility

Main adds no migrations relative to the merge base. All prior main migrations and both feature migration/designer pairs remain byte-identical. IDs are unique within each DbContext. Identical ID `20261010151156_AddKafkaWebhookMessaging` across GitHub/Jira is valid because contexts/databases have separate histories.

Forward SQL was generated offline using synthetic design-time factories from `20260923043000_AddGitHubSyncRevision` / `20260923043100_AddJiraSyncRevision`. Each adds only `kafka_inbox_receipts`, `kafka_outbox_messages`, three indexes and migration-history insertion. No DROP, DELETE, TRUNCATE, existing-table ALTER or UPDATE appears. Evidence: `artifacts/main-kafka-integration/migrations/`.

The additive schema permits older applications to leave new tables unused. Unit/configuration tests cover Kafka-disabled legacy processing and enabled durable handoff. These support code/schema backward compatibility; actual MySQL application and production rollout remain unverified. Preserve approved v1 topics/groups and TLS. Never downgrade migrations or clear durable queues as an image-rollback shortcut.

## Outstanding release boundaries

Read-only GitHub inspection returned `deployment_branch_policy: null` and `protection_rules: []` for production. No branch restriction or reviewer gate was reported; the YAML environment reference does not create one. No settings changed.

The old feature branch still contains its old production workflow. GitHub uses the pushed ref's workflow; updating main cannot disable old feature copies. Before release an owner must restrict production Environment deployment to exact **Branch main**, preserve secrets/OIDC/protection and stop feature-based releases. Review existing queued runs too. See [push events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#push) and [environment branch policies](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments). This protects environment-bound deployments, not old workflows' starts or image publishing before that gate. Do not push the feature branch during cutover.

Shared concurrency serializes infrastructure/application runs but does not guarantee infrastructure starts first. The initial main merge can start both. PR CI is not a CD dependency: require successful reviewed CI before merging. These retained behaviors require release coordination.

## Safe deployment plan — future authorized operations only

1. Review staged code and revisions. Re-fetch before publication and integrate/retest subsequent main changes. Require Docker-isolated Kafka tests, disposable MySQL migration application, Bicep/workflow checks and PR CI; review SQL with database owners.
2. Owner/admin restricts production Environment deployments to exact main, verifies required reviewers/live OIDC and preserves existing secrets/protection. Review old queued/running feature deployment jobs. No identity/secret change is needed merely to switch source branches.
3. Through separately authorized production checks, record image digests/revisions, VM/app power states, backup/migration history, broker identity, volumes/topics and monitoring prerequisites.
4. Coordinate a release window. If infrastructure is ready, retain existing serialization. If prerequisites need updating first, an owner can temporarily disable the application entrypoint before merging main, let approved infrastructure CD finish, then re-enable and manually dispatch application CD on main. Review stale queued app runs separately. An app run awaiting approval while holding workflow concurrency can block infrastructure; do not use that as an ordering mechanism. None of these control actions occurred here.
5. After approval, publish only the new integration branch and merge via main review. For initial application release use `force_full_build=true` to establish integrated-source images; preserve immutable digest/provenance delivery. Keep destructive inputs false and existing dbcheck/migrate-before-rollout order. Initial automatic main deployment also retains fingerprint selection.
6. Preserve current approved runtime Kafka configuration. If not yet enabled, retain false until public CA, TLS endpoint and contracts/topics are validated. Enabling Kafka or changing Environment secrets needs separate authorization. Preserve volumes and refuse topic mismatches; never delete/recreate topics/databases/persistent data.
7. Verify GitHub/Jira revision/digest provenance, readiness, TLS, Kafka metric families, exporter health, Prometheus targets/rules and dashboards. Exercise approved webhooks and verify outbox publication, receipt/handoff, deduplication, lag and business outcomes. Respect runtime power restoration; intentionally powered-down production is not evidence of rollout failure.
8. On failure use existing previous-revision rollback/runtime restoration, preferably the recorded last-known-good Kafka-capable digest. Keep additive schemas and all durable data. Before reverting to pre-Kafka code or disabling processing, review in-flight outbox/receipt/backlog handling with service owners. Preserve TLS/contracts and never discard pending messages.

No publication, environment-policy change, run cancellation, deployment or rollback action above was executed. Production remains unchanged pending owner review.

## Exact changed-file inventory versus fetched main

The full staged patch and diffstat are also preserved in local `artifacts/main-kafka-integration/integration.patch` and `integration-stat.txt`. A means added; M means modified.

```text
M	.github/workflows/azure-infrastructure.yml
M	.github/workflows/backend-ci.yml
M	.github/workflows/backend-deploy-azure-production.yml
M	.github/workflows/backend-deploy-production.yml
M	.github/workflows/backend-deploy-reusable.yml
A	.github/workflows/kafka-integration-tests.yml
M	.gitignore
M	Directory.Packages.props
M	config/env/README.md
M	config/env/github/.env.example
M	config/env/jira/.env.example
M	deploy/azure/scripts/aca-probe.sh
M	deploy/azure/scripts/build-vm-bundle.sh
M	deploy/azure/scripts/install-bundle.sh
A	deploy/azure/scripts/install-monitoring-bundle.sh
A	deploy/azure/scripts/kafka_config.py
M	deploy/azure/scripts/render-containerapp.py
M	deploy/azure/scripts/validate-vm-stack.sh
A	deploy/azure/validation/test-kafka-operations.sh
M	deploy/azure/validation/test-kafka-smoke.sh
M	deploy/azure/validation/test-network-probe.sh
M	deploy/azure/validation/test-service-env-composition.sh
M	deploy/azure/validation/test-validate-azure-env-files.sh
A	deploy/azure/validation/test_kafka_topics_config.py
A	deploy/azure/validation/test_monitoring_reconciliation.py
A	deploy/azure/validation/test_production_branch_policy.py
M	deploy/azure/validation/validate-azure-env-files.sh
M	deploy/azure/vm/compose.yml
M	deploy/azure/vm/kafka/client-ssl.properties
A	deploy/azure/vm/kafka/topics.json
M	deploy/azure/vm/prometheus/prometheus.yml.template
A	deploy/azure/vm/scripts/kafka-common.sh
A	deploy/azure/vm/scripts/kafka-identity.sh
A	deploy/azure/vm/scripts/kafka-topic-manifest.jq
A	deploy/azure/vm/scripts/kafka-topics.sh
A	deploy/azure/vm/scripts/reconcile-monitoring.sh
M	deploy/azure/vm/scripts/reconcile-stack.sh
A	deploy/azure/vm/scripts/verify-kafka.sh
M	deploy/monitoring/README.md
A	deploy/monitoring/grafana/provisioning/dashboards/json/kafka-observability.json
M	deploy/monitoring/grafana/provisioning/dashboards/json/researchtrack-overview.json
M	deploy/monitoring/prometheus.yml
M	deploy/monitoring/rules/researchtrack-alerts.yml
A	deploy/monitoring/tests/kafka-alerts.test.yml
A	deploy/monitoring/tests/validate_kafka_observability.py
M	deploy/validate-env-files.sh
M	docs/devops/azure-production/02_INFRASTRUCTURE_VM.md
M	docs/devops/azure-production/DECISIONS.md
M	docs/devops/azure-production/IMPLEMENTATION_STATUS.md
A	docs/devops/azure-production/KAFKA_CONTRACT_PROPOSAL.md
A	docs/devops/azure-production/KAFKA_INTEGRATION_TESTING.md
A	docs/devops/azure-production/KAFKA_OBSERVABILITY.md
A	docs/devops/azure-production/KAFKA_OBSERVABILITY_TASK_REPORT.md
A	docs/devops/azure-production/KAFKA_OPERATIONS.md
A	docs/devops/azure-production/KAFKA_TASK_REPORT.md
A	docs/devops/azure-production/KAFKA_TOPICS_EVENT_FLOWS.md
A	docs/devops/azure-production/KAFKA_TOPICS_TASK_REPORT.md
A	docs/devops/azure-production/MAIN_KAFKA_INTEGRATION.md
M	docs/devops/azure-production/README.md
M	docs/devops/azure-production/RUNBOOK.md
A	scripts/test-kafka-integration.sh
A	scripts/test-kafka-observability.sh
A	src/BuildingBlocks/Kafka/KafkaClients.cs
A	src/BuildingBlocks/Kafka/KafkaMessagingWorkers.cs
A	src/BuildingBlocks/Kafka/KafkaMetrics.cs
A	src/BuildingBlocks/Kafka/KafkaPersistence.cs
A	src/BuildingBlocks/Kafka/KafkaRuntimeOptions.cs
A	src/BuildingBlocks/Kafka/KafkaWebhookContract.cs
A	src/Services/ResearchTrack.GitHubService/Features/Webhooks/GitHubKafkaEventHandler.cs
M	src/Services/ResearchTrack.GitHubService/Features/Webhooks/GitHubWebhookDeliveryStore.cs
M	src/Services/ResearchTrack.GitHubService/Persistence/GitHubDbContext.cs
A	src/Services/ResearchTrack.GitHubService/Persistence/Migrations/20261010151156_AddKafkaWebhookMessaging.Designer.cs
A	src/Services/ResearchTrack.GitHubService/Persistence/Migrations/20261010151156_AddKafkaWebhookMessaging.cs
M	src/Services/ResearchTrack.GitHubService/Persistence/Migrations/GitHubDbContextModelSnapshot.cs
M	src/Services/ResearchTrack.GitHubService/Program.cs
M	src/Services/ResearchTrack.GitHubService/ResearchTrack.GitHubService.csproj
M	src/Services/ResearchTrack.GitHubService/appsettings.json
M	src/Services/ResearchTrack.JiraService/Extensions/JiraFeatureExtensions.cs
A	src/Services/ResearchTrack.JiraService/Features/JiraKafkaEventHandler.cs
M	src/Services/ResearchTrack.JiraService/Features/JiraSyncScheduler.cs
M	src/Services/ResearchTrack.JiraService/Features/JiraWebhookService.cs
M	src/Services/ResearchTrack.JiraService/Persistence/JiraDbContext.cs
A	src/Services/ResearchTrack.JiraService/Persistence/Migrations/20261010151156_AddKafkaWebhookMessaging.Designer.cs
A	src/Services/ResearchTrack.JiraService/Persistence/Migrations/20261010151156_AddKafkaWebhookMessaging.cs
M	src/Services/ResearchTrack.JiraService/Persistence/Migrations/JiraDbContextModelSnapshot.cs
M	src/Services/ResearchTrack.JiraService/Program.cs
M	src/Services/ResearchTrack.JiraService/ResearchTrack.JiraService.csproj
M	src/Services/ResearchTrack.JiraService/appsettings.json
A	tests/Kafka/IsolatedKafkaFixture.cs
A	tests/Kafka/KafkaMetricSnapshot.cs
A	tests/Kafka/KafkaTestDatabase.cs
A	tests/Kafka/compose.yml
A	tests/Kafka/prometheus.yml
A	tests/Kafka/verify_observability.py
A	tests/ResearchTrack.GitHubService.Tests/GitHubKafkaFlowTests.cs
A	tests/ResearchTrack.GitHubService.Tests/KafkaMessagingTests.cs
A	tests/ResearchTrack.GitHubService.Tests/KafkaMetricsTests.cs
A	tests/ResearchTrack.GitHubService.Tests/KafkaRuntimeOptionsTests.cs
M	tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj
A	tests/ResearchTrack.JiraService.Tests/JiraKafkaFlowTests.cs
A	tests/ResearchTrack.JiraService.Tests/KafkaMessagingTests.cs
M	tests/ResearchTrack.JiraService.Tests/ResearchTrack.JiraService.Tests.csproj
```
