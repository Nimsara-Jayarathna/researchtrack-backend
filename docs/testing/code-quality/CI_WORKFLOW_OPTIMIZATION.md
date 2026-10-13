# ResearchTrack CI / CD workload optimization — October 2026

## Objective and compatibility

Reduce GitHub Actions minutes **without changing production service logic, other students' test suites, or the exact-commit production release policy**. No new secrets or environment variables are required. Existing `production` / `test` environments remain unchanged.

The optimization distinguishes a complete validated release from a PR feedback loop. The Phase 4 update can reuse **GitHub-verified identical-tree evidence**, but any missing, untrusted or ambiguous evidence forces `backend-ci.yml` to run the complete build, unit/coverage and Kafka suite. The exact SHA is always validated against the current trusted `main` before production credentials. For full Phase 4 details and the mandatory same-SHA SonarQube release gate, see [`RELEASE_EVIDENCE_ORCHESTRATION.md`](RELEASE_EVIDENCE_ORCHESTRATION.md). Azure runtime restoration, image fingerprints and performance/mutation implementations remain unchanged.

## Executions

| Event | CI (build/unit/Kafka) | Native quality | SonarQube | Deployment |
|---|---|---|---|---|
| PR to main/develop | Full build/unit/Kafka; relevant DevOps script checks | CSharpier + Roslyn, no duplicate production builds | No automatic PR scan | No |
| Update same PR | Prior PR CI and native-quality runs cancelled; latest validated | Latest revision only | No | No |
| Merge C# code into main | Reuse independently verified identical-tree CI evidence, otherwise rebuild/test exact main SHA | No redundant push quality | Yes, build-aware and enforced gate | Release after successful Sonar gate |
| Merge docs and quality-only config | No automatic post-merge full CI | No | Skipped after small read-only classifier | Skipped before Azure access |
| Merge unit-test-only | No automatic release CI | No | Yes (coverage input changed) | Skipped |
| Merge deploy/runtime inputs | Exact SHA validation is required before production access | No | Conservative unless positively known irrelevant | Yes |
| Merge into develop | PR validation only | No push rerun | No | Test deployment only for relevant changes |
| Manual Sonar/production/test invocation | Release validation preserved for production | Manual native quality works | Manual scan always runs | Manual deployment always runs |
| Unknown file, deleted path, invalid/missing base, force-push | Conservative / run | Standard PR checks | Run | Deploy / validate |

On a pull request, `Backend CI` still executes the real .NET build, unit and coverage tests, and required isolated Kafka integration. Only the independent **offline DevOps validation scripts** are conditional on the changed file set. On release, all existing checks remain mandatory.

## Classification implementation

`deploy/automation/change-scope.py` is stdlib-only. It needs no token, network, external service or secret. Its Git history checks require two valid commit IDs, both objects present, an ancestral base, and a nonempty NUL-delimited diff. Missing, ambiguous, unsupported events, or unknown paths **always run** the work; only positively classified irrelevant changes may skip it. It writes `execute=true|false` to `GITHUB_OUTPUT` and a reason to the job summary.

* `--mode devops`: only PRs can omit offline DevOps checks; release validation ALWAYS runs all of them.
* `--mode application`: only documented irrelevant paths can skip production/Test deployment. It runs *before* reusable workflows, registry access, secrets and Azure/VPS setup. Deployment, monitoring and runtime configuration changes are handled conservatively; infrastructure-specific changes are owned by the separately triggered infrastructure reconciler.
* `--mode sonar`: only clearly non-analysis files can skip a main-branch cloud scan. C# source, unit tests, shared SDK/project settings and unknown paths cause scanning. Manual scan always runs.

No top-level `on: ... paths` are added to PR-required checks. `Backend CI` and `Backend Code Quality` also support `merge_group` for repositories using merge queues.

## Native quality responsibilities

* `bash ./scripts/code-quality.sh check-style ci`: **CI** formatting + IDE0005/IDE0065 verification, restores the 8 production projects for Roslyn's workspace, and produces artifacts. Does **not** invoke another 8 project builds; Backend CI owns compilation/unit testing on PR.
* `bash ./scripts/code-quality.sh check local`: original full formatting + Roslyn + all eight project builds; unchanged for developers/diagnostics.
* `bash ./scripts/code-quality.sh fix`: remains available.
* `bash ./scripts/code-quality.sh analyze`: remains available.

## Fail-closed CI evidence reuse and full-CI fallback

A PR's head SHA and a squash-merged `main` SHA can differ. We do not suppress release CI merely because a PR was green. The read-only `release-evidence.py` requires source-content equivalence and individually successful mandatory checks from trusted GitHub runs. Any ambiguity runs the unchanged full Backend CI, including Kafka. All paths still pass through exact current-main-SHA eligibility before production credentials. The same-SHA SonarQube gate is required before image building for analysis-relevant deployments. See the Phase 4 operational guide.

Production Azure and infrastructure reconciliation both use the same attested release-validation workflow. If previously successful same-SHA release validation is available, the second run may reuse it; simultaneous cache misses may still repeat full CI. We prioritize fail-closed validation over unverified minutes savings.

## Practical verification

```bash
python3 deploy/automation/test_change_scope.py
python3 deploy/azure/validation/test_release_policy.py
python3 deploy/azure/validation/test_workflow_release_gates.py
bash ./scripts/code-quality.sh self-test
```

Run actual PR and merge scenarios in GitHub Actions before treating these as fully verified, including the release-evidence regression suite. Record run IDs, skip reasons, branch-protection requirements, job duration, and deployed image tags. CSharpier/Roslyn are mandatory PR checks; verify required-check names in GitHub branch protection. Do not require a push-only workflow as a PR gate.

**Expected behavior:** a docs/CI-only merge has lightweight change-detection jobs, a successful skipped deployment, and no Azure/VPS credentials or cloud scan; code changes still run the same trusted release pipeline. A `workflow_dispatch` release still fully validates before deployment.

**Known constraint:** the Azure production workflow's `release_summary` summarizes actual deployments; on a verified no-op it is intentionally skipped (the change classifier already records the no-op in its job summary).

## Azure Infrastructure power-state reconciliation fix

The separate infrastructure workflow now captures its pre-run VM power state, classifies actual Container App running status and distinguishes intentional application shutdown from broken scrape endpoints. It restores the preexisting VM state on success/failure only when it is safe to do so; mixed/unknown states fail closed. See [`AZURE_INFRASTRUCTURE_PAUSED_RUNTIME.md`](AZURE_INFRASTRUCTURE_PAUSED_RUNTIME.md) for the state matrix and offline tests.
