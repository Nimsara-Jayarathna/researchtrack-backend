#!/usr/bin/env bash
# Fully offline source-level checks. Actual k6/Azure/VPS execution is manual.
set -Eeuo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
command -v node >/dev/null || { echo 'Node.js is required to validate k6 scripts.' >&2; exit 2; }
for script in "$repo"/scripts/performance/*.sh "$repo/tests/performance/validate.sh"; do
  bash -n "$script"
done
while IFS= read -r -d '' file; do
  node --input-type=module --check < "$file" || {
    echo "Invalid JavaScript in $file" >&2
    exit 1
  }
done < <(find "$repo/tests/performance" -name '*.js' -print0)
RESEARCHTRACK_ROOT="$repo" python3 - <<'PY'
import os, pathlib, re
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
for profile in ('smoke', 'load', 'stress', 'spike', 'write', 'lifecycle', 'webhook'):
    assert (p/f'{profile}.js').exists(), f'missing {profile}.js'
assert 'RateLimiting__PerformanceTest__Enabled=false' in (root/'config/env/gateway/.env.example').read_text()
assert 'runtime-session.sh' in (root/'.github/workflows/azure-performance-k6.yml').read_text()
assert 'run-gateway-session.sh' in (root/'.github/workflows/azure-performance-k6.yml').read_text()
assert 'RateLimiting:PerformanceTest:Token' in (root/'src/Gateway/ResearchTrack.Gateway/PerformanceRateLimitPolicy.cs').read_text()
assert 'PerformanceRateLimitPolicy.AuthorizationHeader' in (root/'src/Gateway/ResearchTrack.Gateway/Program.cs').read_text()
assert 'K6_JIRA_WEBHOOK_CLIENT_SECRET' in (root/'scripts/performance/run-vps.sh').read_text()
workflow=(root/'.github/workflows/azure-performance-k6.yml').read_text()
assert "vars.K6_RATE_LIMIT_BYPASS_ENABLED" in workflow
assert 'run-gateway-session.sh' in workflow
assert 'discover-vps-ip.sh' in workflow
assert 'gateway-exemption.sh capture' in workflow
assert 'gateway-exemption.sh restore' in workflow
for key in ('SSH_HOST','SSH_PORT','SSH_USER','SSH_PRIVATE_KEY'):
    assert 'secrets.'+key in workflow, f'missing existing SSH secret {key}'
for removed in ('K6_VPS_HOST','K6_VPS_USER','K6_VPS_SSH_KEY','K6_VPS_KNOWN_HOSTS'):
    assert removed not in workflow and removed not in (root/'scripts/performance/run-vps.sh').read_text(), f'old duplicate VPS setting {removed}'
assert 'containerapp revision copy' in (root/'scripts/performance/gateway-exemption.sh').read_text()
assert 'K6_RATE_LIMIT_BYPASS_ENABLED' in (root/'tests/performance/lib/settings.js').read_text()
print('Performance source and workflow contracts passed.')
PY
