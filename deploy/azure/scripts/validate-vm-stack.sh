#!/usr/bin/env bash
# Validates the infrastructure VM stack. Runs on the VM (Azure Run Command) from
# /opt/researchtrack/scripts. Prints no secret values.
#
# Environment:
#   EXPECT_APPS=true   also require all seven Prometheus service targets UP and
#                      no firing critical alerts (set once the Container Apps exist)
set -Eeuo pipefail

opt=/opt/researchtrack
data=/data/researchtrack
compose="$opt/scripts/compose.sh"
expect_apps="${EXPECT_APPS:-false}"

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

echo "== Persistent storage"
if mountpoint -q "$data"; then ok "$data mounted ($(df --output=source "$data" | tail -1))"; else fail "$data is not mounted"; fi
for mount in / "$data"; do
  used="$(df --output=pcent "$mount" | tail -1 | tr -dc '0-9')"
  if (( used >= 90 )); then fail "$mount is ${used}% full"
  elif (( used >= 80 )); then echo "  WARN $mount is ${used}% full"
  else ok "$mount ${used}% used"; fi
done

echo "== Containers"
for service in nginx mysql kafka prometheus grafana; do
  id="$("$compose" ps -q "$service" 2>/dev/null || true)"
  if [[ -z "$id" || "$(docker inspect --format '{{.State.Running}}' "$id")" != true ]]; then
    fail "$service is not running"
    continue
  fi
  health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$id")"
  [[ "$health" == healthy || "$health" == none ]] && ok "$service running ($health)" || fail "$service health is $health"
done

echo "== Host port exposure"
listeners="$(ss -Htln | awk '{print $4}')"
bound_only_to() {
  local port="$1" allowed="$2" addr bad=""
  while read -r addr; do
    [[ "${addr##*:}" == "$port" ]] || continue
    [[ "${addr%:*}" == "$allowed" ]] || bad+=" $addr"
  done <<<"$listeners"
  [[ -z "$bad" ]] && ok "$port bound only to $allowed" || fail "port $port also bound on:$bad"
}
bound_only_to 3306 "$INFRA_PRIVATE_IP"
if docker inspect --format '{{json .HostConfig.PortBindings}}' "$("$compose" ps -q kafka)" \
    | jq -e --arg ip "$INFRA_PRIVATE_IP" 'keys == ["9092/tcp"] and
      .["9092/tcp"] == [{HostIp: $ip, HostPort: "9092"}]' >/dev/null; then
  ok "Kafka Docker binding is private-IP:9092 only"
else
  fail "Kafka Docker binding missing or exposes an unexpected address/port"
fi
bound_only_to 9092 "$INFRA_PRIVATE_IP"
bound_only_to 9090 127.0.0.1
bound_only_to 3000 127.0.0.1
for port in 29092 9093; do
  grep -Eq "(^|:)$port$" <<<"$listeners" && fail "Kafka port $port is published on the host" || ok "$port not published"
done

echo "== MySQL (TLS over the private IP)"
# Every service account must authenticate over TLS to the address Container Apps use.
if "$compose" exec -T -e RT_HOST="$INFRA_PRIVATE_IP" mysql sh -c '
  set -e
  for svc in AUTH PROJECT GITHUB JIRA MEETING SUBMISSION; do
    eval "user=\$${svc}_DB_USER pass=\$${svc}_DB_PASSWORD db=\$${svc}_DB_NAME"
    cipher="$(MYSQL_PWD="$pass" mysql -h "$RT_HOST" -P 3306 -u "$user" --ssl-mode=REQUIRED -N -e "SHOW SESSION STATUS LIKE '"'"'Ssl_cipher'"'"'" "$db" | cut -f2)"
    [ -n "$cipher" ] || { echo "no TLS for $svc" >&2; exit 1; }
  done' </dev/null; then
  ok "all six service accounts connect over TLS"
else
  fail "a service database account cannot connect over TLS"
fi
if "$compose" exec -T -e RT_HOST="$INFRA_PRIVATE_IP" mysql sh -c \
    'MYSQL_PWD="$AUTH_DB_PASSWORD" mysql -h "$RT_HOST" -u "$AUTH_DB_USER" --ssl-mode=DISABLED -e "SELECT 1" >/dev/null 2>&1' </dev/null; then
  fail "MySQL accepted a non-TLS TCP connection"
else
  ok "non-TLS TCP connections are rejected"
fi

echo "== Kafka (TLS listener on the private IP)"
# Deterministic and idempotent: record the partition end offset, produce one
# unique token synchronously (acks=all) through the TLS listener that Container
# Apps use, then consume exactly one record from that offset through the same
# listener and compare it byte-for-byte. The smoke topic is single-partition, so
# the token is the next record regardless of earlier runs.
# shellcheck source=../vm/scripts/kafka-common.sh
. "$opt/scripts/kafka-common.sh"

kafka_log=""
kafka_detail=""
if kafka_smoke; then
  ok "TLS produce/consume via $INFRA_PRIVATE_IP:9092 ($kafka_detail)"
else
  fail "Kafka TLS smoke message was not consumed"
  tail -n 60 "$kafka_log" | sed 's/^/       /' >&2
fi
rm -f "$kafka_log"

echo "== Prometheus"
if retry 20 3 curl -fsS http://127.0.0.1:9090/-/ready; then
  rules="$(curl -fsS http://127.0.0.1:9090/api/v1/rules)"
  for expected in researchtrack-availability researchtrack-github-integration researchtrack-jira-integration \
      ResearchTrackServiceUnavailable ResearchTrackApiGatewayUnavailable \
      ResearchTrackSustainedServerErrors ResearchTrackGitHubSyncFailures \
      ResearchTrackGitHubReconciliationFailures ResearchTrackGitHubReconciliationMissed ResearchTrackGitHubWebhookFailures \
      ResearchTrackGitHubSyncQueueBacklog ResearchTrackJiraSyncFailures \
      ResearchTrackJiraWebhookFailures ResearchTrackJiraWebhookRegistrationDegraded \
      ResearchTrackJiraReconciliationFailures ResearchTrackJiraReconciliationMissed \
      ResearchTrackJiraSyncQueueBacklog ResearchTrackJiraAtlassianApiFailures; do
    [[ "$rules" == *"\"$expected\""* ]] || fail "rule/alert '$expected' not loaded"
  done
  ok "alert rules loaded"

  if [[ "$expect_apps" == true ]]; then
    targets_up() {
      curl -fsS http://127.0.0.1:9090/api/v1/targets | jq -e '
        [.data.activeTargets[] | select(.labels.job == "researchtrack-services")] as $t
        | ($t | length) == 7 and all($t[]; .health == "up")' >/dev/null
    }
    if retry 18 5 targets_up; then
      ok "all 7 service targets UP (service/environment labels present)"
    else
      curl -fsS http://127.0.0.1:9090/api/v1/targets | jq -r '.data.activeTargets[]
        | select(.labels.job == "researchtrack-services")
        | "       \(.labels.service) \(.health) \(.lastError)"' >&2 || true
      fail "not every Container App is scraped successfully"
    fi
    critical="$(curl -fsS http://127.0.0.1:9090/api/v1/alerts | jq -r '[.data.alerts[]
      | select(.state == "firing" and .labels.severity == "critical") | .labels.alertname] | unique | join(", ")')"
    [[ -z "$critical" ]] && ok "no firing critical alerts" || fail "critical alerts firing: $critical"
  fi
else
  fail "Prometheus is not ready"
fi

echo "== Grafana"
if retry 20 3 curl -fsS http://127.0.0.1:3000/api/health; then
  grafana_auth="$(set -a; . "$opt/runtime/grafana.env"; printf '%s:%s' "$GF_SECURITY_ADMIN_USER" "$GF_SECURITY_ADMIN_PASSWORD")"
  curl -fsS -u "$grafana_auth" http://127.0.0.1:3000/api/datasources/uid/prometheus >/dev/null \
    && ok "Prometheus datasource" || fail "Grafana Prometheus datasource missing"
  for uid in researchtrack-overview github-sync-operations jira-integration-operations; do
    curl -fsS -u "$grafana_auth" "http://127.0.0.1:3000/api/dashboards/uid/$uid" >/dev/null \
      && ok "dashboard $uid" || fail "Grafana dashboard '$uid' missing"
  done
  unset grafana_auth
else
  fail "Grafana is not healthy"
fi

echo "== Nginx"
"$compose" exec -T nginx nginx -t -q && ok "configuration valid" || fail "nginx configuration invalid"
if [[ -s "/etc/letsencrypt/live/$GRAFANA_HOSTNAME/fullchain.pem" ]]; then
  curl -fsS --max-time 10 --resolve "$GRAFANA_HOSTNAME:443:127.0.0.1" "https://$GRAFANA_HOSTNAME/api/health" >/dev/null \
    && ok "https://$GRAFANA_HOSTNAME -> grafana" || fail "Grafana not reachable through Nginx HTTPS"
  code="$(curl -s -o /dev/null -w '%{http_code}' --resolve "$GRAFANA_HOSTNAME:80:127.0.0.1" "http://$GRAFANA_HOSTNAME/")"
  [[ "$code" == 301 ]] && ok "HTTP -> HTTPS redirect" || fail "expected 301 from http://$GRAFANA_HOSTNAME, got $code"
else
  echo "  --  no certificate for $GRAFANA_HOSTNAME yet (pre-cutover)"
fi

if ((failures > 0)); then
  echo "VM stack validation failed: $failures problem(s)." >&2
  exit 1
fi
echo "VM stack validation passed."
