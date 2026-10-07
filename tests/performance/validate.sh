#!/usr/bin/env bash
# Fully offline source-level checks. Actual k6/Azure/VPS execution is manual or
# automatic only after a successful Production deployment when explicitly enabled.
set -Eeuo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
command -v node >/dev/null || { echo 'Node.js is required to validate k6 scripts.' >&2; exit 2; }
for script in "$repo"/scripts/performance/*.sh "$repo/tests/performance"/*.sh; do
  bash -n "$script"
done
while IFS= read -r -d '' file; do
  node --input-type=module --check < "$file" || {
    echo "Invalid JavaScript in $file" >&2
    exit 1
  }
done < <(find "$repo/tests/performance" -name '*.js' -print0)
RESEARCHTRACK_ROOT="$repo" python3 - <<'PY'
import os, pathlib
root = pathlib.Path(os.environ['RESEARCHTRACK_ROOT'])
p = root / 'tests/performance'
suite = (p/'suite.js').read_text()
required = {
    'projects': '/api/v1/projects',
    'dashboard': '/api/v1/supervisor/dashboard',
    'submissions': '/submissions/requirements',
    'jira': '/jira/issues',
    'github': '/github/activity',
}
for name, route in required.items():
    assert route in suite, f'{name} route missing in suite'
profiles = ('smoke', 'load', 'stress', 'spike', 'write', 'lifecycle', 'webhook')
for profile in profiles:
    assert (p/f'{profile}.js').exists(), f'missing {profile}.js'
assert not (p/'calibration.js').exists(), 'calibration profile should not exist; baseline uses the real profiles'
assert 'RateLimiting__PerformanceTest__Enabled=false' in (root/'config/env/gateway/.env.example').read_text()
assert 'RateLimiting:PerformanceTest:Token' in (root/'src/Gateway/ResearchTrack.Gateway/PerformanceRateLimitPolicy.cs').read_text()
assert 'PerformanceRateLimitPolicy.AuthorizationHeader' in (root/'src/Gateway/ResearchTrack.Gateway/Program.cs').read_text()

manual = (root/'.github/workflows/azure-performance-k6.yml').read_text()
reusable = (root/'.github/workflows/azure-performance-k6-reusable.yml').read_text()
deploy = (root/'.github/workflows/backend-deploy-production.yml').read_text()
assert 'azure-performance-k6-reusable.yml' in manual
assert 'workflow_call:' in reusable
assert 'runtime-session.sh' in reusable and 'run-gateway-session.sh' in reusable
assert 'discover-vps-ip.sh' in reusable
assert 'gateway-exemption.sh capture' in reusable and 'gateway-exemption.sh restore' in reusable
assert 'automatic-post-deploy' in reusable
assert 'Automatic post-deployment k6 Smoke' in deploy
assert "vars.K6_AUTO_SMOKE_ENABLED == 'true'" in deploy
assert 'needs: deploy' in deploy
assert 'profile: smoke' in deploy
assert 'rate_limit_bypass_enabled: false' in deploy
assert 'azure-performance-k6-reusable.yml' in deploy
for key in ('SSH_HOST','SSH_PORT','SSH_USER','SSH_PRIVATE_KEY'):
    assert 'secrets.'+key in reusable, f'missing existing SSH secret {key}'
for removed in ('K6_VPS_HOST','K6_VPS_USER','K6_VPS_SSH_KEY','K6_VPS_KNOWN_HOSTS'):
    assert removed not in reusable and removed not in (root/'scripts/performance/run-vps.sh').read_text(), f'old duplicate VPS setting {removed}'
assert 'containerapp revision copy' in (root/'scripts/performance/gateway-exemption.sh').read_text()
assert 'K6_RATE_LIMIT_BYPASS_ENABLED' in (root/'tests/performance/lib/settings.js').read_text()

profiles_source=(root/'tests/performance/lib/profiles.js').read_text()
for key in ('K6_SMOKE_P95_MS','K6_LOAD_P95_MS','K6_STRESS_P95_MS','K6_SPIKE_P95_MS',
            'K6_WRITE_P95_MS','K6_LIFECYCLE_P95_MS','K6_WEBHOOK_P95_MS'):
    assert key in profiles_source and key in reusable, f'missing runtime threshold wiring for {key}'
assert 'K6_ENDPOINT_P95_THRESHOLDS_JSON' in profiles_source
assert 'K6_ENDPOINT_P95_THRESHOLDS_JSON' in reusable
assert 'jira_issues' in profiles_source
assert "duration: '45s'" in profiles_source, 'Smoke should collect a larger baseline sample'
for stat in ("'p(90)'", "'p(95)'", "'p(99)'"):
    assert stat in profiles_source, f'missing detailed trend stat {stat}'

runner=(root/'scripts/performance/run-vps.sh').read_text()
for key in ('K6_SMOKE_P95_MS','K6_LOAD_P95_MS','K6_STRESS_P95_MS','K6_SPIKE_P95_MS',
            'K6_WRITE_P95_MS','K6_LIFECYCLE_P95_MS','K6_WEBHOOK_P95_MS',
            'K6_ENDPOINT_P95_THRESHOLDS_JSON','K6_EXECUTION_MODE'):
    assert key in runner, f'VPS environment transfer missing {key}'
assert 'K6_JIRA_WEBHOOK_CLIENT_SECRET' in runner

report=(root/'tests/performance/lib/report.js').read_text()
metrics=(root/'tests/performance/lib/metrics.js').read_text()
assert 'Endpoint performance' in report and '-report.html' in report and '-endpoints.csv' in report
assert 'Runtime threshold configuration' in report
assert 'Jira issues diagnostic' in report
assert 'schemaVersion: 3' in report
assert 'researchtrack_op_' in metrics and 'operationMetrics' in metrics
assert 'K6_WARMUP_PASSES' in (root/'tests/performance/lib/settings.js').read_text()
assert 'warmUp' in suite
print('Performance source, runtime thresholds, reporting and automatic-Smoke workflow contracts passed.')
PY
