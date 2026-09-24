#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

set -a
. ./deploy.env
set +a

compose=(docker compose --env-file deploy.env -f compose.yml)

if [[ "$DEPLOY_ENV" == "test" ]]; then
  echo "Checking Kafka..."

  kafka_container_id="$("${compose[@]}" ps -a -q kafka)"

  if [[ -z "$kafka_container_id" ]]; then
    echo "Kafka container was never created." >&2
    echo
    echo "Current Compose state:"
    "${compose[@]}" ps -a
    exit 1
  fi

  kafka_running="$(
    docker inspect \
      --format='{{.State.Running}}' \
      "$kafka_container_id" 2>/dev/null ||
      true
  )"

  kafka_health="$(
    docker inspect \
      --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' \
      "$kafka_container_id" 2>/dev/null ||
      true
  )"

  if [[ "$kafka_running" != "true" ]]; then
    echo "Kafka container exists but has stopped." >&2
    echo
    echo "Container status:"
    docker inspect \
      --format='Status={{.State.Status}} ExitCode={{.State.ExitCode}} Error={{.State.Error}}' \
      "$kafka_container_id" || true

    echo
    echo "Last 200 Kafka log lines:"
    docker logs --tail 200 "$kafka_container_id" || true
    exit 1
  fi

  if [[ "$kafka_health" != "healthy" ]]; then
    echo "Kafka container is running but is not healthy." >&2
    echo "Kafka health status: ${kafka_health:-unknown}" >&2
    docker logs --tail 200 "$kafka_container_id" || true
    exit 1
  fi

  if ! "${compose[@]}" exec -T kafka \
    /opt/kafka/bin/kafka-topics.sh \
    --bootstrap-server kafka:9092 \
    --list >/dev/null; then
    echo "Kafka broker connectivity verification failed." >&2
    docker logs --tail 200 "$kafka_container_id" || true
    exit 1
  fi

  echo "Kafka is running, healthy, and accepting broker connections."
fi

services=(gateway auth project github jira meeting submission)
observability_services=(prometheus grafana)

for service in "${services[@]}"; do
  echo "Checking $service..."

  container_id="$("${compose[@]}" ps -a -q "$service")"

  if [[ -z "$container_id" ]]; then
    echo "Container was never created: $service" >&2
    echo
    echo "Current Compose state:"
    "${compose[@]}" ps -a
    exit 1
  fi

  image="${IMAGE_PREFIX}/researchtrack-${service}:${DEPLOY_ENV}"

  expected_image_id="$(
    docker image inspect \
      --format='{{.Id}}' \
      "$image" 2>/dev/null ||
      true
  )"

  running_image_id="$(
    docker inspect \
      --format='{{.Image}}' \
      "$container_id" 2>/dev/null ||
      true
  )"

  if [[ -z "$expected_image_id" || "$running_image_id" != "$expected_image_id" ]]; then
    echo "$service is not running the exact image currently resolved by $image." >&2
    echo "Expected image ID: ${expected_image_id:-missing}" >&2
    echo "Running image ID:  ${running_image_id:-missing}" >&2
    exit 1
  fi

  healthy=false

  for attempt in $(seq 1 35); do
    running="$(
      docker inspect \
        --format='{{.State.Running}}' \
        "$container_id" 2>/dev/null ||
        true
    )"

    health="$(
      docker inspect \
        --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' \
        "$container_id" 2>/dev/null ||
        true
    )"

    if [[ "$running" != "true" ]]; then
      echo "$service container exists but has stopped." >&2
      echo
      echo "Container status:"
      docker inspect \
        --format='Status={{.State.Status}} ExitCode={{.State.ExitCode}} Error={{.State.Error}}' \
        "$container_id" || true

      echo
      echo "Last 200 log lines:"
      docker logs --tail 200 "$container_id" || true
      exit 1
    fi

    if [[ "$health" == "healthy" ]]; then
      healthy=true
      break
    fi

    if [[ "$health" == "unhealthy" ]]; then
      echo "$service reported unhealthy." >&2
      docker logs --tail 200 "$container_id" || true
      exit 1
    fi

    sleep 2
  done

  [[ "$healthy" == "true" ]] || {
    echo "Timed out waiting for $service to become healthy." >&2
    docker logs --tail 200 "$container_id" || true
    exit 1
  }

  if ! docker exec "$container_id" \
    curl -fsS http://127.0.0.1:8080/health/ready >/dev/null; then
    echo "$service is live but not ready." >&2
    docker logs --tail 200 "$container_id" || true
    exit 1
  fi

  echo "$service is healthy, ready, and running the current pulled image."
done

probe_container_id="$("${compose[@]}" ps -a -q gateway)"

[[ -n "$probe_container_id" ]] || {
  echo "Cannot run observability HTTP checks because gateway container was not found." >&2
  exit 1
}

wait_for_observability_http() {
  local service="$1"
  local url="$2"
  local container_id="$3"
  local ready=false

  for attempt in $(seq 1 35); do
    running="$(
      docker inspect \
        --format='{{.State.Running}}' \
        "$container_id" 2>/dev/null ||
        true
    )"

    if [[ "$running" != "true" ]]; then
      echo "$service container exists but has stopped." >&2
      echo
      echo "Container status:"
      docker inspect \
        --format='Status={{.State.Status}} ExitCode={{.State.ExitCode}} Error={{.State.Error}}' \
        "$container_id" || true

      echo
      echo "Last 200 log lines:"
      docker logs --tail 200 "$container_id" || true
      exit 1
    fi

    if docker exec "$probe_container_id" curl -fsS "$url" >/dev/null; then
      ready=true
      break
    fi

    sleep 2
  done

  [[ "$ready" == "true" ]] || {
    echo "$service is running but its HTTP health check did not become ready: $url" >&2
    docker logs --tail 200 "$container_id" || true
    exit 1
  }
}

for service in "${observability_services[@]}"; do
  echo "Checking $service..."

  container_id="$("${compose[@]}" ps -a -q "$service")"

  if [[ -z "$container_id" ]]; then
    echo "Container was never created: $service" >&2
    echo
    echo "Current Compose state:"
    "${compose[@]}" ps -a
    exit 1
  fi

  running="$(
    docker inspect \
      --format='{{.State.Running}}' \
      "$container_id" 2>/dev/null ||
      true
  )"

  if [[ "$running" != "true" ]]; then
    echo "$service container exists but has stopped." >&2
    echo
    echo "Container status:"
    docker inspect \
      --format='Status={{.State.Status}} ExitCode={{.State.ExitCode}} Error={{.State.Error}}' \
      "$container_id" || true

    echo
    echo "Last 200 log lines:"
    docker logs --tail 200 "$container_id" || true
    exit 1
  fi

  case "$service" in
    prometheus)
      wait_for_observability_http \
        "$service" \
        "http://prometheus:9090/-/ready" \
        "$container_id"

      rules_payload="$(
        docker exec "$probe_container_id" \
          curl -fsS http://prometheus:9090/api/v1/rules
      )"

      expected_rule_groups=(
        researchtrack-availability
        researchtrack-github-integration
      )

      expected_alerts=(
        ResearchTrackServiceUnavailable
        ResearchTrackApiGatewayUnavailable
        ResearchTrackSustainedServerErrors
        ResearchTrackGitHubSyncFailures
        ResearchTrackGitHubReconciliationFailures
        ResearchTrackGitHubReconciliationMissed
      )

      for expected in "${expected_rule_groups[@]}"; do
        if [[ "$rules_payload" != *"$expected"* ]]; then
          echo "Prometheus is ready but expected ResearchTrack rule group '$expected' is not loaded." >&2
          docker logs --tail 200 "$container_id" || true
          exit 1
        fi
      done

      for expected in "${expected_alerts[@]}"; do
        if [[ "$rules_payload" != *"$expected"* ]]; then
          echo "Prometheus is ready but expected alert '$expected' is not loaded." >&2
          docker logs --tail 200 "$container_id" || true
          exit 1
        fi
      done

      echo "$service is running, ready, and has all ResearchTrack operational alert rules loaded."
      ;;

    grafana)
      wait_for_observability_http \
        "$service" \
        "http://grafana:3000/api/health" \
        "$container_id"

      grafana_admin_user="$(
        docker exec "$container_id" \
          printenv GF_SECURITY_ADMIN_USER 2>/dev/null ||
          true
      )"

      grafana_admin_password="$(
        docker exec "$container_id" \
          printenv GF_SECURITY_ADMIN_PASSWORD 2>/dev/null ||
          true
      )"

      if [[ -z "$grafana_admin_user" || -z "$grafana_admin_password" ]]; then
        echo "Cannot verify Grafana provisioning because Grafana admin credentials are missing from the container environment." >&2
        exit 1
      fi

      if ! docker exec "$probe_container_id" sh -c \
        'curl -fsS -u "$1:$2" http://grafana:3000/api/datasources/uid/prometheus >/dev/null' \
        sh "$grafana_admin_user" "$grafana_admin_password"; then
        echo "Grafana is running but the provisioned Prometheus datasource with UID 'prometheus' is not available." >&2
        docker logs --tail 200 "$container_id" || true
        exit 1
      fi

      for dashboard_uid in researchtrack-overview github-sync-operations; do
        if ! docker exec "$probe_container_id" sh -c \
          'curl -fsS -u "$1:$2" "http://grafana:3000/api/dashboards/uid/$3" >/dev/null' \
          sh "$grafana_admin_user" "$grafana_admin_password" "$dashboard_uid"; then
          echo "Grafana is running but expected provisioned dashboard '$dashboard_uid' is not available." >&2
          docker logs --tail 200 "$container_id" || true
          exit 1
        fi
      done

      echo "$service is running, healthy, and has the Prometheus datasource and ResearchTrack dashboards provisioned."
      ;;
  esac
done

if [[ "$DEPLOY_ENV" == "test" ]]; then
  echo "All ResearchTrack backend, Kafka, and observability services are healthy and running the current deployment state."
else
  echo "All ResearchTrack backend and observability services are healthy and running the current deployment state."
fi