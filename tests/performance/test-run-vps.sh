#!/usr/bin/env bash
# Mocked SSH/SCP/k6 regression: tests VPS transfer, secret handling, report
# retrieval and cleanup on BOTH passing and failing performance thresholds.
set -Eeuo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin" "$work/out"
cat > "$work/bin/ssh" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
while (($#)); do
  case "$1" in
    -i|-o|-p|-P) shift 2;;
    -*) shift;;
    *) shift; break;;
  esac
done
bash -c "$*"
SH
cat > "$work/bin/scp" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
while (($#)); do
  case "$1" in
    -i|-o|-p|-P) shift 2;;
    -q|-r|-p) shift;;
    -*) shift;;
    *) break;;
  esac
done
[[ $# == 2 ]] || exit 2
src="$1" dst="$2"
[[ "$src" == *@*:* ]] && src="${src#*:}"
[[ "$dst" == *@*:* ]] && dst="${dst#*:}"
if [[ "$src" == */. ]]; then
  mkdir -p "$dst"
  cp -a "${src%/.}/." "$dst/"
else
  cp -a "$src" "$dst"
fi
SH
cat > "$work/bin/k6" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
[[ "${K6_SUPERVISOR_PASSWORD:-}" == synthetic-supervisor-password ]] || exit 4
[[ "${K6_STUDENT_PASSWORD:-}" == synthetic-student-password ]] || exit 4
[[ -f "${!#}" ]] || exit 4
mkdir -p "$K6_REPORT_DIR"
echo '# Fake k6 report (test stub)' > "$K6_REPORT_DIR/${K6_PROFILE}-report.md"
echo '{"passed":true}' > "$K6_REPORT_DIR/${K6_PROFILE}-summary.json"
if [[ "${K6_SHOULD_FAIL:-false}" == true ]]; then
  echo 'Simulated threshold failure' >&2
  exit 5
fi
printf 'Simulated k6 PASS\n'
SH
cat > "$work/bin/ssh-keyscan" <<'SH'
#!/usr/bin/env bash
printf 'example.test ssh-ed25519 SYNTHETIC\n'
SH
chmod +x "$work/bin/ssh" "$work/bin/scp" "$work/bin/k6" "$work/bin/ssh-keyscan"
export PATH="$work/bin:$PATH"
export SSH_HOST=example.test SSH_PORT=2222 SSH_USER=k6runner SSH_PRIVATE_KEY='synthetic-ssh-key'
export K6_RATE_LIMIT_BYPASS_ENABLED=true
export K6_BASE_URL=https://api.example.test
export K6_PROJECT_ID=01234567-89ab-cdef-0123-456789abcdef
export K6_PERFORMANCE_TOKEN='this-is-synthetic-only-secret-at-least-32-bytes'
export K6_SUPERVISOR_EMAIL=supervisor@example.test K6_SUPERVISOR_PASSWORD=synthetic-supervisor-password
export K6_STUDENT_EMAIL=student@example.test K6_STUDENT_PASSWORD=synthetic-student-password
export K6_PROFILE=smoke
export GITHUB_RUN_ID=98653210 GITHUB_RUN_ATTEMPT=1
export K6_LOCAL_REPORT_DIR="$work/out"
runner="$repo/scripts/performance/run-vps.sh"
"$runner" >/dev/null
[[ -f "$work/out/smoke-report.md" ]] || { echo 'Missing retrieved report' >&2; exit 1; }
[[ ! -d /tmp/researchtrack-k6-98653210-1 ]] || { echo 'Remote directory was not deleted' >&2; exit 1; }
if grep -r 'synthetic-supervisor-password' "$work/out"; then
  echo 'Credentials appeared in test artifacts.' >&2; exit 1
fi
export K6_SHOULD_FAIL=true
export GITHUB_RUN_ID=98653211
if "$runner" >/dev/null 2>&1; then
  echo 'A k6 failure was incorrectly accepted.' >&2
  exit 1
fi
[[ -f "$work/out/smoke-report.md" ]] || { echo 'Failed run did not preserve report' >&2; exit 1; }
[[ ! -d /tmp/researchtrack-k6-98653211-1 ]] || { echo 'Failed run left credentials on the VPS' >&2; exit 1; }
echo 'Mocked VPS k6 PASS/fail/report/cleanup tests passed.'
