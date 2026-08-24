#!/usr/bin/env bash
set -Eeuo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

compose=(docker compose --env-file deploy.env -f compose.yml)
services=(gateway auth project github jira meeting submission)

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

echo "All ResearchTrack backend services are healthy."
