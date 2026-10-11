#!/usr/bin/env bash
# Existing monitoring only: never invokes broker/database/edge reconciliation.
set -Eeuo pipefail
opt=/opt/researchtrack
compose="$opt/scripts/compose.sh"
set -a
# shellcheck disable=SC1091
. "$opt/runtime/infra.env"
set +a
[[ "${ACA_ENV_DOMAIN:-}" =~ ^[a-z0-9][a-z0-9.-]*$ ]] || { echo 'Invalid ACA_ENV_DOMAIN.' >&2; exit 1; }
[[ "${ACA_TLS_VERIFY:-}" == on || "${ACA_TLS_VERIFY:-}" == off ]] || { echo 'Invalid ACA_TLS_VERIFY.' >&2; exit 1; }
declare -A unchanged
for service in kafka mysql nginx; do
  id="$("$compose" ps -q "$service")"
  [[ -n "$id" ]] || { echo "Existing $service required." >&2; exit 1; }
  unchanged[$service]="$(docker inspect --format '{{.Id}} {{.State.StartedAt}}' "$id")"
done
next="$(mktemp "$opt/prometheus/.monitoring-next.XXXXXX")"
override="$(mktemp "$opt/.monitoring-images.XXXXXX.json")"
trap 'rm -f "$next" "$override"' EXIT
insecure=false
[[ "$ACA_TLS_VERIFY" == off ]] && insecure=true
sed -e "s|__ACA_ENV_DOMAIN__|$ACA_ENV_DOMAIN|g" -e "s|__ACA_TLS_INSECURE__|$insecure|g" \
  "$opt/prometheus/prometheus.yml.template" > "$next"
chmod 644 "$next"
"$compose" config --quiet
# Use the deployed Prometheus image, without pulling or upgrading it.
prom_id="$("$compose" ps -q prometheus)"
prom_image="$(docker inspect --format '{{.Image}}' "$prom_id")"
grafana_image="$(docker inspect --format '{{.Image}}' "$("$compose" ps -q grafana)")"
[[ "$prom_image" =~ ^sha256:[0-9a-f]{64}$ && "$grafana_image" =~ ^sha256:[0-9a-f]{64}$ ]] \
  || { echo 'Existing monitoring image identities required.' >&2; exit 1; }
printf '{"services":{"prometheus":{"image":"%s"},"grafana":{"image":"%s"}}}\n' \
  "$prom_image" "$grafana_image" > "$override"
docker run --rm --network none --read-only --entrypoint /bin/promtool \
  -v "$next:/etc/prometheus/prometheus.yml:ro" -v "$opt/prometheus/rules:/etc/prometheus/rules:ro" \
  "$prom_image" check config /etc/prometheus/prometheus.yml
mv "$next" "$opt/prometheus/prometheus.yml"
"$compose" pull --quiet kafka-exporter
"$compose" up -d --no-deps kafka-exporter
"$compose" -f "$override" up -d --no-deps --force-recreate --pull never prometheus grafana
for service in kafka mysql nginx; do
  id="$("$compose" ps -q "$service")"
  [[ "$(docker inspect --format '{{.Id}} {{.State.StartedAt}}' "$id")" == "${unchanged[$service]}" ]] \
    || { echo "FAIL: $service identity/start time changed during monitoring reconciliation." >&2; exit 1; }
done
echo 'Monitoring reconciled; Kafka, MySQL and Nginx identities/start times unchanged.'
