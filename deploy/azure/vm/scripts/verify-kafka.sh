#!/usr/bin/env bash
# Kafka-only checks. Default: read-only. --smoke writes the dedicated test topic.
# --restart explicitly authorizes restarting only Kafka, with persistence proof.
set -uo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
opt="$(cd "$script_dir/.." && pwd)"
data=/data/researchtrack
compose="$script_dir/compose.sh"
# shellcheck source=kafka-common.sh
. "$script_dir/kafka-common.sh"
# shellcheck source=kafka-identity.sh
. "$script_dir/kafka-identity.sh"
mode="${1:---check}"
[[ $# -le 1 && ( "$mode" == --check || "$mode" == --smoke || "$mode" == --restart ) ]] || {
  echo 'Usage: verify-kafka.sh [--check|--smoke|--restart]' >&2; exit 2;
}
failures=0
pass() { echo "PASS: $*"; }
fail() { echo "FAIL: $*" >&2; failures=$((failures + 1)); }
check() { local label="$1"; shift; if "$@"; then pass "$label"; else fail "$label"; fi; }

if [[ ! -r "$opt/runtime/infra.env" ]]; then
  fail 'runtime/infra.env is absent or unreadable; run as root'; exit 1
fi
# Generated runtime config is read without displaying its contents.
# shellcheck disable=SC1091
. "$opt/runtime/infra.env"
if [[ -z "${INFRA_PRIVATE_IP:-}" || -z "${KAFKA_RETENTION_HOURS:-}" ]]; then
  fail 'INFRA_PRIVATE_IP and KAFKA_RETENTION_HOURS are required'; exit 1
fi
if ! jq -en --arg ip "$INFRA_PRIVATE_IP" '$ip | split(".") | select(length == 4) |
    map(tonumber) | all(.[]; . >= 0 and . <= 255) and
    (.[0] == 10 or (.[0] == 172 and .[1] >= 16 and .[1] <= 31) or (.[0] == 192 and .[1] == 168))' >/dev/null 2>&1; then
  fail 'INFRA_PRIVATE_IP must be an RFC1918 IPv4 address'; exit 1
fi
[[ "$KAFKA_RETENTION_HOURS" =~ ^[0-9]+$ ]] || { fail 'KAFKA_RETENTION_HOURS must be numeric'; exit 1; }
pass 'runtime IP and retention are present and valid'
id="$("$compose" ps -q kafka 2>/dev/null)" || id=""
[[ -n "$id" ]] || { fail 'Kafka container is absent'; exit 1; }
inspection="$(docker inspect "$id" 2>/dev/null)" || { fail 'Kafka container cannot be inspected'; exit 1; }
json_check() {
  local label="$1" query="$2"; shift 2
  if jq -e "$@" "$query" <<<"$inspection" >/dev/null; then pass "$label"; else fail "$label"; fi
}
json_check 'Kafka is running and healthy' '.[0].State | .Running == true and .Health.Status == "healthy"'
json_check 'restart policy and container user' '.[0] | .HostConfig.RestartPolicy.Name == "unless-stopped" and .Config.User == "1000:1000"'
json_check 'Docker publishes only private-IP:9092' '.[0].HostConfig.PortBindings | keys == ["9092/tcp"] and .["9092/tcp"] == [{HostIp: $ip, HostPort: "9092"}]' --arg ip "$INFRA_PRIVATE_IP"
json_check 'running Docker network publication is private-IP:9092 only' '.[0].NetworkSettings.Ports | to_entries | map(select(.value != null and (.value | length) > 0)) | . == [{key: "9092/tcp", value: [{HostIp: $ip, HostPort: "9092"}]}]' --arg ip "$INFRA_PRIVATE_IP"
json_check 'persistent data, identity and restricted configuration mounts' '.[0].Mounts as $m |
  ($m | length) == 5 and
  any($m[]; .Source == $data + "/kafka/data" and .Destination == "/var/lib/kafka/data" and .RW == true) and
  any($m[]; .Source == $data + "/kafka/cluster-id" and .Destination == "/etc/kafka/cluster-id" and .RW == false) and
  any($m[]; .Source == $opt + "/kafka" and .Destination == "/etc/researchtrack/kafka" and .RW == false) and
  any($m[]; .Source == $opt + "/runtime/kafka" and .Destination == "/etc/researchtrack/kafka-runtime" and .RW == false) and
  any($m[]; .Source == $data + "/kafka/tls/container" and .Destination == "/etc/researchtrack/kafka-tls" and .RW == false)' --arg data "$data" --arg opt "$opt"

# Docker can publish through NAT without a userspace ss socket. Docker bindings
# above establish publication; ss separately catches other unexpected listeners.
if listeners="$(ss -Htln 2>/dev/null | awk '{print $4}')"; then
  bad="$(awk -v ip="$INFRA_PRIVATE_IP" '
    /:(9092|9093|29092)$/ {if ($0 != ip ":9092") print}' <<<"$listeners")"
  [[ -z "$bad" ]] && pass 'host sockets have no unexpected Kafka exposure' || fail 'host sockets expose an unexpected Kafka address/port'
else
  fail 'cannot inspect host sockets'
fi
check 'persistent data disk mounted' mountpoint -q "$data"
check 'Kafka persistent data directory exists' test -d "$data/kafka/data"
check 'persisted cluster identity matches KRaft metadata' kafka_identity_safe "$data/kafka"
check 'KRaft metadata exists' test -s "$data/kafka/data/meta.properties"
check 'Kafka data owner/mode' test "$(stat -c '%u:%g:%a' "$data/kafka/data" 2>/dev/null)" = 1000:1000:750
check 'runtime broker config permissions' test "$(stat -c '%u:%g:%a' "$opt/runtime/kafka/server.properties" 2>/dev/null)" = 0:1000:640
check 'mounted keystore permissions' test "$(stat -c '%u:%g:%a' "$data/kafka/tls/container/broker.p12" 2>/dev/null)" = 0:1000:440
check 'CA private-key permissions' test "$(stat -c '%u:%g:%a' "$data/kafka/tls/ca.key" 2>/dev/null)" = 0:0:600
check 'keystore password permissions' test "$(stat -c '%u:%g:%a' "$data/kafka/tls/keystore.pass" 2>/dev/null)" = 0:0:600
props="$opt/runtime/kafka/server.properties"
for expected in 'process.roles=broker,controller' 'node.id=1' 'controller.quorum.voters=1@localhost:9093' \
  'controller.listener.names=CONTROLLER' 'inter.broker.listener.name=INTERNAL' \
  'listeners=EXTERNAL://0.0.0.0:9092,INTERNAL://0.0.0.0:29092,CONTROLLER://localhost:9093' \
  "advertised.listeners=EXTERNAL://$INFRA_PRIVATE_IP:9092,INTERNAL://kafka:29092" \
  'listener.security.protocol.map=EXTERNAL:SSL,INTERNAL:PLAINTEXT,CONTROLLER:PLAINTEXT' \
  'ssl.client.auth=none' 'ssl.keystore.type=PKCS12' \
  'ssl.keystore.location=/etc/researchtrack/kafka-tls/broker.p12' \
  'log.dirs=/var/lib/kafka/data' 'default.replication.factor=1' \
  "log.retention.hours=$KAFKA_RETENTION_HOURS"; do
  check "broker setting ${expected%%=*}" grep -Fxq -- "$expected" "$props"
done
for expected in 'security.protocol=SSL' 'ssl.truststore.type=PEM' \
  'ssl.truststore.location=/etc/researchtrack/kafka-tls/ca.crt' 'ssl.endpoint.identification.algorithm=https'; do
  check "TLS client setting ${expected%%=*}" grep -Fxq -- "$expected" "$opt/kafka/client-ssl.properties"
done
if openssl verify -CAfile "$data/kafka/tls/ca.crt" "$data/kafka/tls/broker.crt" >/dev/null 2>&1; then pass 'broker certificate chain'; else fail 'broker certificate chain'; fi
if openssl x509 -in "$data/kafka/tls/broker.crt" -noout -checkip "$INFRA_PRIVATE_IP" >/dev/null 2>&1; then pass 'broker certificate private-IP identity'; else fail 'broker certificate private-IP identity'; fi
if openssl x509 -in "$data/kafka/tls/broker.crt" -noout -checkend 2592000 >/dev/null 2>&1; then pass 'broker certificate valid for next 30 days'; else fail 'broker certificate expires within 30 days'; fi
if timeout 10 openssl s_client -connect "$INFRA_PRIVATE_IP:9092" -CAfile "$data/kafka/tls/ca.crt" \
    -verify_return_error -verify_ip "$INFRA_PRIVATE_IP" </dev/null >/dev/null 2>&1; then
  pass 'private listener TLS handshake verifies trust and IP'
else
  fail 'private listener TLS handshake failed (check SAN, trust, expiry, binding)'
fi
if timeout 60 "$compose" exec -T kafka sh -c '
    for f in /etc/researchtrack/kafka/client-ssl.properties /etc/researchtrack/kafka-runtime/server.properties \
      /etc/researchtrack/kafka-tls/broker.p12 /etc/researchtrack/kafka-tls/ca.crt; do
      [ -r "$f" ] || exit 1
    done' </dev/null; then pass 'container user can read required configuration'; else fail 'container cannot read required configuration'; fi
if timeout 60 "$compose" exec -T \
    -e KAFKA_LOG4J_OPTS=-Dlog4j.configuration=file:/opt/kafka/config/tools-log4j.properties \
    -e KAFKA_HEAP_OPTS=-Xmx256m kafka /opt/kafka/bin/kafka-broker-api-versions.sh \
    --bootstrap-server localhost:29092 </dev/null >/dev/null 2>&1; then pass 'internal broker API available'; else fail 'internal broker API unavailable'; fi
if "$script_dir/kafka-topics.sh" list >/dev/null; then pass 'topic listing available'; else fail 'cannot list topics'; fi

echo 'NOT VERIFIED: effective Azure NSG restrictions; inspect Azure NIC effective NSG separately'
echo 'NOT VERIFIED: Container Apps connectivity; run the approved existing network probe'
kafka_log=""; kafka_detail=""
trap '[[ -z "$kafka_log" ]] || rm -f "$kafka_log"' EXIT
smoke_check() {
  if kafka_smoke; then pass "TLS producer/consumer ($kafka_detail)"; return 0; fi
  fail 'TLS producer/consumer round trip'
  echo 'Check Kafka CLI access, CA trust, topic partitions, network and concurrent smoke operations.' >&2
  return 1
}

# Every preflight must pass before ANY smoke writes or restart operation.
if ((failures == 0)) && [[ "$mode" != --check ]]; then
  if [[ "$mode" == --restart ]]; then
    # Serialize cooperating operators on this VM; ACA workflow has shared
    # concurrency but should not run during a maintenance restart.
    exec 9>/run/lock/researchtrack-kafka-verification.lock
    flock -n 9 || { fail 'another Kafka maintenance verification is running'; exit 1; }
    declare -a unrelated_ids=() unrelated_started=()
    for service in mysql nginx prometheus grafana; do
      other="$("$compose" ps -q "$service" 2>/dev/null)" || other=""
      started="$(docker inspect --format '{{.State.Running}} {{.State.StartedAt}}' "$other" 2>/dev/null)" || started=""
      if [[ -z "$other" || "$started" != true\ * ]]; then fail "$service must be running before restart"; else
        unrelated_ids+=("$other"); unrelated_started+=("$started")
      fi
    done
    identity_before="$(sha256sum "$data/kafka/cluster-id" "$data/kafka/data/meta.properties")" || fail 'cannot hash persisted identity before restart'
  fi
  if ((failures == 0)) && smoke_check; then
    if [[ "$mode" == --restart ]]; then
      # No up/down/recreate, dependencies, pull, formatting or data deletion.
      if "$compose" restart --no-deps kafka; then
        healthy=false
        for ((attempt=1; attempt<=60; attempt++)); do
          status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{end}}' "$id" 2>/dev/null)" || status=""
          if [[ "$status" == healthy ]]; then healthy=true; break; fi
          sleep 3
        done
        if "$healthy"; then
          pass 'Kafka healthy after independent restart'
          check 'pre-restart message remains consumable over TLS' kafka_verify_saved_message
          rm -f "$kafka_log"; kafka_log=""
          smoke_check || true
        else fail 'Kafka did not recover within 180 seconds; do not reset data'; fi
      else fail 'Kafka restart command failed'; fi
      check 'cluster ID and metadata unchanged' test "$identity_before" = "$(sha256sum "$data/kafka/cluster-id" "$data/kafka/data/meta.properties")"
      for ((i=0; i<${#unrelated_ids[@]}; i++)); do
        current="$(docker inspect --format '{{.State.Running}} {{.State.StartedAt}}' "${unrelated_ids[$i]}" 2>/dev/null)" || current=""
        check "unrelated container ${unrelated_ids[$i]} remained running without restart" test "$current" = "${unrelated_started[$i]}"
      done
    fi
  fi
else
  echo 'SKIPPED: smoke writes (read-only mode or failed preflight)'
fi
[[ "$mode" == --restart ]] || echo 'SKIPPED: independent restart (requires explicit --restart in an approved maintenance window)'
if ((failures)); then echo "FAIL: $failures Kafka check(s) failed" >&2; exit 1; fi
pass 'requested VM Kafka checks passed; Azure checks remain NOT VERIFIED as stated above'
