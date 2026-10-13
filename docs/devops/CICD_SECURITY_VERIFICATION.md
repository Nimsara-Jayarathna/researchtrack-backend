# CI/CD Security Verification — TASK 02

Verified locally on 2026-10-12 against `1810f09535d6a6d00947bcf84d18fde473e40c39`, on `integration/main-kafka-production-20261011`. Changes below are uncommitted. GitHub metadata was read with GET requests; no workflow was dispatched, settings or secrets edited, Azure login performed, or deployment executed.

`main` remains the permanent repository default. Integration remains the temporary application/infrastructure production-testing source. This task creates no integration ruleset and requires no new permanent integration protection. Earlier documentation recommending an integration ruleset is superseded by this scope. Existing deployment triggers, Environment references, credentials, Azure identity, federation, migrations and runtime rollback are preserved.

## Confirmed defects and corrections

The application and infrastructure release paths already gate Azure access behind shared CI and source validation. However, `azure-runtime-power.yml`, `azure-runtime-session.yml` and `azure-performance-k6-reusable.yml` had no equivalent repository/event/source authorization before Azure login. The manual entrypoints could select other refs; the reusable performance workflow had no internal source check if invoked by another caller. With an Environment-based OIDC subject, Azure cannot infer the intended branch restriction from that subject.

The three workflows now reject untrusted repository/event/ref contexts at job level, before entering the production Environment. They explicitly check out `github.sha`, disable persisted checkout credentials, and authorize the source before credential-bearing configuration steps or Azure login. Manual runtime/performance operations permit the current head of `main` or the temporary integration branch in this repository, only through `workflow_dispatch`. They do not build or release application images, so they do not falsely claim release CI ran. Existing operator confirmations and restoration safeguards remain in place.

Automatic post-deployment smoke receives `needs.deploy.result` from the production caller. The default is empty, so a caller omitting successful deployment evidence fails closed. It uses the existing strict release policy: integration source, trusted repository, push/manual release event, full immutable SHA, successful validated deployment and current branch head. These checks trust reviewed caller workflow code; an input is not an independently attested GitHub check result. The existing caller dependency on successful deployment remains essential.

The policy error handler also exposed the text of every `ValueError`, including errors from HTTP/header validation that can contain credentials. It now prints only fixed `PolicyError` messages; other exceptions print their type. Sentinel regressions cover both ordinary network errors and external `ValueError` messages.

The release-policy CLI's default remains the strict release path. `--operation` is used only by the manual operational paths above, accepts no user-supplied SHA or branch input, and performs the same read-only branch-head comparison. It is not used by application or infrastructure deployment.

## Verified controls

| Control | Finding and evidence |
|---|---|
| CI before privileged deployment | Application `validate → build → deploy`; infrastructure `eligibility → validate → deploy`. Shared validation runs `backend-ci.yml` before source eligibility. No Azure/Environment secrets or OIDC permission are passed to that CI. Source is rechecked before app/infra secrets and Azure login. |
| Kafka cannot be skipped in release CI | Backend CI's required Kafka reusable job has no optional condition. The isolated runner checks TRX counts: GitHub 3, Jira 2; every test must execute and pass. Missing tools/Docker return nonzero. No `continue-on-error` bypass exists in the required chain. |
| Immutable deployment source | Release, CI, Kafka, image-build, infrastructure and reporting checkouts use `github.sha`, with persisted credentials disabled. Deployment policies compare that SHA with the approved branch head; no PR artifact or caller-supplied release SHA is accepted. Revised operational checkouts now do the same source check, without claiming CI validation for manual operations. |
| Failure/cancellation/skip blocking | Release policy accepts only `success`. Default dependency gating prevents app/infra deployment when required validation fails, cancels or skips. Required CI/Kafka jobs have no skip conditions; offline graph tests prevent introducing them. Optional smoke and reporting do not override deployment failure. |
| PR isolation | PR CI has `contents: read`, no production Environment, Azure login, inherited secrets or OIDC permission. Production policy rejects `pull_request`, `pull_request_target`, `workflow_run` and other unapproved events. New manual-operation checks also reject those events. |
| GitHub token least privilege | CI/eligibility: contents read only. Image detection: packages read; image publication: packages write. Azure app deployment: contents/packages read plus id-token write; infrastructure/operations: contents read plus id-token write. Reporting: contents/actions read. Production caller declarations are permission ceilings for reusable jobs; CI explicitly reduces them. No write-all, repository contents write or PAT is used for CI/image publication. |
| Repository/fork trust | Exact repository check rejects forks even if branch names match. Untrusted contexts are rejected before the policy's authenticated GitHub GET request. Fork tokens also cannot match this repository's expected Azure subject. This is conditional on the actual Azure trust inventory matching expectations below. |
| Secret delivery | Environment files use `umask 077`; generated app/job specs use mode 0600. Sensitive environment keys and GHCR pull passwords are delivered through Azure `secretRef`/registry secret references. VM runtime archives use protected Run Command parameters; bundles and Environment files are cleaned with `always()`. Kafka public CA handling and required-secret validation are unchanged. |
| Logs/artifacts | Reviewed workflow bodies do not echo runtime credentials or upload env/spec/runtime secret bundles. Artifact paths cover coverage, isolated test evidence, runtime-state snapshots and performance reports. Policy errors redact network exception bodies/headers; a new sentinel regression checks this. Azure command errors and arbitrary application/remote stdout are not proven universally free of secrets; no real secret values or complete production logs were inspected. |
| App/infra concurrency | Application, infrastructure, manual runtime power/session and manual performance entrypoints share `researchtrack-azure-production`, `cancel-in-progress: false`, `queue: max`. Called workflows do not reacquire their caller's lock. This serializes these repository entrypoints; it does not lock out CLI operators or older workflow copies on other branches. |
| Restoration/rollback | Runtime capture, ownership markers, `always()` restore/verify and recovery artifacts are unchanged. Failed rollouts still fail after previous-revision recovery. Gateway exemption restoration remains independent of runtime restoration. Mocked regression suites verify both. |
| Permanent main protection | Read-only API confirms default branch `main`, active `Protect Main` ruleset, deletion/force-push prevention, two PR approvals, Code Owner review, stale-review dismissal, resolved conversations, strict required `Restore, Build and Test` check and required `test` deployment. No settings were changed. |
| Temporary integration testing | Application/infrastructure push filters and strict release branch remain integration. Existing Environment name and OIDC inputs are unchanged. New manual operations permit integration as well as main. Published baseline release succeeded; the local corrections have not run in GitHub or Azure. |

Concurrency queueing is supported by current GitHub, even though the installed actionlint does not recognize the `queue` field. See [GitHub concurrency documentation](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#concurrency).

## Azure OIDC findings and remaining configuration evidence

All Azure workflows retain `azure/login@v2` with `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID`, no client secret, and `environment: production`. Azure Login's default Azure-public-cloud audience is `api://AzureADTokenExchange`. User-assigned identity federation on a hosted runner uses this OIDC login flow; switching to `auth-type: IDENTITY` would instead request managed-identity authentication from the runner. See [Azure Login's official documentation](https://github.com/Azure/login).

Read-only GitHub metadata reports:

```json
{
  "use_default": true,
  "use_immutable_subject": true,
  "sub_claim_prefix": "repo:Nimsara-Jayarathna@66525584/researchtrack-backend@1335622081"
}
```

Therefore the expected production job subject, inferred from that configuration and GitHub's documented format, is:

```text
issuer:  https://token.actions.githubusercontent.com
subject: repo:Nimsara-Jayarathna@66525584/researchtrack-backend@1335622081:environment:production
audience: api://AzureADTokenExchange
```

An Environment subject does not encode the branch or inherently exclude PRs. Reusable-workflow claims are not automatically additional Azure trust restrictions. GitHub's immutable format includes owner/repository IDs. See [GitHub OIDC reference](https://docs.github.com/en/actions/reference/security/oidc). Azure validates the configured federation issuer, subject and audience against the token; see [Microsoft's federation documentation](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-create-trust).

**Do not run `bootstrap-oidc.sh` to verify this.** It is a mutating one-time bootstrap, hardcodes the older name-only subject and checks only an existing credential's subject. Its assumption differs from the GitHub metadata above. That is a confirmed bootstrap assumption gap, not proof that the deployed identity is broken. The current federation and role inventory were NOT EXECUTED: Azure CLI is absent here. No identity/federation code or cloud setting was changed. The latest published baseline's successful OIDC login is evidence of a working exchange at that time, not an inventory of every allowed federated subject or role.

For an already authenticated operator, the following are read-only inventory commands; do not log in, create credentials or add roles as part of this audit:

```bash
az identity federated-credential list \
  --resource-group rg-researchtrack-bootstrap \
  --identity-name id-researchtrack-github-prod \
  --query '[].{name:name,issuer:issuer,subject:subject,audiences:audiences}' -o json
az identity show -g rg-researchtrack-bootstrap -n id-researchtrack-github-prod \
  --query '{clientId:clientId,principalId:principalId}' -o json
# Use the returned principalId; these identifiers are not credentials.
az role assignment list --assignee '<principalId>' --all \
  --query '[].{role:roleDefinitionName,scope:scope}' -o json
```

Confirm that the identity client ID corresponds to the existing production login configuration, and inspect every credential, issuer/audience and role scope. Do not replace an existing credential based only on the repository example. Bootstrap requests subscription-level Contributor to create the resource group; actual current RBAC, additional subjects and other identities remain unverified.

The `production` Environment API currently reports `deployment_branch_policy: null`, `protection_rules: []`. It provides secret scoping but no independent branch/reviewer gate. Code authorization depends on trusted reviewed workflow contents and repository write access; contributors who can replace those contents can also remove checks. Other branches' old workflow definitions have not been patched by editing this worktree. No integration ruleset or Environment setting change is required by this task.

## Executed verification

Fresh evidence is retained locally under ignored `artifacts/cicd-security-verification/`, including command outputs, exit codes, syntax checks and read-only GitHub metadata. Offline tests use synthetic data and stub Azure/GHCR/SSH commands; they do not connect to production.

| Check | Actual result |
|---|---|
| `python3 deploy/azure/validation/test_release_policy.py` | PASS — 22 tests, including 5 new authorization/API/redaction regressions |
| `python3 deploy/azure/validation/test_workflow_release_gates.py` | PASS — 22 tests, including 6 new operational source, automatic-smoke, mandatory Kafka and shared-lock regressions |
| `python3 deploy/azure/validation/test_monitoring_reconciliation.py` | PASS — 12 tests |
| `bash deploy/azure/validation/test-validate-azure-env-files.sh` | PASS — synthetic environment validation |
| `bash deploy/azure/validation/test-service-env-composition.sh` | PASS — app/job environment and secret composition |
| `bash deploy/azure/validation/test-deploy-image-selection.sh` | PASS — mocked regression suite |
| `bash deploy/azure/validation/test-runtime-power.sh` | PASS — mocked regression suite |
| `bash deploy/azure/validation/test-runtime-readiness.sh` | PASS — mocked regression suite |
| `bash deploy/azure/validation/test-preflight.sh` | PASS — mocked regression suite |
| `bash tests/performance/test-gateway-exemption.sh` | PASS — mocked regression suite |
| Shell syntax / changed Python compilation | PASS — 47 deploy shell scripts; 3 changed Python files |
| Unfiltered actionlint 1.7.12 | FAIL — existing `queue` schema limitations and SC2129 style findings |
| Qualified actionlint | PASS — only `unexpected key "queue"` and `SC2129` excluded; no additional findings |
| Live tests of these uncommitted changes | NOT EXECUTED — no dispatch/deployment authorized |
| Azure federation/RBAC inventory | NOT EXECUTED — Azure CLI absent; no Azure login performed |
| Fresh full .NET/isolated Kafka/coverage suites | NOT EXECUTED in TASK 02 — no application, fixture or test-runner changes; published baseline evidence below is separate |

Read-only [published run 38153663471](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38153663471), at the exact baseline SHA above, completed successfully on 2026-10-11. Job/step metadata confirms successful full CI, isolated Kafka, source eligibility, application Azure OIDC login, runtime restoration, automatic smoke and both final restoration safeguards. Image publication was skipped for unchanged sources; required CI/Kafka were successful. This run predates the local fixes and cannot validate them. Production health, federation inventory and infrastructure's current cloud state were not probed.

## Files changed and review plan

| File | Change |
|---|---|
| `deploy/azure/scripts/release-policy.py` | Explicit manual-operation authorization mode, current-head GET, accurate operational reporting, external-error redaction |
| `.github/workflows/azure-runtime-power.yml` | Immutable checkout and source gate before credential-bearing steps |
| `.github/workflows/azure-runtime-session.yml` | Same source gate; restoration unchanged |
| `.github/workflows/azure-performance-k6-reusable.yml` | Same checkout/source gate; default-empty automatic deployment evidence input |
| `.github/workflows/backend-deploy-production.yml` | Pass actual successful deployment result to automatic smoke; trigger unchanged |
| `deploy/azure/validation/test_release_policy.py` | Five offline authorization, read-only API and error-redaction tests |
| `deploy/azure/validation/test_workflow_release_gates.py` | Six offline job authorization/workflow/source/required-Kafka/locking tests |
| `docs/devops/configuration/GITHUB_ENVIRONMENTS.md` | Clarify temporary integration scope and supersede earlier mandatory integration-settings guidance |
| `docs/devops/CICD_SECURITY_VERIFICATION.md` | This audit, actual evidence, limitations and review plan |

Review the local diff and offline evidence first. Retain main protection and the existing Azure identity/federation. After owner approval and normal publication, observe CI for the published SHA; use the existing approved workflow process if live validation is desired. A later reviewed transition can move production triggers/policy to main when integration is merged. This task performs none of those publication or deployment steps.

## Optional future hardening

These are proposals, not current acceptance blockers or settings changes:

- Add the isolated Kafka check to permanent main protection when its final workflow check name is established; preserve existing main rules.
- Review production Environment reviewers and an allowlist that accommodates approved main/manual and temporary integration consumers. Do not apply an integration-only restriction that breaks approved main operations.
- Inventory all Azure federation/RBAC and old branch workflow copies, then consider narrower identities/roles through a separate approved change.
- Pin third-party Actions to reviewed commit SHAs with an update process; update actionlint for current concurrency syntax.
- Keep credentials out of arbitrary application/remote logs and inspect any uploaded performance/recovery artifacts for accidental sensitive values.

No permanent integration ruleset is proposed.
