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

[[ -n "${IMAGE_PREFIX:-}" ]] || {
  echo "deploy.env IMAGE_PREFIX is required." >&2
  exit 1
}

[[ -n "${EDGE_NETWORK:-}" ]] || {
  echo "deploy.env EDGE_NETWORK is required." >&2
  exit 1
}

[[ -n "${KAFKA_CLUSTER_ID:-}" ]] || {
  echo "deploy.env KAFKA_CLUSTER_ID is required." >&2
  exit 1
}

[[ "$KAFKA_CLUSTER_ID" =~ ^[A-Za-z0-9_-]{22}$ ]] || {
  echo "deploy.env KAFKA_CLUSTER_ID must be a Kafka-generated 22-character UUID." >&2
  exit 1
}

compose=(docker compose --env-file deploy.env -f compose.yml)
app_services=(gateway auth project github jira meeting submission)
db_services=(auth project github jira meeting submission)
observability_services=(prometheus grafana)
started_services=("${app_services[@]}" "${observability_services[@]}")

if [[ "$DEPLOY_ENV" == "test" ]]; then
  started_services+=(kafka)
fi

declare -A app_target_image_ids=()
declare -A app_before_container_ids=()
declare -A app_env_changed=()
force_recreate_services=()

sha256_file() {
  sha256sum "$1" | awk '{print $1}'
}

container_file_sha256() {
  local container_id="$1"
  local container_path="$2"
  local tmp_dir tmp_file

  tmp_dir="$(mktemp -d)"
  tmp_file="$tmp_dir/file"

  if ! docker cp "$container_id:$container_path" "$tmp_file" >/dev/null 2>&1; then
    rm -rf "$tmp_dir"
    return 1
  fi

  sha256_file "$tmp_file"
  rm -rf "$tmp_dir"
}

service_env_files() {
  case "$1" in
    gateway) printf '%s\n' gateway.env ;;
    auth) printf '%s\n' shared-auth.env auth.env ;;
    project) printf '%s\n' shared-auth.env project.env ;;
    github) printf '%s\n' shared-auth.env github.env ;;
    jira) printf '%s\n' shared-auth.env jira.env ;;
    meeting) printf '%s\n' shared-auth.env meeting.env ;;
    submission) printf '%s\n' shared-auth.env submission.env ;;
    *) return 1 ;;
  esac
}

service_environment_changed() {
  local service="$1"
  local file

  # env.previous is retained by the workflow until health verification succeeds.
  # If it is unavailable while a container already exists, recreate conservatively.
  if [[ ! -d env.previous ]]; then
    [[ -n "$("${compose[@]}" ps -a -q "$service")" ]]
    return
  fi

  while IFS= read -r file; do
    if [[ ! -f "env.previous/$file" || ! -f "env/$file" ]]; then
      return 0
    fi
    if ! cmp -s "env.previous/$file" "env/$file"; then
      return 0
    fi
  done < <(service_env_files "$service")

  return 1
}

append_unique_service() {
  local service="$1"
  local existing

  for existing in "${force_recreate_services[@]}"; do
    [[ "$existing" == "$service" ]] && return 0
  done

  force_recreate_services+=("$service")
}

echo "[1/10] Validate Compose"
"${compose[@]}" config --quiet
echo "      OK"

echo "[2/10] Ensure edge network"
docker network inspect "$EDGE_NETWORK" >/dev/null 2>&1 \
  || docker network create "$EDGE_NETWORK" >/dev/null
echo "      OK"

echo "[3/10] Start Kafka when deploying test"

if [[ "$DEPLOY_ENV" == "test" ]]; then
  echo "      Pulling Kafka image..."

  if ! "${compose[@]}" pull kafka; then
    echo "Kafka image pull failed." >&2
    exit 1
  fi

  echo "      Starting Kafka..."
  "${compose[@]}" up -d --wait --wait-timeout 180 kafka

  kafka_id="$("${compose[@]}" ps -a -q kafka)"

  [[ -n "$kafka_id" ]] || {
    echo "Kafka container was not created." >&2
    exit 1
  }

  kafka_running="$(docker inspect \
    --format='{{.State.Running}}' \
    "$kafka_id" 2>/dev/null || true)"

  kafka_health="$(docker inspect \
    --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' \
    "$kafka_id" 2>/dev/null || true)"

  if [[ "$kafka_running" != "true" || "$kafka_health" != "healthy" ]]; then
    echo "Kafka did not become healthy." >&2
    echo "Running: ${kafka_running:-unknown}" >&2
    echo "Health: ${kafka_health:-unknown}" >&2
    docker logs --tail 200 "$kafka_id" || true
    exit 1
  fi

  echo "      Kafka is healthy: $kafka_id"
else
  echo "      Skipped: Kafka is currently enabled only for the test environment."
fi

echo "[4/10] Start MySQL with current reconciliation script"
mysql_id="$("${compose[@]}" ps -a -q mysql)"
mysql_host_script_sha="$(sha256_file ./mysql/reconcile-databases.sh)"
mysql_force_recreate=false

if [[ -n "$mysql_id" ]]; then
  mysql_container_script_sha="$(
    container_file_sha256 \
      "$mysql_id" \
      /opt/researchtrack/reconcile-databases.sh ||
      true
  )"

  if [[ -z "$mysql_container_script_sha" || "$mysql_container_script_sha" != "$mysql_host_script_sha" ]]; then
    mysql_force_recreate=true
    echo "      MySQL reconciliation script changed; recreating container to refresh its bind mount."
  fi
fi

if [[ "$mysql_force_recreate" == "true" ]]; then
  "${compose[@]}" up -d --force-recreate mysql
else
  "${compose[@]}" up -d mysql
fi

mysql_id="$("${compose[@]}" ps -q mysql)"

[[ -n "$mysql_id" ]] || {
  echo "MySQL container was not created." >&2
  exit 1
}

mysql_mounted_script_sha="$(
  container_file_sha256 \
    "$mysql_id" \
    /opt/researchtrack/reconcile-databases.sh ||
    true
)"

if [[ "$mysql_mounted_script_sha" != "$mysql_host_script_sha" ]]; then
  echo "MySQL container does not have the current reconciliation script mounted." >&2
  exit 1
fi

echo "      started with current reconciliation script: $mysql_id"

echo "[5/10] Wait for MySQL"
mysql_ready=false

for attempt in $(seq 1 30); do
  status="$(
    docker inspect \
      --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' \
      "$mysql_id"
  )"

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

echo "[6/10] Provision databases"
"${compose[@]}" exec -T mysql \
  /opt/researchtrack/reconcile-databases.sh </dev/null
echo "      OK"

echo "[7/10] Pull application images"

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

  app_target_image_ids["$service"]="$(docker image inspect --format='{{.Id}}' "$image")"
  app_before_container_ids["$service"]="$("${compose[@]}" ps -a -q "$service")"

  if service_environment_changed "$service"; then
    app_env_changed["$service"]="true"
  else
    app_env_changed["$service"]="false"
  fi

  echo "      image available: $image (${app_target_image_ids[$service]})"
done

echo "[8/10] Validate service DB connectivity"

for service in "${db_services[@]}"; do
  printf '      %-12s ' "$service"

  "${compose[@]}" run \
    --rm \
    --no-deps \
    -T \
    --entrypoint /app/dbcheck \
    "$service" </dev/null
done

echo "[9/10] Apply EF Core migrations"

for service in "${db_services[@]}"; do
  printf '      %-12s ' "$service"

  "${compose[@]}" run \
    --rm \
    --no-deps \
    -T \
    --entrypoint /app/migrate \
    "$service" </dev/null
done

echo "[10/10] Reconcile application and observability containers"

# Let Compose apply ordinary image/config changes first.
"${compose[@]}" up -d --remove-orphans "${app_services[@]}"

# Compose normally recreates containers when image/config/env changes are detected.
# Verify that happened and force recreation only where required. This protects
# deployments that use moving image tags and env_file values from silently
# leaving a service on an old image or old environment.
force_recreate_services=()

for service in "${app_services[@]}"; do
  container_id="$("${compose[@]}" ps -a -q "$service")"

  [[ -n "$container_id" ]] || {
    echo "Application container was not created: $service" >&2
    exit 1
  }

  running_image_id="$(docker inspect --format='{{.Image}}' "$container_id")"

  if [[ "$running_image_id" != "${app_target_image_ids[$service]}" ]]; then
    echo "      $service is still on an old image; forcing recreation."
    append_unique_service "$service"
  fi

  before_id="${app_before_container_ids[$service]}"

  if [[ "${app_env_changed[$service]}" == "true" && -n "$before_id" && "$container_id" == "$before_id" ]]; then
    echo "      $service environment changed but container was not recreated; forcing recreation."
    append_unique_service "$service"
  fi
done

if ((${#force_recreate_services[@]} > 0)); then
  "${compose[@]}" up \
    -d \
    --no-deps \
    --force-recreate \
    "${force_recreate_services[@]}"
fi

# Hard postcondition: every application container must run the exact image that
# was pulled for the current moving environment tag.
for service in "${app_services[@]}"; do
  container_id="$("${compose[@]}" ps -a -q "$service")"

  [[ -n "$container_id" ]] || {
    echo "Application container is missing after reconciliation: $service" >&2
    exit 1
  }

  running_image_id="$(docker inspect --format='{{.Image}}' "$container_id")"

  if [[ "$running_image_id" != "${app_target_image_ids[$service]}" ]]; then
    echo "Application container '$service' is not running the image pulled for ${DEPLOY_ENV}." >&2
    echo "Expected: ${app_target_image_ids[$service]}" >&2
    echo "Running:  $running_image_id" >&2
    exit 1
  fi

  echo "      $service image reconciled: $running_image_id"
done

# Monitoring configuration and Grafana provisioning are bind-mounted. The
# deployment workflow replaces those host files/directories atomically, so an
# already-running container can otherwise retain the old mount/configuration.
# Recreate both observability containers on every deployment to guarantee that
# the newly uploaded rules, Prometheus config, dashboards, datasource config,
# and Grafana environment are active without a manual restart.
declare -A observability_before_ids=()

for service in "${observability_services[@]}"; do
  observability_before_ids["$service"]="$("${compose[@]}" ps -a -q "$service")"
done

"${compose[@]}" up \
  -d \
  --no-deps \
  --force-recreate \
  "${observability_services[@]}"

for service in "${observability_services[@]}"; do
  container_id="$("${compose[@]}" ps -a -q "$service")"

  [[ -n "$container_id" ]] || {
    echo "Observability container was not created: $service" >&2
    exit 1
  }

  before_id="${observability_before_ids[$service]}"

  if [[ -n "$before_id" && "$container_id" == "$before_id" ]]; then
    echo "Observability container '$service' was not recreated as required." >&2
    exit 1
  fi

  echo "      $service recreated with current provisioned configuration: $container_id"
done

echo
echo "========== COMPOSE STATE AFTER UP =========="
"${compose[@]}" ps -a
echo "============================================"
echo

for service in "${started_services[@]}"; do
  container_id="$("${compose[@]}" ps -a -q "$service")"

  if [[ -z "$container_id" ]]; then
    echo "ERROR: deployment reconciliation returned successfully but '$service' was not created." >&2
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

echo "Application images, Kafka infrastructure, environment changes, and observability configuration were reconciled successfully."