#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

compose=(docker compose --env-file deploy.env -f compose.yml)
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

  healthy=false
  for attempt in $(seq 1 35); do
    running="$(docker inspect --format='{{.State.Running}}' "$container_id" 2>/dev/null || true)"
    health="$(docker inspect --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$container_id" 2>/dev/null || true)"

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

  if ! docker exec "$container_id" curl -fsS http://127.0.0.1:8080/health/ready >/dev/null; then
    echo "$service is live but not ready." >&2
    docker logs --tail 200 "$container_id" || true
    exit 1
  fi

  echo "$service is healthy and ready."
done

probe_container_id="$("${compose[@]}" ps -a -q gateway)"
[[ -n "$probe_container_id" ]] || { echo "Cannot run observability HTTP checks because gateway container was not found." >&2; exit 1; }

wait_for_observability_http() {
  local service="$1"
  local url="$2"
  local container_id="$3"
  local ready=false

  for attempt in $(seq 1 35); do
    running="$(docker inspect --format='{{.State.Running}}' "$container_id" 2>/dev/null || true)"
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

  running="$(docker inspect --format='{{.State.Running}}' "$container_id" 2>/dev/null || true)"
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
      wait_for_observability_http "$service" "http://prometheus:9090/-/ready" "$container_id"

      rules_payload="$(docker exec "$probe_container_id" curl -fsS http://prometheus:9090/api/v1/rules)"
      if [[ "$rules_payload" != *"researchtrack-availability"* \
        || "$rules_payload" != *"researchtrack-github-integration"* ]]; then
        echo "Prometheus is ready but the ResearchTrack operational alert rule groups are not loaded." >&2
        docker logs --tail 200 "$container_id" || true
        exit 1
      fi

      echo "$service is running, ready, and has the ResearchTrack alert rules loaded."
      ;;
    grafana)
      wait_for_observability_http "$service" "http://grafana:3000/api/health" "$container_id"

      grafana_admin_user="$(docker exec "$container_id" printenv GF_SECURITY_ADMIN_USER 2>/dev/null || true)"
      grafana_admin_password="$(docker exec "$container_id" printenv GF_SECURITY_ADMIN_PASSWORD 2>/dev/null || true)"
      if [[ -z "$grafana_admin_user" || -z "$grafana_admin_password" ]]; then
        echo "Cannot verify Grafana datasource because Grafana admin credentials are missing from the container environment." >&2
        exit 1
      fi

      if ! docker exec "$probe_container_id" sh -c \
        'curl -fsS -u "$1:$2" http://grafana:3000/api/datasources/uid/prometheus >/dev/null' \
        sh "$grafana_admin_user" "$grafana_admin_password"; then
        echo "Grafana is running but the provisioned Prometheus datasource with UID 'prometheus' is not available." >&2
        docker logs --tail 200 "$container_id" || true
        exit 1
      fi

      echo "$service is running, healthy, and has the Prometheus datasource."
      ;;
  esac
done

echo "All ResearchTrack backend and observability services are healthy."
