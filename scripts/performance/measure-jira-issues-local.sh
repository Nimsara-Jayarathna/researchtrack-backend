#!/usr/bin/env bash
# Disposable MySQL and loopback HTTP only. Does not load runtime env files.
set -Eeuo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
for binary in docker dotnet python3 openssl; do
  command -v "$binary" >/dev/null || { echo "NOT EXECUTED: missing $binary" >&2; exit 2; }
done
docker info >/dev/null 2>&1 || { echo 'NOT EXECUTED: Docker Desktop is unavailable.' >&2; exit 2; }
[[ -z "${DOCKER_HOST:-}" || "$DOCKER_HOST" == unix://* ]] || exit 2
[[ "$(docker context inspect --format '{{.Endpoints.docker.Host}}')" == unix://* ]] || exit 2
python3 - <<'PY'
import socket
for port in (13308, 19114, 19115):
    with socket.socket() as probe:
        try: probe.bind(('127.0.0.1', port))
        except OSError: raise SystemExit(f'Refusing occupied local fixture port {port}.')
PY
umask 077
work="$(mktemp -d "${TMPDIR:-/tmp}/researchtrack-jira-perf.XXXXXX")"
project="rt-jira-perf-$(openssl rand -hex 6)"
evidence="$root/artifacts/jira-issues-performance/$project"
mkdir -p "$evidence"
password="$(openssl rand -hex 24)"
printf 'MYSQL_ROOT_PASSWORD=%s\nMYSQL_DATABASE=researchtrack_test_jira_perf\nMYSQL_USER=fixture\nMYSQL_PASSWORD=%s\n' "$password" "$password" > "$work/mysql.env"
container_id=""
cleanup() {
  [[ -z "$container_id" ]] || docker stop "$container_id" >/dev/null 2>&1 || true
  printf 'project=%s\ncontainer=%s\nvolume=%s-mysql\nprivate_work=%s\n' "$project" "$container_id" "$project" "$work" > "$evidence/fixture-location.txt"
  echo "RETAINED: project=$project evidence=$evidence (fixture data preserved)"
}
trap cleanup EXIT
container_id="$(docker run -d --name "$project" --label researchtrack.fixture=jira-issues-perf \
  --env-file "$work/mysql.env" --publish 127.0.0.1:13308:3306 \
  --mount "type=volume,src=$project-mysql,dst=/var/lib/mysql" \
  --memory 1g --cpus 2 mysql:8.4 --innodb-buffer-pool-size=134217728 --max-connections=64)"
for ((attempt=0;attempt<90;attempt++)); do
  # The entrypoint's temporary socket-only initialization server can answer ping.
  # Wait for the final TCP server and the actual fixture user's grants instead.
  if docker exec "$container_id" sh -c 'MYSQL_PWD="$MYSQL_PASSWORD" mysql --protocol=TCP -h 127.0.0.1 -u fixture "$MYSQL_DATABASE" -Nse "SELECT 1"' >/dev/null 2>&1; then break; fi
  [[ "$attempt" != 89 ]] || { echo 'FAIL: isolated MySQL did not become ready.' >&2; exit 1; }
  sleep 2
done
export RT_JIRA_PERF_ENABLE=1 RT_JIRA_PERF_CONTAINER_ID="$container_id" RT_JIRA_PERF_OUTPUT="$evidence" RT_JIRA_PERF_ROOT="$root"
export RT_JIRA_PERF_CONNECTION="Server=127.0.0.1;Port=13308;Database=researchtrack_test_jira_perf;User=fixture;Password=$password;SslMode=Required;MaximumPoolSize=32;ConnectionTimeout=5;"
env -u JIRA_SERVICE_URL -u PROJECT_SERVICE_URL -u AUTH_SERVICE_URL \
  dotnet run --project "$root/tests/performance/jira-issues/JiraIssuesBenchmark.csproj" -c Release > "$evidence/benchmark.log" 2>&1
tail -24 "$evidence/benchmark.log"
