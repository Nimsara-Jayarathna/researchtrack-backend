#!/usr/bin/env bash
# Validates private networking from the VM side. Runs on the VM (Azure Run
# Command). The Container Apps -> MySQL/Kafka direction is proven separately by
# network-probe.sh, which runs inside the Container Apps environment.
#
# Environment:
#   EXPECT_APPS=true    the seven Container Apps exist; check VM -> app HTTPS,
#                       /metrics and Nginx -> Gateway
#   ACA_ENV_STATIC_IP   optional; expected private DNS answer
set -Eeuo pipefail

opt=/opt/researchtrack
expect_apps="${EXPECT_APPS:-false}"
services=(gateway auth project github jira meeting submission)

set -a
# shellcheck disable=SC1091
. "$opt/runtime/infra.env"
set +a

failures=0
fail() { echo "FAIL: $*" >&2; failures=$((failures + 1)); }
ok() { echo "  ok  $*"; }
retry() {
  local attempts="$1" delay="$2"
  shift 2
  for _ in $(seq 1 "$attempts"); do
    "$@" >/dev/null 2>&1 && return 0
    sleep "$delay"
  done
  return 1
}

tls_args=()
[[ "$ACA_TLS_VERIFY" == on ]] || tls_args=(-k)

echo "== Private DNS for the Container Apps environment"
answer="$(getent ahostsv4 "rt-gateway-prod.$ACA_ENV_DOMAIN" | awk 'NR==1 {print $1}')"
if [[ -z "$answer" ]]; then
  fail "rt-gateway-prod.$ACA_ENV_DOMAIN does not resolve (private DNS zone/link missing?)"
elif [[ ! "$answer" =~ ^10\. ]]; then
  fail "rt-gateway-prod.$ACA_ENV_DOMAIN resolves to non-private $answer"
elif [[ -n "${ACA_ENV_STATIC_IP:-}" && "$answer" != "$ACA_ENV_STATIC_IP" ]]; then
  fail "private DNS answer $answer != environment static IP $ACA_ENV_STATIC_IP"
else
  ok "*.$ACA_ENV_DOMAIN -> $answer"
fi

echo "== Infrastructure endpoints on the private IP"
for port in 3306 9092; do
  if timeout 5 bash -c "</dev/tcp/$INFRA_PRIVATE_IP/$port" 2>/dev/null; then ok "$INFRA_PRIVATE_IP:$port listening"
  else fail "$INFRA_PRIVATE_IP:$port not reachable"; fi
done

if [[ "$expect_apps" == true ]]; then
  echo "== VM -> Container Apps (HTTPS over the VNet)"
  for service in "${services[@]}"; do
    fqdn="rt-$service-prod.$ACA_ENV_DOMAIN"
    if retry 20 3 curl "${tls_args[@]}" -fsS --max-time 5 "https://$fqdn/health/ready"; then
      ok "$service ready (https://$fqdn)"
    else
      fail "$service not ready at https://$fqdn/health/ready"
    fi
  done

  echo "== Prometheus scrape paths"
  # Output is captured before matching: `curl | grep -q` can fail under
  # pipefail when grep exits early.
  has_metrics() {
    local body
    body="$(curl "$@" -fsS --max-time 5 2>/dev/null)" || return 1
    grep -q '^# HELP' <<<"$body"
  }
  for service in auth project github jira meeting submission; do
    has_metrics "${tls_args[@]}" "https://rt-$service-prod.$ACA_ENV_DOMAIN/metrics" \
      && ok "$service /metrics" || fail "$service /metrics not reachable"
  done
  has_metrics "http://rt-gateway-prod.$ACA_ENV_DOMAIN:9100/metrics" \
    && ok "gateway :9100/metrics" || fail "gateway metrics port 9100 not reachable"

  echo "== Nginx -> Gateway"
  if [[ -s "/etc/letsencrypt/live/$API_HOSTNAME/fullchain.pem" ]]; then
    edge_resolve="$API_HOSTNAME:443:127.0.0.1"
    edge_url="https://$API_HOSTNAME"
  else
    # Pre-cutover: restricted HTTP mode (on-VM/VNet callers only).
    edge_resolve="$API_HOSTNAME:80:127.0.0.1"
    edge_url="http://$API_HOSTNAME"
  fi
  retry 10 3 curl -fsS --max-time 10 --resolve "$edge_resolve" "$edge_url/health/ready" \
    && ok "$edge_url/health/ready via Nginx" || fail "Nginx cannot reach the Gateway ($edge_url)"
  code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 --resolve "$edge_resolve" "$edge_url/metrics")"
  [[ "$code" == 404 ]] && ok "/metrics not exposed publicly" || fail "public /metrics returned $code"
fi

if ((failures > 0)); then
  echo "Infrastructure network validation failed: $failures problem(s)." >&2
  exit 1
fi
echo "Infrastructure network validation passed."
