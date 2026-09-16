# ResearchTrack Sprint 2 Operational Monitoring

ResearchTrack uses Prometheus for metrics and alert-state evaluation and Grafana for operational visibility. Sprint 2 deliberately keeps alerting small and investigation-focused; it does not add external paging or notification infrastructure.

## Alert rules

Rules are stored in `deploy/monitoring/rules/researchtrack-alerts.yml` and loaded by Prometheus through `deploy/monitoring/prometheus.yml`.

| Alert | Condition | Recovery |
| --- | --- | --- |
| `ResearchTrackServiceUnavailable` | A non-gateway backend scrape target remains down for 1 minute. | Resolves after the target is scrapeable again. |
| `ResearchTrackApiGatewayUnavailable` | The API Gateway scrape target remains down for 1 minute. | Resolves after the gateway is scrapeable again. |
| `ResearchTrackSustainedServerErrors` | At least 5 HTTP 5xx responses occur in 5 minutes and the condition remains true for 2 minutes. | Resolves when the rolling 5-minute count falls below the threshold. |
| `ResearchTrackGitHubSyncFailures` | At least 2 GitHub sync runs fail within 10 minutes and the condition remains true for 1 minute. | Resolves after failures age out of the 10-minute window. |
| `ResearchTrackGitHubReconciliationFailures` | At least 2 reconciliation cycles fail or partially fail within 30 minutes. | Resolves after failures age out of the 30-minute window. |
| `ResearchTrackGitHubReconciliationMissed` | No reconciliation cycle completes within twice the configured interval, sustained for 5 minutes. | Resolves after a reconciliation cycle completes. |

Alert annotations contain only service/metric context. They do not include GitHub tokens, webhook secrets, private keys, database credentials, JWT signing material, request bodies, or repository content.

## Lightweight GitHub reconciliation

The GitHub service runs a periodic reconciliation safety net in addition to webhook and manual synchronization.

Configure the cadence in `github.env`:

```env
GitHub__SyncIntervalMinutes=15
```

Valid values are 1 through 1440 minutes. A cycle runs immediately when the GitHub service starts and then repeats at the configured interval.

For each active, enabled repository with a connected GitHub App installation, reconciliation:

1. Reads current repository metadata from GitHub to determine the current default branch.
2. Reads only the current HEAD SHA of that default branch.
3. Compares the remote default branch and SHA with the values stored after the most recent successful ResearchTrack synchronization.
4. Does nothing when both values match.
5. Enqueues the existing repository synchronization pipeline with trigger `RECONCILIATION` when either value differs.

`LastKnownHeadSha` is updated only by a successful full synchronization. A failed reconciliation-triggered sync therefore leaves the old SHA in place, causing a later reconciliation cycle to detect the same mismatch and retry naturally.

To reduce GitHub API traffic, links that refer to the same GitHub repository through the same installation share one remote metadata/HEAD check per cycle.

## Reconciliation metrics

- `github_reconciliation_cycles_total{outcome="completed|partial_failure|failed"}`
- `github_reconciliation_checks_total{outcome="unchanged|changed|failed"}`
- `github_reconciliation_repositories_queued_total`
- `github_reconciliation_last_completed_timestamp_seconds`
- `github_reconciliation_last_success_timestamp_seconds`
- `github_reconciliation_interval_seconds`

The existing `github_sync_runs_total{trigger,outcome}` metric records the subsequent full synchronization with `trigger="RECONCILIATION"`.

## Verification

### Backend service unavailable and recovery

Use the test environment only. From the deployment directory:

```bash
docker compose --env-file deploy.env -f compose.yml stop meeting
```

After the scrape interval and 1-minute `for` period, verify `ResearchTrackServiceUnavailable` is firing in Prometheus/Grafana. Restore the service:

```bash
docker compose --env-file deploy.env -f compose.yml start meeting
```

Verify `up{instance="meeting:8080"}` returns to `1` and the alert resolves.

### API Gateway unavailable and recovery

```bash
docker compose --env-file deploy.env -f compose.yml stop gateway
```

Verify `ResearchTrackApiGatewayUnavailable` fires, then restore:

```bash
docker compose --env-file deploy.env -f compose.yml start gateway
```

Verify the alert resolves.

### Sustained HTTP 5xx errors

Use only a controlled test environment and an existing failure scenario that is already known to return HTTP 500; do not add a production failure endpoint for alert testing. Generate at least five 5xx responses within five minutes and keep the rolling condition above the threshold for two minutes. Verify:

```promql
sum by (instance) (increase(http_requests_received_total{code=~"5.."}[5m]))
```

and confirm `ResearchTrackSustainedServerErrors` becomes pending/firing for the affected service. Stop generating the controlled failure and restore the dependency/condition that caused it. The alert clears automatically once the rolling five-minute count drops below five.

If the current test deployment has no safe existing path that can intentionally return 500, verify the metric and loaded rule without introducing an artificial production-only endpoint; record that limitation in the Sprint evidence.

### GitHub synchronization failure and recovery

Use a disposable/test repository and the test GitHub App installation. Temporarily remove that repository from the installation or otherwise make its installation access invalid, then request synchronization twice. Verify failed runs are visible through:

```promql
increase(github_sync_runs_total{outcome="failed"}[10m])
```

and verify `ResearchTrackGitHubSyncFailures` becomes pending/firing. Restore installation access and run a successful sync. The alert resolves once the earlier failed samples age out of the 10-minute window.

Never place tokens, PEM material, webhook secrets, or credentials into alert labels/annotations while testing.

### Reconciliation failure and recovery

For the test GitHub App/repository, temporarily revoke or invalidate repository access and set `GitHub__SyncIntervalMinutes=1` in the isolated test environment. Leave the GitHub service itself running. Two reconciliation cycles with failed remote checks within 30 minutes cause `ResearchTrackGitHubReconciliationFailures` to become pending/firing. Verify the failure counter with:

```promql
increase(github_reconciliation_cycles_total{outcome=~"partial_failure|failed"}[30m])
```

Restore the GitHub App repository access. Confirm subsequent reconciliation cycles complete successfully and `github_reconciliation_last_success_timestamp_seconds` advances. The failure alert resolves automatically after the earlier failed-cycle samples age out of its 30-minute window. Return the test interval to the normal deployment value afterward.

### Reconciliation detects an out-of-band repository change

1. Ensure the linked repository is synchronized and note that `LastKnownHeadSha` matches the default branch HEAD.
2. Push a new commit to the repository's default branch without using ResearchTrack manual refresh.
3. Wait for the configured reconciliation interval (or temporarily use a small safe test interval such as 1 minute).
4. Verify `github_reconciliation_checks_total{outcome="changed"}` increases.
5. Verify `github_reconciliation_repositories_queued_total` increases.
6. Verify a sync run appears with `trigger="RECONCILIATION"` and, after success, the next reconciliation reports the repository as unchanged.

### Missed reconciliation

`ResearchTrackGitHubReconciliationMissed` is intentionally different from service availability. If the entire GitHub service is stopped, `ResearchTrackServiceUnavailable` is the authoritative alert because the reconciliation metrics are no longer scrapeable. The missed-run alert detects a live GitHub service whose reconciliation worker has stopped completing cycles.

For a controlled missed-run test, use an isolated test build or debugger to pause/prevent the reconciliation worker from completing while leaving the GitHub HTTP service and `/metrics` endpoint running. Wait for more than twice `github_reconciliation_interval_seconds` plus the 5-minute alert `for` duration, verify the alert becomes visible, then allow the worker to complete a cycle and verify the alert resolves. Do not add a production failure endpoint solely for this test.

## Prometheus rule verification

Deployment force-recreates Prometheus so the newly uploaded bind-mounted configuration and rule files are loaded without a manual restart. Health verification then checks `/api/v1/rules` for both ResearchTrack rule groups and all six expected alert names, rather than accepting the presence of the group names alone. This prevents an older rule set with the same group names from being mistaken for the current deployment.

Grafana is also force-recreated so startup provisioning uses the current datasource/dashboard tree. Verification checks datasource UID `prometheus` and dashboard UIDs `researchtrack-overview` and `github-sync-operations`. Named monitoring data volumes are retained during recreation.
