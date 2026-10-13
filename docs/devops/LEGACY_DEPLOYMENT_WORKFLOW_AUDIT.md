# Legacy Deployment Workflow Audit — TASK 03

Audit date: 2026-10-12. Repository: `Nimsara-Jayarathna/researchtrack-backend`. Working branch: `integration/main-kafka-production-20261011`; baseline commit: `1810f09535d6a6d00947bcf84d18fde473e40c39`.

`main` remains the permanent default branch with its existing protection. Integration remains the temporary Azure production-testing source. TASK 02's uncommitted security changes were present at audit start and are preserved. TASK 03 changes only the current VPS reusable workflow, its offline regressions and this report. Nothing was committed, pushed, merged, dispatched, disabled remotely or deployed; no secrets, Azure identity, federation, settings or persistent data were modified.

## Findings and local correction

**Confirmed dormant production route, corrected locally:** `backend-deploy-reusable.yml` accepted `environment_name: production`, acquired the production Environment and used the SSH/VPS implementation. Its only current caller is `backend-deploy-test.yml`, which passes `test`; Azure production uses a different implementation. The dormant route had production CI/source checks but no independent Azure production concurrency lock. A future caller could therefore deploy to VPS with production env files under an unrelated lock. Repository-wide SSH credentials and deployment-root variables make this more than an unused string constant. No execution or actual VPS production reachability was tested.

The reusable VPS workflow now has a credential-free, checkout-free `validate-target` job. It requires `test`, this exact repository, a full source SHA, and either a develop push or trusted manual dispatch. Build and deployment depend on its success; deployment explicitly references `environment: test`. Non-Test input fails visibly before image publication or Environment access. The obsolete production validation job and SSH source recheck are removed from this Test-only implementation. The input remains for caller compatibility. Existing Test manual ref selection, develop trigger, image-build logic, migrations, SSH delivery, health checks, env backup and cleanup are preserved. No Test CI requirement, host change or concurrency change was introduced.

**Confirmed alternate Azure routes, remote copies unchanged:** main has main-triggered Azure application/infrastructure workflows; the previous Kafka branch has its own branch-triggered copies. They can select different application images for the same production target. Their shared Azure concurrency group serializes runs but does not decide which source is approved. Older DevOps branches also retain manual production-capable workflow copies, including VPS implementations with a different lock. Editing integration cannot retire those copies.

**Configuration concerns, not automatic fixes:** production and test Environments currently have no deployment-branch policy or protection rules. SSH credentials are repository-wide; `BACKEND_DEPLOY_ROOT` and `NPM_NET_NAME` are repository variables. The Test route does not reference production Environment secrets, Azure login or OIDC permission, but credential values/host ownership were not inspected. Manual k6 intentionally uses the VPS as a load generator against Azure production; that is not a VPS application deployment and must remain available.

## Current-branch workflow matrix

All filenames below are under `.github/workflows/`. `I` = integration branch above, `M` = main, `D` = develop. All release checkouts use the event's explicit `github.sha`. Manual Test dispatch retains operator-selected refs; it does not claim those refs passed release CI.

Permission/secret bundles:

- **Registry:** contents read; packages read for detection/deployment and write for publication; automatic `GITHUB_TOKEN`, no Environment secrets or OIDC.
- **Azure app:** contents/packages read and id-token write; production Azure client/tenant/subscription identifiers, ten runtime env-file secrets, optional GHCR pull credentials. Build calls have packages write, CI calls reduce to contents read.
- **Azure infra:** contents read and id-token write; production Azure identifiers and MySQL/Grafana env files for full reconciliation. Monitoring-only uses existing VM runtime files.
- **VPS Test:** contents/packages read, no OIDC; test's ten env-file secrets, repository `SSH_HOST`, `SSH_PORT`, `SSH_USER`, `SSH_PRIVATE_KEY`, automatic registry token. Image build receives packages write. Target-validation job has no token permissions.
- **k6:** contents read and id-token write; production Azure identifiers, dedicated performance accounts/token and optional Jira webhook secret; repository SSH credentials for the load-generator VPS.
- **Runtime:** contents read and id-token write; production Azure identifiers only.
- **Read-only CI:** contents read, no production Environment, inherited secrets or OIDC.

The ten env-file secret names are `MYSQL_ENV_FILE`, `SHARED_AUTH_ENV_FILE`, `AUTH_ENV_FILE`, `PROJECT_ENV_FILE`, `RT_GITHUB_SERVICE_ENV`, `JIRA_ENV_FILE`, `MEETING_ENV_FILE`, `SUBMISSION_ENV_FILE`, `GATEWAY_ENV_FILE` and `GRAFANA_ENV_FILE`. Azure identifiers are `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID`. Optional registry credentials are `GHCR_PULL_USERNAME`/`GHCR_PULL_TOKEN`. k6 uses `K6_PERFORMANCE_TOKEN`, `K6_SUPERVISOR_EMAIL`/`K6_SUPERVISOR_PASSWORD`, `K6_STUDENT_EMAIL`/`K6_STUDENT_PASSWORD` and optional `K6_JIRA_WEBHOOK_CLIENT_SECRET`. Only names were inventoried; no values were retrieved.

`A` = `researchtrack-azure-production`, non-cancelling, `queue: max`. `T` = `researchtrack-backend-test-deployment`, `cancel-in-progress: true`. Reusable implementations rely on their known entrypoint's lock and must not reacquire it. Future callers must preserve that lock.

| Workflow filename | Trigger / source | Environment / target | Permissions and secrets | CI / authorization prerequisites | Direct manual dispatch | Concurrency | Existing recovery | Status |
|---|---|---|---|---|---|---|---|---|
| `backend-deploy-test.yml` | D push; trusted manual selected ref | test / VPS Compose | Registry + VPS Test through callee | Test-target authorization + image-build success; no same-SHA full CI added | Yes | T | Callee retains prior env on failure; manual recovery | Current, preserved |
| `backend-deploy-reusable.yml` | workflow_call; D push or trusted manual caller SHA | **test only** / SSH host, `<BACKEND_DEPLOY_ROOT>/test`, Compose Test project | VPS Test; no Azure permission | New target/repository/event/SHA gate → build → deploy | No; via Test entrypoint | Inherits T | `env.previous` retained until successful health; no automatic image/DB rollback | Current Test implementation; production capability removed locally |
| `backend-build-images.yml` | workflow_call; caller SHA | No Environment / GHCR images | Registry | Production caller requires full CI/source eligibility; Test caller requires target authorization | No | Caller A or T | No automatic registry-tag rollback; source tags/provenance available | Shared publication helper, required |
| `backend-deploy-production.yml` | I push or dispatch on I | production / Azure via callee | Azure app + k6 through callees; reporting contents/actions read | Shared release CI and exact-source policy; optional smoke needs successful deploy | Yes, I only | A | App revision recovery, exact runtime restore; k6 independent safeguards | Current temporary release entrypoint; trigger unchanged |
| `backend-deploy-azure-production.yml` | workflow_call; I caller SHA | production / `rg-researchtrack-prod`, seven Container Apps + migration jobs | Azure app | Required CI → source eligibility → image build; source recheck before credentials | No | Inherits A | Previous healthy revision on rollout failure; always restore captured runtime state; DB migrations not reversed | Current Azure implementation |
| `azure-infrastructure.yml` | I push on listed infra/monitoring paths; I dispatch | production / Bicep + infrastructure VM stack | Azure infra | Required same-SHA CI/Kafka + source check → Bicep build → pre-access source recheck; What-If gate for full reconciliation | Yes, monitoring-only and explicit destructive override retained | A | Data-preserving reconciliation; no automatic ARM/config rollback or full-run power-state restoration. Monitoring-only requires already-running VM | Current, preserved |
| `azure-performance-k6.yml` | Manual M/I through guarded callee | production / Azure runtime + load-generator VPS | k6 | Trusted current-head manual operation + confirmation; does not publish an application release | Yes | A | Callee restores Gateway exemption and runtime snapshots | Current required manual operation |
| `azure-performance-k6-reusable.yml` | Manual M/I; automatic I after successful deployment | production / Azure + VPS k6 | k6 | TASK 02 repository/event/ref job guard, current-head source gate; automatic smoke requires successful validated deployment result | No; via manual entrypoint or production smoke | Inherits A | Runtime-session trap plus final Gateway/runtime restoration; recovery artifacts | Current shared implementation; TASK 02 changes retained |
| `azure-runtime-power.yml` | Manual M/I | production / existing VM and Container Apps | Runtime | TASK 02 source guard/current-head check; stop requires `STOP_PRODUCTION` | Yes | A | Intentional power action; no automatic undo of requested start/stop | Current required operator control |
| `azure-runtime-session.yml` | Manual M/I | production / temporary acquire/hold/restore | Runtime | TASK 02 source guard/current-head check, bounded hold and confirmation | Yes | A | Snapshot, trap, ownership checks, final restore/verify and artifact | Current required operator control |
| `backend-release-validation.yml` | workflow_call; I release SHA | None / CI + read-only GitHub branch-head GET | Read-only CI | Backend CI including required Kafka → strict repository/ref/event/SHA/current-head policy | No | Caller A | Fails closed; no target mutation | Current release authorization helper |
| `backend-ci.yml` | PR to D/M/I; workflow_call for release SHA | None / hosted runner | Read-only CI | Build, coverage, offline security/operations checks and required isolated Kafka job | No | Release caller A; no standalone CI lock | No deployment; failures block dependent release jobs | Current validation, not a deployment path |
| `kafka-integration-tests.yml` | workflow_call; dispatch on selected ref | None / disposable local Kafka/MySQL/Prometheus | Read-only CI | Runner requires all five integration tests execute/pass; missing Docker/tools fail nonzero | Configured, but absent from current default-main workflow directory; direct GitHub dispatch availability not demonstrated | Release caller A; standalone none | Stops only owned disposable fixture; data retained | Current release component; direct manual discoverability limitation |
| `backend-mutation.yml` | Manual selected ref | None / hosted-runner .NET/Stryker | Read-only CI | Unit/mock baseline before mutation run | Yes; file exists on main | None | No deployment | Current optional testing, not a deployment path |

Production app target names/VM defaults are repository code defaults, not a live Azure inventory. Infrastructure derives actual names from Bicep outputs. The VPS's actual SSH host and secret values were not read. Test and production configuration use identically named env-file secrets in different Environments; names alone do not establish value separation.

## Read-only remote scope and interpretation

Frozen branch heads, all 119 workflow files, referenced release/rollout scripts, job controls and metadata are retained in ignored `artifacts/legacy-deployment-workflow-audit/`. Fifteen branches were selected: main, develop, integration, all `devops/` branches, `Deploy-and-Configure-Kafka-Infrastructure`, `feature/azure-performance-workflow`, and `fix/production-image-selection`. No branch was checked out, rewritten or deleted. Remote workflow YAML was safely parsed as data, never executed.

There are 29 distinct deployment-capable file variants across those copies. The appendix below groups byte-identical variants without hiding which branches contain each file. The remote integration snapshot is published HEAD; it does **not** include TASK 02 or TASK 03's local edits. GitHub's workflow registry reports the existing application, infrastructure, Test, build, runtime and performance filenames active; that status is not proof that every branch copy has an authorized or successful deployment route.

Push workflows run from the event's branch/commit, and local reusable-workflow references use the caller's commit. A main push filter stored on an old DevOps branch does not cause a push to that DevOps branch to deploy main; its manual dispatch can still be selectable if the corresponding workflow exists on the default branch. Dispatch additionally needs normal operator access and applicable Environment/code gates. Changing the repository default does not neutralize branch copies. See [GitHub workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows), [manual workflow execution](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow) and [reusable workflows](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows).

The current default main directory has no `backend-release-validation.yml` or `kafka-integration-tests.yml`. Required integration CI can call the latter from its own commit, but this audit does not claim the new standalone Kafka workflow is dispatchable from GitHub's default-branch UI/API. Keep the isolated local runner available; address default-branch discoverability through a future reviewed PR rather than changing the default branch or dispatching anything now.

## Confirmed risks and recommended handling

1. **Multiple live source routes to the same Azure production target.** Main and previous Kafka automatic routes still exist. Main protection protects changes to main; it does not make the previous Kafka route disappear. Same concurrency prevents overlap among Azure entrypoints but allows sequential replacement by another branch. Retire/guard prior triggers through separately reviewed branch-specific PRs after approval; do not edit those branches here.
2. **Legacy manual VPS production and dynamic Environment selection.** Old shared reusable copies accept `production`; early DevOps production callers actually pass it and use `researchtrack-backend-production-deployment`. They can materialize production configuration using repository SSH credentials, without the Azure release lock. Current branch is now Test-only; backport that correction through reviewed PRs where still required, preserving develop Test callers and existing operator tasks.
3. **Remote releases lack integration's same-SHA gate.** Main/most older Azure copies require image build for app delivery and Bicep compile for infra, rather than integration's shared CI/Kafka/current-head policy. Main's PR protection does not prove CI ran for every manual alternate ref. Previous Kafka copies have branch guards but no equivalent full release gate. Keep the reviewed integration path for temporary testing; migrate the complete gate together at final cutover.
4. **Environment trust is broader than the intended testing source.** Both Environments currently report null branch policy and empty protection rules. TASK 02 also found immutable OIDC subjects differing from the bootstrap's older example. Actual Azure federation/RBAC is unverified. Do not change identity/federation, create a permanent integration ruleset, or assume an Environment subject filters a branch. See [CI/CD Security Verification](CICD_SECURITY_VERIFICATION.md).
5. **Code reuse versus historical duplication.** The current application, infrastructure and k6 implementations serve different targets/transactions; keep them. The shared image helper is needed by Test and Azure. Historical inline k6 and VPS-production callers duplicate functionality already centralized in current workflows and are retirement candidates. Do not remove Test's VPS implementation because k6 also happens to use a VPS.
6. **Test operational limits are unchanged.** Test uses mutable `:test` tags, cancels superseded Test deployments, has no automatic rollback and no full CI prerequisite inside its deploy workflow. The new source/target gate does not improve those unrelated properties. Treat immutable Test release references, cancellation-safe recovery or additional CI as separate improvements, not reasons to modify production credentials or block current manual operations.

No claim is made that an older route was exploited, currently has every required configuration value, or that a shared SSH key targets a production host. Only names and workflow/configuration structure were inspected.

## Verification and evidence

Fresh TASK 03 results are in `artifacts/legacy-deployment-workflow-audit/results.json` and corresponding logs. These runs use synthetic configuration and stub Azure/GHCR/SSH commands. There was no production connection from test fixtures.

| Executed check | Result |
|---|---|
| `python3 deploy/azure/validation/test_release_policy.py` | PASS — 22 tests; the obsolete VPS-production regression now requires successful Test authorization/build and a static test Environment |
| `python3 deploy/azure/validation/test_workflow_release_gates.py` | PASS — 27 tests, including five new VPS target/source/caller regressions |
| `bash deploy/azure/validation/test-service-env-composition.sh` | PASS — unchanged app/migration secret composition |
| `bash deploy/validate-shared-auth-wiring.sh` | PASS — local/Test/Azure JWT configuration wiring |
| `bash deploy/azure/validation/test-deploy-image-selection.sh` | PASS — Azure same-source images, failure propagation and rollout recovery |
| `bash deploy/azure/validation/test-runtime-power.sh` | PASS — mocked existing power-state restoration |
| `bash tests/performance/test-gateway-exemption.sh` | PASS — mocked existing exemption/restoration |
| Unfiltered actionlint 1.7.12 | FAIL — existing five queue-schema and five SC2129 style findings |
| Qualified actionlint | PASS — only the known queue-schema and SC2129 findings excluded |
| Current/remote YAML parsing, shell syntax, changed Python compilation and diff checks | PASS — 14 local and 119 remote workflow files, 47 deploy shell scripts, 2 TASK 03 Python test files, clean diff whitespace |
| Live dispatch, Azure/VPS target checks, full .NET/Kafka rerun, production rollback exercise | NOT EXECUTED — this is a workflow audit; no live execution authorized or application/fixture code changed |

The new regressions execute the real authorization shell body with synthetic context: reject production/empty/unknown inputs; accept develop pushes and trusted manual refs; reject forks/PRs/wrong automatic refs/malformed SHAs; require authorization before build and static test Environment; ensure only Test uses the VPS implementation and Azure retains its release route. An initial test run exposed two test-harness extraction/assertion mistakes; both were corrected, and the final suite passed. This was not a live workflow failure.

Read-only run-history observations, which predate these edits:

| Source / workflow | Most recent observed run | Conclusion |
|---|---|---|
| Main app release | [38117467983](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38117467983), 2026-10-11, `141181575e7b` | CANCELLED; do not infer all partial deployment work was undone |
| Main infrastructure | [37576983929](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/37576983929), 2026-10-07 | SUCCESS at that run's older SHA, not verification of current main |
| Develop Test | [37184820991](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/37184820991), 2026-10-04, `04d40956c986` | SUCCESS; Test remains an active required path |
| Previous Kafka app/infra | [38123492270](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38123492270) / [38123491911](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38123491911), 2026-10-11, `4f9d5f098aa9` | SUCCESS; the old route is not merely an abandoned filename |
| Integration app/infra | [38153663471](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38153663471) / [38153663507](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38153663507), 2026-10-11, `1810f09535d6` | SUCCESS for published baseline; local changes untested on GitHub |

Main remains protected; read-only ruleset evidence retains its strict `Restore, Build and Test` check, required Test deployment, PR/Code Owner approval and deletion/force-push restrictions. No bypass or protection relaxation is part of this plan.

## Safe future retirement and cutover plan

These are future owner-reviewed steps, not actions executed or authorization to execute them now.

1. Review the local TASK 02/03 diffs and fresh offline results. Inventory queued/running app, infrastructure, Test and manual sessions using read-only metadata before any publishing decision. Record approved source, environment and recovery owner; retain current revision/image digests, runtime snapshot evidence and database backup references privately. Do not disable the repository workflow ID to retire a single branch copy: integration/main/manual consumers share those filenames.
2. While integration testing is active, coordinate a release hold on alternate production sources. Through separate reviewed PRs on affected refs, neutralize only the obsolete automatic production triggers and legacy VPS production acceptance. Where main must temporarily stop automatic production delivery, make that an explicit reversible workflow change; keep main as default and preserve its protection, Test, CI and required operator controls. Do not assume this local branch already implements that hold.
3. Preserve develop's Test entrypoint and shared build/VPS helper. Backport the Test-only target gate through the established develop review process; retain repository SSH use for Test and k6 until ownership/rotation is separately approved. Do not delete credentials based on an old "unused VPS Production" note.
4. Meet main's existing PR approvals, required checks and required Test deployment through the approved Test process. Do not weaken protection to merge integration. Publish only after owner approval; no commit/PR/merge is performed by this audit.
5. For final main cutover, change the approved release branch atomically in application/infra triggers, job guards, release-policy constant, operational allowlists and their tests/docs. Preserve the same-SHA CI/Kafka gate, immutable image/provenance checks, production Environment/OIDC inputs and shared Azure lock. Retain reviewed manual power/session/k6 behavior. Changing only a trigger or only `BRANCH` produces an inconsistent release path.
6. Observe required CI for the final published main SHA before any approved release. Perform staged app/infrastructure validation only through the established process; avoid unrelated infra reconciliation for an application-only release. Confirm event contracts, migrations, health, monitoring and runtime restoration with actual evidence. Preserve old compatible app revisions/images and database backups; do not remove Kafka topics, data, volumes or databases.
7. After successful main acceptance, retire the previous Kafka/integration production triggers and obsolete inline/manual production paths through reviewed PRs. Keep branches/history until retention and recovery ownership are agreed; deletion is unnecessary for disabling a route. Ensure any retained manual operation checks the intended source and uses the shared lock. No new permanent integration ruleset is required.

## Verification and rollback instructions

To reproduce the local checks, run the seven commands in the verification table from the integration worktree. `actionlint` shows the raw baseline findings; the qualified diagnostic is:

```bash
actionlint -ignore 'unexpected key "queue"' -ignore 'SC2129'
git diff --check
```

For a future reviewed cutover, inspect workflow definitions at the exact candidate SHA rather than trusting branch labels or an Actions run name. Verify the app and infrastructure call the same gate, all required CI results are success, Test still uses `test`, manual operator paths remain reachable and every production-mutating entrypoint uses the same non-cancelling lock. Check check-run names against main protection without changing the rules. An Actions success with skipped deployment is not proof a new version was deployed.

**Workflow configuration rollback:** retain a patch/reviewed commit for every future trigger/policy edit. If release authorization or required manual operations regress, stop further publishing and revert that reviewed workflow change through the normal protected-branch process. Restore a consistent trigger/guard/policy/test set. Do not re-enable VPS production as a shortcut, re-run an old successful run blindly, or turn both old and new automatic production routes back on.

**Azure application/runtime recovery:** use the existing application's previous-healthy-revision recovery and runtime ownership/snapshot restoration, followed by verification. Failed migrations are not automatically reversed; validate older app compatibility with the applied schema before rollback. If the runner disappears, recover using that exact run's runtime/Gateway state artifacts and the [Azure runbook](azure-production/RUNBOOK.md) under explicit operator approval. Do not take a new snapshot of the failed state and call it the original. Snapshot validation alone is local/read-only; restoring it changes runtime state and was not executed here.

**Infrastructure recovery:** no transactional ARM or VM-config rollback exists. Stop repeated reconciliations, inspect What-If/resource state and compare with last-known-good config. Use an approved narrowly scoped correction or monitoring-only delivery when appropriate; do not recreate disks, broker volumes, topics or databases. Restoring application runtime state does not undo infrastructure changes.

**VPS Test recovery:** `env.previous` is a retained config backup until health succeeds, not automatic rollback. The script applies migrations and mutable images; older image identities may not be recoverable unless recorded before rollout. With Test-owner approval, select known compatible image digests and saved configuration, then run health verification. Do not run the deploy script blindly as a rollback: it pulls current tags and reapplies migrations. Retain backups privately; do not print env files or attach them to audit artifacts. No VPS operation was performed here.

## TASK 03 changed files

- `.github/workflows/backend-deploy-reusable.yml`: Test-only VPS target, credential-free source authorization, static test Environment; obsolete production route removed.
- `deploy/azure/validation/test_release_policy.py`: replace the old VPS-production expectation with Test authorization/build/Environment requirements.
- `deploy/azure/validation/test_workflow_release_gates.py`: five offline VPS authorization/caller regressions; existing release/operational tests retained.
- `docs/devops/LEGACY_DEPLOYMENT_WORKFLOW_AUDIT.md`: complete audit and retirement/recovery plan.

TASK 02 files already modified at audit start remain local. No main, develop or old DevOps branch files were edited remotely.

## Remote-copy appendix

The following frozen-head inventory and variant matrices are generated from the safely parsed read-only snapshots. Abbreviated permission/secret bundles use the definitions above; remote published copies may lack the local corrections.

### Frozen remote branch heads

| ID | Branch | Exact inspected commit | Protection observed |
|---|---|---|---|
| B1 | `Deploy-and-Configure-Kafka-Infrastructure` | [`54dcbae67793a2103b6ef78502401bf4ab04b336`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/54dcbae67793a2103b6ef78502401bf4ab04b336) | Not protected |
| B2 | `develop` | [`04d40956c986e74b3d81f3175f9b0d04a8d41b62`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/04d40956c986e74b3d81f3175f9b0d04a8d41b62) | Protected |
| B3 | `devops/SCRUM-204-do-304-extend-prometheus-and-grafana-for-jira-integration-monitoring` | [`2e8e1474b8dc08a70a53978f626967133a43036a`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/2e8e1474b8dc08a70a53978f626967133a43036a) | Not protected |
| B4 | `devops/azure-production-deployment` | [`3c05d6689688040e5030fe3cbc6a97f89e75144f`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/3c05d6689688040e5030fe3cbc6a97f89e75144f) | Not protected |
| B5 | `devops/infrastructure-setup` | [`a04fca5865964781e61c444264aaf41b2dcf704c`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/a04fca5865964781e61c444264aaf41b2dcf704c) | Not protected |
| B6 | `devops/resize-prod-vm-to-b2s` | [`a00c850d2048bdd08e2f3ec2b6b5716e922cac51`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/a00c850d2048bdd08e2f3ec2b6b5716e922cac51) | Not protected |
| B7 | `devops/scrum-194-do-201-integrate-prometheus-metrics-collection` | [`8c26e867c6562ab19a5a228238ef45848a4f77fb`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/8c26e867c6562ab19a5a228238ef45848a4f77fb) | Not protected |
| B8 | `devops/scrum-195-do-202-deploy-grafana-and-build-researchtrack-service-dashboard` | [`c56f6c9cefa7019ba8830e57929114db5ff37bbf`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/c56f6c9cefa7019ba8830e57929114db5ff37bbf) | Not protected |
| B9 | `devops/scrum-196-do-203-add-github-integration-monitoring` | [`3dcdc6909b78a0630a28a83f078b710c2c3b3250`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/3dcdc6909b78a0630a28a83f078b710c2c3b3250) | Not protected |
| B10 | `devops/scrum-197-do-204-configure-basic-monitoring-alerts-and-failure-verification` | [`6230d4ad316e3afdc4ec6f7c899a4dc020c843b4`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/6230d4ad316e3afdc4ec6f7c899a4dc020c843b4) | Not protected |
| B11 | `devops/sprint4-kafka-infrastructure-verification` | [`4f9d5f098aa964d2ad8bce6f44ce0b3dce96c2ad`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/4f9d5f098aa964d2ad8bce6f44ce0b3dce96c2ad) | Not protected |
| B12 | `feature/azure-performance-workflow` | [`f80acea6f90e7ca000b4d43c6a4f82cf8408bef1`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/f80acea6f90e7ca000b4d43c6a4f82cf8408bef1) | Not protected |
| B13 | `fix/production-image-selection` | [`f2673f8cce8e8a28915854a662fff296d35826dc`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/f2673f8cce8e8a28915854a662fff296d35826dc) | Not protected |
| B14 | `integration/main-kafka-production-20261011` | [`1810f09535d6a6d00947bcf84d18fde473e40c39`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/1810f09535d6a6d00947bcf84d18fde473e40c39) | Not protected |
| B15 | `main` | [`141181575e7b7fdda75e31356bfe9a980deadb7c`](https://github.com/Nimsara-Jayarathna/researchtrack-backend/tree/141181575e7b7fdda75e31356bfe9a980deadb7c) | Protected |

### Deployment-capable remote variants

Each row records every branch copy in its ID list. “Manual-capable” describes workflow configuration, not a live dispatch test. Caller locks mean no separate lock exists in that reusable file. Main-filtered old branches have no automatic push route from their own head; their dispatch/call routes still matter. No listed remote copy was changed.

| Filename / variant | Copies | Trigger / source | Environment / target | Permissions / secrets | CI prerequisite | Direct dispatch | Lock | Recovery | Status |
|---|---|---|---|---|---|---|---|---|---|
| azure-infrastructure.yml #1 | B11 | Push devops/sprint4-kafka-infrastructure-verification (path-filtered); manual devops/sprint4-kafka-infrastructure-verification | production / Bicep + VM | Azure infra | Bicep compile only | Yes | researchtrack-azure-production; cancel=false; queue=max | What-If deletion gate; manual infra/config recovery | Automatic route: B11; manual-capable |
| azure-infrastructure.yml #2 | B14 | Push integration/main-kafka-production-20261011 (path-filtered); manual integration/main-kafka-production-20261011 | production / Bicep + VM | Azure infra | Full CI/Kafka + source gate + Bicep | Yes | researchtrack-azure-production; cancel=false; queue=max | What-If deletion gate; manual infra/config recovery | Automatic route: B14; manual-capable |
| azure-infrastructure.yml #3 | B2, B3, B4, B6, B13 | Push main (path-filtered); manual selected ref | production / Bicep + VM | Azure infra | Bicep compile only | Yes | researchtrack-azure-production; cancel=false; default queue | What-If deletion gate; manual infra/config recovery | Historical/helper copies; manual-capable |
| azure-infrastructure.yml #4 | B1, B12, B15 | Push main (path-filtered); manual selected ref | production / Bicep + VM | Azure infra | Bicep compile only | Yes | researchtrack-azure-production; cancel=false; queue=max | What-If deletion gate; manual infra/config recovery | Automatic route: B15; manual-capable |
| azure-performance-k6-reusable.yml #1 | B1, B11, B14, B15 | Call; caller SHA | production / Azure + VPS load generator | k6 | Request/config validation; automatic caller needs deploy success | No | Caller lock | Runtime + Gateway snapshot restoration | Historical/helper copies; call-only |
| azure-performance-k6.yml #1 | B12 | manual selected ref | production / Azure + VPS load generator | k6 | Request/config validation; no release CI | Yes | researchtrack-azure-production; cancel=false; queue=max | Runtime + Gateway snapshot restoration | Historical/helper copies; manual-capable |
| azure-performance-k6.yml #2 | B1, B11, B14, B15 | manual selected ref | production / Azure + VPS load generator | k6 | Request/config validation; no release CI | Yes | researchtrack-azure-production; cancel=false; queue=max | Runtime + Gateway snapshot restoration | Historical/helper copies; manual-capable |
| azure-runtime-power.yml #1 | B1, B11, B12, B14, B15 | manual selected ref | production / Azure VM + apps | Runtime | Request/config validation; no source or CI gate | Yes | researchtrack-azure-production; cancel=false; queue=max | Intentional state change; no undo | Historical/helper copies; manual-capable |
| azure-runtime-session.yml #1 | B1, B11, B12, B14, B15 | manual selected ref | production / Azure VM + apps | Runtime | Request/config validation; no source or CI gate | Yes | researchtrack-azure-production; cancel=false; queue=max | Snapshot + trap/final runtime restore | Historical/helper copies; manual-capable |
| backend-build-images.yml #1 | B1, B2, B3, B6, B11, B12, B13, B15 | Call; caller SHA | No Environment / GHCR | Registry | Caller controls prerequisites; detection → build | No | Caller lock | No registry tag rollback | Historical/helper copies; call-only |
| backend-build-images.yml #2 | B14 | Call; caller SHA | No Environment / GHCR | Registry | Caller controls prerequisites; detection → build | No | Caller lock | No registry tag rollback | Historical/helper copies; call-only |
| backend-build-images.yml #3 | B4, B5, B7, B8, B9, B10 | Call; caller SHA | No Environment / GHCR | Registry; old detector packages write | Caller controls prerequisites; detection → build | No | Caller lock | No registry tag rollback | Historical/helper copies; call-only |
| backend-deploy-azure-production.yml #1 | B1, B12, B15 | Call; caller SHA | production / Azure apps + migration jobs | Azure app | Image build only | No | Caller lock | App revision recovery + runtime snapshot restore | Historical/helper copies; call-only |
| backend-deploy-azure-production.yml #2 | B14 | Call; caller SHA; production integration/main-kafka-production-20261011 | production / Azure apps + migration jobs | Azure app | Full CI/Kafka + source gate → build | No | Caller lock | App revision recovery + runtime snapshot restore | Historical/helper copies; call-only |
| backend-deploy-azure-production.yml #3 | B4 | Call; caller SHA | production / Azure apps + migration jobs | Azure app | Image build only | No | Caller lock | App revision recovery; no runtime restore | Historical/helper copies; call-only |
| backend-deploy-azure-production.yml #4 | B11 | Call; caller SHA; production devops/sprint4-kafka-infrastructure-verification | production / Azure apps + migration jobs | Azure app | Image build only | No | Caller lock | App revision recovery + runtime snapshot restore | Historical/helper copies; call-only |
| backend-deploy-azure-production.yml #5 | B2, B3, B6, B13 | Call; caller SHA | production / Azure apps + migration jobs | Azure app | Image build only | No | Caller lock | App revision recovery; no runtime restore | Historical/helper copies; call-only |
| backend-deploy-production.yml #1 | B12 | Push main; manual selected ref | production / Azure | Azure app | Image build only | Yes | researchtrack-azure-production; cancel=false; queue=max | App revision recovery + runtime snapshot restore | Historical/helper copies; manual-capable |
| backend-deploy-production.yml #2 | B5, B7, B8, B9, B10 | Push main; manual selected ref | production / VPS | VPS production + Registry | Image build only | Yes | researchtrack-backend-production-deployment; cancel=false; default queue | Saved env; manual recovery | Historical/helper copies; manual-capable |
| backend-deploy-production.yml #3 | B11 | Push devops/sprint4-kafka-infrastructure-verification; manual devops/sprint4-kafka-infrastructure-verification | production / Azure | Azure app + k6 | Image build only | Yes | researchtrack-azure-production; cancel=false; queue=max | App revision recovery + runtime snapshot restore | Automatic route: B11; manual-capable |
| backend-deploy-production.yml #4 | B2, B3, B4, B6, B13 | Push main; manual selected ref | production / Azure | Azure app | Image build only | Yes | researchtrack-azure-production; cancel=false; default queue | App revision recovery; no runtime restore | Historical/helper copies; manual-capable |
| backend-deploy-production.yml #5 | B1, B15 | Push main; manual selected ref | production / Azure | Azure app + k6 | Image build only | Yes | researchtrack-azure-production; cancel=false; queue=max | App revision recovery + runtime snapshot restore | Automatic route: B15; manual-capable |
| backend-deploy-production.yml #6 | B14 | Push integration/main-kafka-production-20261011; manual integration/main-kafka-production-20261011 | production / Azure | Azure app + k6 | Full CI/Kafka + source gate | Yes | researchtrack-azure-production; cancel=false; queue=max | App revision recovery + runtime snapshot restore | Automatic route: B14; manual-capable |
| backend-deploy-reusable.yml #1 | B8, B9 | Call; caller SHA | Input Environment / VPS (test or production) | VPS Test/production + Registry | Image build only | No | Caller lock | Saved env; manual recovery | Historical/helper copies; call-only |
| backend-deploy-reusable.yml #2 | B11 | Call; caller SHA; production devops/sprint4-kafka-infrastructure-verification | Input Environment / VPS (test or production) | VPS Test/production + Registry | Image build only; production ref guard | No | Caller lock | Saved env; manual recovery | Historical/helper copies; call-only |
| backend-deploy-reusable.yml #3 | B14 | Call; caller SHA; production integration/main-kafka-production-20261011 | Input Environment / VPS (test or production) | VPS Test/production + Registry | Production CI/Kafka + source gate; Test build only | No | Caller lock | Saved env; manual recovery | Historical/helper copies; call-only |
| backend-deploy-reusable.yml #4 | B1, B2, B3, B4, B6, B10, B12, B13, B15 | Call; caller SHA | Input Environment / VPS (test or production) | VPS Test/production + Registry | Image build only | No | Caller lock | Saved env; manual recovery | Historical/helper copies; call-only |
| backend-deploy-reusable.yml #5 | B5, B7 | Call; caller SHA | Input Environment / VPS (test or production) | VPS Test/production + Registry | Image build only | No | Caller lock | Saved env; manual recovery | Historical/helper copies; call-only |
| backend-deploy-test.yml #1 | B1, B2, B3, B4, B5, B6, B7, B8, B9, B10, B11, B12, B13, B14, B15 | Push develop; manual selected ref | test / VPS via callee | VPS Test + Registry | Image build; no full CI gate | Yes | researchtrack-backend-test-deployment; cancel=true; default queue | Saved env; manual recovery | Automatic route: B2; manual-capable |

Remote parent permissions are ceilings for nested jobs; referenced secret bundles describe the effective callee, not secrets materialized in a reusable-call job. All variants use contents read. Azure bundles add id-token write; VPS variants do not. Optional GHCR pull credentials and actual host/Environment value correctness were not verified. Recovery entries describe existing code, not a production recovery test. Infrastructure What-If is a deletion gate, not a transactional rollback.

Remote VPS copies on B5/B7 predate Grafana env delivery and omit `GRAFANA_ENV_FILE`; the other VPS copies reference all ten env-file secrets. Their shared SSH/registry names are unchanged. This historical variation is not a reason to change current Test env delivery.

### Supporting remote workflow coverage

| Supporting file | Branch copies | Target / secrets / prerequisites / dispatch / recovery |
|---|---|---|
| backend-ci.yml | B1, B2, B3, B4, B5, B6, B7, B8, B9, B10, B11, B12, B13, B14, B15 | PR/call or older push CI; no production Environment/OIDC; full release gating only where caller requires it; no direct dispatch; no target mutation |
| backend-mutation.yml | B14, B15 | Manual mutation tests; contents read, no Environment/OIDC; unit/mock precheck; no deploy lock or deployment rollback needed |
| backend-release-validation.yml | B14 | Call-only I same-SHA CI/Kafka/source gate; contents read; no inherited secrets/OIDC; caller lock; no target mutation |
| kafka-integration-tests.yml | B11, B14 | B11: manual-only historical fixture workflow, not a release prerequisite. B14: call/dispatch configuration, required five-test release fixture. Both contents read, no Environment/OIDC or production target; default-main direct dispatch not demonstrated |
