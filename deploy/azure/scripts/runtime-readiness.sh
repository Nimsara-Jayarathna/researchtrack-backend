#!/usr/bin/env bash
# Readiness probe executed *on the infrastructure VM* through vm-run.sh.
# It is intentionally narrower than full deployment validation: temporary
# runtime acquisition needs core dependencies + application readiness, not a
# full Grafana/Prometheus configuration audit on every wake-up.
#
# RUNTIME_READINESS_MODE=infra  -> MySQL, Kafka, Nginx usable
# RUNTIME_READINESS_MODE=apps   -> all seven /health/ready + Nginx -> Gateway
set -Eeuo pipefail

mode="${RUNTIME_READINESS_MODE:-infra}"
opt="${RT_OPT:-/opt/researchtrack}"
compose="$opt/scripts/compose.sh"
interval="${RUNTIME_READY_INTERVAL_SECONDS:-5}"
attempts="${RUNTIME_READY_ATTEMPTS:-72}" # 6 minutes at 5 s

[[ -x "$compose" ]] || { echo "compose helper missing: $compose" >&2; exit 1; }
[[ -f "$opt/runtime/infra.env" ]] || { echo "runtime infra.env missing" >&2; exit 1; }
# shellcheck disable=SC1091
set -a; . "$opt/runtime/infra.env"; set +a

container_ready() {
  local service="$1" id running health
  id="$($compose ps -q "$service" 2>/dev/null || true)"
  [[ -n "$id" ]] || return 1
  running="$(docker inspect --format '{{.State.Running}}' "$id" 2>/dev/null || true)"
  [[ "$running" == true ]] || return 1
  health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$id" 2>/dev/null || true)"
  [[ "$health" == healthy || "$health" == none ]]
}

port_open() {
  local host="$1" port="$2"
  timeout 4 bash -c "</dev/tcp/$host/$port" >/dev/null 2>&1
}

infra_ready() {
  container_ready mysql || return 1
  container_ready kafka || return 1
  container_ready nginx || return 1
  port_open "$INFRA_PRIVATE_IP" 3306 || return 1
  port_open "$INFRA_PRIVATE_IP" 9092 || return 1
  "$compose" exec -T nginx nginx -t -q >/dev/null 2>&1 || return 1
}

apps_ready() {
  local tls_args=() service fqdn
  [[ "${ACA_TLS_VERIFY:-on}" == on ]] || tls_args=(-k)
  for service in auth project github jira meeting submission gateway; do
    fqdn="rt-$service-prod.$ACA_ENV_DOMAIN"
    curl "${tls_args[@]}" -fsS --max-time 5 "https://$fqdn/health/ready" >/dev/null || return 1
  done

  # Prove the same Nginx -> Gateway path used by the public edge, without
  # depending on external DNS being reachable from the GitHub runner.
  if [[ -s "/etc/letsencrypt/live/$API_HOSTNAME/fullchain.pem" ]]; then
    curl -fsS --max-time 8 --resolve "$API_HOSTNAME:443:127.0.0.1" \
      "https://$API_HOSTNAME/health/ready" >/dev/null || return 1
  else
    curl -fsS --max-time 8 --resolve "$API_HOSTNAME:80:127.0.0.1" \
      "http://$API_HOSTNAME/health/ready" >/dev/null || return 1
  fi
}

case "$mode" in
  infra) check=infra_ready; label='VM dependencies (MySQL, Kafka, Nginx)' ;;
  apps) check=apps_ready; label='all seven applications and Nginx -> Gateway' ;;
  *) echo "Unsupported RUNTIME_READINESS_MODE '$mode' (expected infra|apps)." >&2; exit 2 ;;
esac

for ((attempt=1; attempt<=attempts; attempt++)); do
  if "$check"; then
    echo "Runtime readiness passed: $label."
    exit 0
  fi
  echo "Runtime readiness pending: $label ($attempt/$attempts) ..."
  sleep "$interval"
done

echo "Runtime readiness timed out: $label." >&2
if [[ "$mode" == infra ]]; then
  "$compose" ps >&2 || true
else
  echo "Managed app control-plane states:" >&2
  echo "  inspect from GitHub runner with runtime-power.sh status" >&2
fi
exit 1
