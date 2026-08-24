#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

set -a
. ./deploy.env
set +a

case "${DEPLOY_ENV:-}" in
  test|production) ;;
  *) echo "deploy.env DEPLOY_ENV must be 'test' or 'production'." >&2; exit 1 ;;
esac

[[ -n "${IMAGE_PREFIX:-}" ]] || { echo "deploy.env IMAGE_PREFIX is required." >&2; exit 1; }
[[ -n "${EDGE_NETWORK:-}" ]] || { echo "deploy.env EDGE_NETWORK is required." >&2; exit 1; }

compose=(docker compose --env-file deploy.env -f compose.yml)
app_services=(gateway auth project github jira meeting submission)
db_services=(auth project github jira meeting submission)

echo "[1/9] Validate Compose"
"${compose[@]}" config --quiet
echo "      OK"

echo "[2/9] Ensure edge network"
docker network inspect "$EDGE_NETWORK" >/dev/null 2>&1 \
  || docker network create "$EDGE_NETWORK" >/dev/null
echo "      OK"

echo "[3/9] Start MySQL"
"${compose[@]}" up -d mysql
mysql_id="$("${compose[@]}" ps -q mysql)"
[[ -n "$mysql_id" ]] || { echo "MySQL container was not created." >&2; exit 1; }
echo "      started: $mysql_id"

echo "[4/9] Wait for MySQL"
mysql_ready=false
for attempt in $(seq 1 30); do
  status="$(docker inspect --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$mysql_id")"
  if [[ "$status" == "healthy" ]]; then
    mysql_ready=true
    break
  fi
  if [[ "$status" == "unhealthy" ]]; then
    docker logs --tail 100 "$mysql_id" || true
    exit 1
  fi
  sleep 2
done
[[ "$mysql_ready" == "true" ]] || {
  echo "Timed out waiting for MySQL." >&2
  docker logs --tail 100 "$mysql_id" || true
  exit 1
}
echo "      healthy"

echo "[5/9] Provision databases"
"${compose[@]}" exec -T mysql /opt/researchtrack/reconcile-databases.sh </dev/null
echo "      OK"

echo "[6/9] Pull application images"
if ! "${compose[@]}" pull "${app_services[@]}"; then
  echo "Backend image pull failed." >&2
  exit 1
fi

for service in "${app_services[@]}"; do
  image="${IMAGE_PREFIX}/researchtrack-${service}:${DEPLOY_ENV}"
  if ! docker image inspect "$image" >/dev/null 2>&1; then
    echo "Required backend image is missing after pull: $image" >&2
    exit 1
  fi
  echo "      image available: $image"
done

echo "[7/9] Validate service DB connectivity"
for service in "${db_services[@]}"; do
  printf '      %-12s ' "$service"
  "${compose[@]}" run --rm --no-deps -T --entrypoint /app/dbcheck "$service" </dev/null
done

echo "[8/9] Apply EF Core migrations"
for service in "${db_services[@]}"; do
  printf '      %-12s ' "$service"
  "${compose[@]}" run --rm --no-deps -T --entrypoint /app/migrate "$service" </dev/null
done

echo "[9/9] Reconcile application containers"
"${compose[@]}" up -d --remove-orphans "${app_services[@]}"

echo
echo "========== COMPOSE STATE AFTER UP =========="
"${compose[@]}" ps -a
echo "============================================"
echo

for service in "${app_services[@]}"; do
  container_id="$("${compose[@]}" ps -a -q "$service")"
  if [[ -z "$container_id" ]]; then
    echo "ERROR: docker compose up returned successfully but '$service' was not created." >&2
    echo
    echo "Resolved Compose services:"
    "${compose[@]}" config --services || true
    echo
    echo "Resolved Compose images:"
    "${compose[@]}" config --images || true
    echo
    echo "Current Compose containers:"
    "${compose[@]}" ps -a || true
    exit 1
  fi
  echo "      $service created: $container_id"
done

echo "All application containers were created."
