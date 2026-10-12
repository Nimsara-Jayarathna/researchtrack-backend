# GitHub Actions failure corrections — 2026-10-12

Target: `integration/main-kafka-production-20261011`. Starting source:
`f252ae5c23a9ddf130d6984816c433e3d6bd2ded`, the owner's merge of main into integration.
The local checkout was fast-forwarded to that existing commit before editing. Main's
cookie security, host lifetime, code-quality/Sonar and other merged features are retained.

## Current failing runs

| Run / job | Confirmed cause | Correction |
|---|---|---|
| [Backend CI 38265761187](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38265761187), ordinary job `114867389191` | CSharpier wraps Jira's existing `AddResearchTrackJwtAuthentication(builder.Configuration)` call over multiple lines. The wiring checker searched for one literal single-line string. Authentication was still registered. | Recognize the same canonical call across whitespace while excluding comments/string literals; retain exact protected-service membership and runtime environment checks. Add offline regression execution to CI. |
| [Linked Kafka job 114867389794](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38265761187/job/114867389794) | Merge discarded nine Jira `datetime(6)` runtime mappings. Provider model reverted to `datetime`; persisted webhook time rounded to whole seconds while the outbox contract retained microseconds. Expected `14:55:30.6357000`, actual `14:55:31.0000000`. GitHub 3 passed; Jira 1 passed/1 failed. | Restore exactly the nine prior mappings without changing migrations, timestamps, event validation, scheduling or offsets. Add nine offline MySQL-model regressions. Keep existing real MySQL exact timestamp assertions. |
| [Code Quality 38265760741](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38265760741), job `114867387097` | Four CSharpier batches fail on 16 Kafka/GitHub/Jira production files. All eight style checks and production builds already pass in the failed job. | Format only the 16 reported files with the existing pinned CSharpier 1.3.0/configuration. No files added to ignore lists; no diagnostic or threshold disabled. |
| [Production 38265755155](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38265755155) | Its required ordinary CI and Kafka jobs fail for the same two causes. Build/deploy/smoke skip afterward. Summary succeeds but the overall run correctly fails. | Fix shared source/checks. Preserve fail-closed gating; do not bypass failed CI, skips or source authorization to reach Azure. |

These are current-source regressions, not evidence of a production broker outage. No
production Kafka restart, topic creation/deletion, offset reset, schema repair, outbox
clearance or automatic destructive rollback is part of the correction.

## Scope and regression evidence

The nine restored fields are webhook `ReceivedAt`/`ProcessedAt`, job `StartedAt`/
`RequestedAt`/`CompletedAt`/`AvailableAt`, and connection `WebhookExpiresAt`/
`LastWebhookAt`/`LastReconciledAt`. Existing migrations already describe the intended
columns. Restoring runtime mapping prevents precision loss in provider parameters and
new disposable schemas; no migration file or existing database is edited.

`tests/ResearchTrack.JiraService.Tests/Persistence/JiraTimestampMappingTests.cs` inspects
the actual MySQL provider model without opening a connection. All nine cases fail before
the correction (`datetime`) and pass afterward (`datetime(6)`). The existing isolated
flow still compares exact MySQL time with the contract and rejects ±1 microsecond; no
rounding/tolerance was introduced to make the test pass.

`deploy/azure/validation/test_shared_auth_wiring.py` uses a private source-only fixture
to verify canonical and formatter-wrapped registration, rejection of missing/different
configuration, rejection of comment/string-only registration and enforcement of the
exact protected service set. Before correction there were four failed assertions across
five methods. After correction all five methods and the actual source wiring checker
pass. This remains a source-contract check, not a whole-program C# authorization proof;
the normal application regression suites validate runtime behavior separately.

For the 15 files with formatting-only changes, imports/body tokens match the original
after normalizing trivia, import order and optional trailing list commas. JiraDbContext
has the deliberate nine-mapping restoration as well. Migrations, EF snapshots, tests,
contracts, dashboards and source outside the reported formatter scope were not rewritten
by the formatter.

Local evidence: `artifacts/actions-failure-fixes-20261012/` (ignored by Git).

| Executed check | Result |
|---|---|
| MySQL runtime model before / after | FAIL 9/9 → PASS 9/9, zero skips |
| JWT wiring before / after | FAIL on wrapped call/comment-only false positives → PASS 5 methods and real checker |
| Full offline Azure Python regression discovery | PASS 103 methods, zero skips |
| Existing complete code-quality check | PASS on 426 eligible production C# files; all eight style checks and production builds pass |
| Environment composition and Azure environment validators | PASS; synthetic values only |
| JWT checker shell syntax / ShellCheck | PASS |
| Workflow validation | Qualified actionlint PASS; existing `queue` schema incompatibility excluded, not represented as a clean raw lint pass |
| Real isolated Kafka/MySQL suite | PASS GitHub 3/3 and Jira 2/2, zero skips; exact MySQL timestamps, broker outage/retry/commit recovery, business results, exporter/topic/group postflight; fixture `rt-kafka-fixture-b7cb858e3ea5` |
| Full ordinary .NET regression/coverage | PASS 867, zero failures, five conditional Kafka skips separately executed by the real fixture; business coverage 74.7% line / 63.8% branch, unchanged 65/50 floors |
| Release solution build in the isolated runner | PASS zero warnings/errors |
| Fresh Kafka observability / operational safety | PASS 159 checks / 33 scenarios / 55 expressions; 62 shell safety cases and 8 Python methods |

The new source has the following changes in addition to this report:

- `.github/workflows/backend-ci.yml`: run the new offline JWT regressions before the real wiring check.
- `deploy/validate-shared-auth-wiring.sh` and `deploy/azure/validation/test_shared_auth_wiring.py`: formatting-aware validation and regressions.
- `tests/ResearchTrack.JiraService.Tests/Persistence/JiraTimestampMappingTests.cs`: nine provider-model regressions.
- `src/Services/ResearchTrack.JiraService/Persistence/JiraDbContext.cs`: restore nine timestamp mappings and apply existing formatting.
- Formatting only in six shared Kafka files (`KafkaClients`, `KafkaMessagingWorkers`, `KafkaMetrics`, `KafkaPersistence`, `KafkaRuntimeOptions`, `KafkaWebhookContract`); GitHub `GitHubKafkaEventHandler`, `GitHubWebhookDeliveryStore`, `GitHubWebhookIngressService`, `GitHubDbContext`, `Program`; Jira `JiraKafkaEventHandler`, `JiraSyncScheduler`, `JiraWebhookService`, `Program`.

The ordinary tests include the new mappings and preserved main Auth cookie regressions.
The five conditional skips are not counted as ordinary passes. Formatting changes the
line-based coverage denominator; no gate was relaxed and no application code was
removed from the existing coverage scope. Protected database integration suites outside
the disposable Kafka fixture were not executed.

## Historical failures and remaining configuration

Earlier runs `38224173196` (infrastructure), `38224173326` (production), `38224281397`
(all mutations), `38224287109` (runtime status), `38224292646` (runtime session) and
`38224298725` (k6 smoke) at `1f6efd9a169b5803d7b2fad0dcf4e81e6b9b7962` succeeded.
Older main quality/Sonar failures were followed by successful later main runs; cancelled
runs are not test failures. Do not rerun superseded broken SHAs or erase failure history
to make the Actions list appear green.

[Test deployment 38224276717](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38224276717)
is a distinct configuration failure: `test/GATEWAY_ENV_FILE` lacks the required
performance-test keys. The Test secret metadata still reports its prior update time,
and the supplied complete local `gateway.env` does not exist. Its contents cannot be
read through the GitHub secret API. No secret was replaced with a partial/example file.

The owner must supply the full authoritative Test file or update the full secret while
preserving existing values. The intended disabled settings are:

```env
RateLimiting__PerformanceTest__Enabled=false
RateLimiting__PerformanceTest__AllowedIp=0.0.0.0
RateLimiting__PerformanceTest__Token=DISABLED
```

Avoid duplicate keys. Once the complete Test secret is corrected, rerun its failed
deployment jobs on the accepted source. This repository correction does not weaken the
canonical contract, change production secrets, alter OIDC/Environment/concurrency/
triggers, or run heavy performance/production chaos scenarios.
