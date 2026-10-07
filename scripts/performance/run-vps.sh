#!/usr/bin/env bash
# GitHub runner side. Invoked inside runtime-session.sh AFTER Azure readiness.
# Uploads k6 source + short-lived credentials to the approved, pinned VPS,
# executes it, downloads reports, and deletes remote temporary material.
set -Eeuo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
local_reports="${K6_LOCAL_REPORT_DIR:-$repo/reports/performance}"
mkdir -p "$local_reports"

required=(SSH_HOST SSH_PORT SSH_USER SSH_PRIVATE_KEY K6_BASE_URL
  K6_PROJECT_ID K6_SUPERVISOR_EMAIL K6_SUPERVISOR_PASSWORD
  K6_STUDENT_EMAIL K6_STUDENT_PASSWORD K6_PROFILE)
for name in "${required[@]}"; do
  [[ -n "${!name:-}" ]] || { echo "Missing required environment key: $name" >&2; exit 2; }
done
[[ "${K6_RATE_LIMIT_BYPASS_ENABLED:-false}" =~ ^(true|false)$ ]] || {
  echo 'K6_RATE_LIMIT_BYPASS_ENABLED must be true or false.' >&2; exit 2;
}
if [[ "${K6_RATE_LIMIT_BYPASS_ENABLED:-false}" == true && -z "${K6_PERFORMANCE_TOKEN:-}" ]]; then
  echo 'K6_PERFORMANCE_TOKEN is required while exemption is enabled.' >&2; exit 2
fi
[[ "${K6_PROFILE}" =~ ^(smoke|load|stress|spike|write|lifecycle|webhook)$ ]] || { echo 'Invalid K6_PROFILE.' >&2; exit 2; }
[[ "$K6_BASE_URL" =~ ^https://[a-zA-Z0-9.-]+(/)?$ ]] || { echo 'K6_BASE_URL must be an HTTPS origin.' >&2; exit 2; }
[[ "$K6_PROJECT_ID" =~ ^[a-fA-F0-9-]{36}$ ]] || { echo 'K6_PROJECT_ID must be a UUID.' >&2; exit 2; }
# Report path and SSH upload destination contain only GitHub numerical run IDs.
run_id="${GITHUB_RUN_ID:-manual}"
run_attempt="${GITHUB_RUN_ATTEMPT:-1}"
[[ "$run_id" =~ ^([0-9]+|manual)$ && "$run_attempt" =~ ^[0-9]+$ ]] || exit 2
remote_dir="/tmp/researchtrack-k6-${run_id}-${run_attempt}"
# Use the exact Test CI repository SSH secrets/host-key discovery flow.
source "$repo/scripts/performance/ssh-common.sh"
trap k6_ssh_cleanup EXIT  # also clean keys if host-key discovery/SSH setup fails
k6_ssh_prepare
remote="$k6_ssh_remote"
ssh_args=("${k6_ssh_args[@]}")
scp_args=("${k6_scp_args[@]}")
work="$(mktemp -d)"
remote_created=false
cleanup() {
  local code=$?
  trap - EXIT
  if $remote_created; then
    ssh "${ssh_args[@]}" "$remote" "rm -rf -- '$remote_dir'" >/dev/null 2>&1 || true
  fi
  rm -rf "$work"
  k6_ssh_cleanup
  exit "$code"
}
trap cleanup EXIT
chmod 700 "$work"

# The config is a temporary shell environment file escaped by shlex.quote.
# Never put credentials in command-line arguments, workflow output or artifacts.
RUN_ENV_PATH="$work/run.env" python3 - <<'PY'
import base64, hashlib, hmac, json, os, pathlib, secrets, shlex, time
keys = ('K6_BASE_URL', 'K6_PROJECT_ID', 'K6_PERFORMANCE_TOKEN',
        'K6_SUPERVISOR_EMAIL', 'K6_SUPERVISOR_PASSWORD',
        'K6_STUDENT_EMAIL', 'K6_STUDENT_PASSWORD',
        'K6_PROFILE', 'K6_INCLUDE_INTEGRATIONS', 'K6_P95_MS',
        'K6_ENABLE_WRITES', 'K6_STUDENT_ID', 'K6_ENABLE_WEBHOOK',
        'K6_JIRA_PROJECT_KEY', 'K6_JIRA_ISSUE_KEY', 'K6_JIRA_ISSUE_ID',
        'K6_RATE_LIMIT_BYPASS_ENABLED')
values = {k: os.environ.get(k, '') for k in keys}
if values['K6_RATE_LIMIT_BYPASS_ENABLED'] != 'true':
    values['K6_PERFORMANCE_TOKEN'] = ''  # no need to ship exemption secret when disabled
if values['K6_PROFILE'] == 'webhook':
    secret = os.environ.get('K6_JIRA_WEBHOOK_CLIENT_SECRET', '')
    if not secret or not values['K6_JIRA_PROJECT_KEY'] or not values['K6_JIRA_ISSUE_KEY']:
        raise SystemExit('Webhook mode requires webhook client secret and test Jira project/issue IDs')
    def b64(data):
        return base64.urlsafe_b64encode(json.dumps(data, separators=(',', ':')).encode()).decode().rstrip('=')
    head = b64({'alg': 'HS256', 'typ': 'JWT'})
    payload = b64({'iat': int(time.time()), 'exp': int(time.time()) + 900,
                   'jti': secrets.token_hex(12)})
    to_sign = f'{head}.{payload}'
    sig = base64.urlsafe_b64encode(hmac.new(secret.encode(), to_sign.encode(), hashlib.sha256).digest()).decode().rstrip('=')
    values['K6_JIRA_WEBHOOK_BEARER'] = f'{to_sign}.{sig}'
path = pathlib.Path(os.environ['RUN_ENV_PATH'])
path.write_text(''.join(f'export {key}={shlex.quote(value)}\n' for key, value in values.items()), encoding='utf-8')
path.chmod(0o600)
PY

ssh "${ssh_args[@]}" "$remote" "umask 077; mkdir -m 700 -p '$remote_dir' '$remote_dir/tests'"
remote_created=true
# Only scripts and the generated transient environment are uploaded.
scp -q "${scp_args[@]}" -r "$repo/tests/performance" "$remote:$remote_dir/tests/"
scp -q "${scp_args[@]}" "$repo/scripts/performance/run-remote.sh" "$remote:$remote_dir/run-remote.sh"
scp -q "${scp_args[@]}" "$work/run.env" "$remote:$remote_dir/run.env"

rc=0
ssh "${ssh_args[@]}" "$remote" "bash '$remote_dir/run-remote.sh'" || rc=$?
# Always fetch evidence from a test failure; report copying failures independently.
mkdir -p "$local_reports"
scp -q "${scp_args[@]}" -r "$remote:$remote_dir/reports/." "$local_reports/" || {
  echo 'Could not download complete k6 reports from the VPS.' >&2
  ((rc == 0)) && rc=1
}
if ssh "${ssh_args[@]}" "$remote" "rm -rf -- '$remote_dir'"; then
  remote_created=false
else
  # EXIT trap attempts removal once more if the first cleanup SSH failed.
  echo 'WARNING: Remote temporary directory cleanup failed; retrying during exit.' >&2
  ((rc == 0)) && rc=1
fi
exit "$rc"
