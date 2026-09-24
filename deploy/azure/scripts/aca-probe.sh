#!/usr/bin/env bash
# Runs INSIDE the short-lived Container Apps Job rt-netcheck-prod (image
# apache/kafka) to prove private connectivity from the Container Apps
# environment. Delivered by network-probe.sh as base64 in RT_PROBE_B64 so the
# Container Apps/Kubernetes argument expansion ($(...), $$) never touches it.
#
# Environment (rendered into the job by network-probe.sh):
#   MYSQL_HOST, MYSQL_PORT         VM private MySQL endpoint
#   KAFKA_HOST, KAFKA_PORT         VM private Kafka TLS listener
#   KAFKA_CA_PEM                   public Kafka CA certificate
#   EXPECT_APPS                    true: all seven apps must answer; false: skipped
#   KAFKA_BIN (optional)           default /opt/kafka/bin
#
# Every probe logs "PROBE <name> <target> ..." before and "PASS"/"FAIL" after,
# with the target, exit status and non-secret diagnostics on failure.
set -uo pipefail

kafka_bin="${KAFKA_BIN:-/opt/kafka/bin}"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
failures=0

probe_start() { echo "PROBE $1 -> $2"; }
probe_pass() { echo "PASS  $1 -> $2${3:+ ($3)}"; }
probe_fail() {
  local name="$1" target="$2" status="$3" detail_file="${4:-}"
  echo "FAIL  $name -> $target (exit $status)"
  if [[ -n "$detail_file" && -s "$detail_file" ]]; then
    tail -n 25 "$detail_file" | sed 's/^/      | /'
  fi
  failures=$((failures + 1))
}

tcp_probe() {
  local name="$1" host="$2" port="$3" status
  probe_start "$name" "$host:$port (tcp)"
  timeout 5 bash -c "exec 3<>/dev/tcp/$host/$port" 2>"$tmp/tcp.err"
  status=$?
  if ((status == 0)); then probe_pass "$name" "$host:$port" "tcp connect"; return 0; fi
  [[ $status == 124 ]] && echo "timed out after 5s" >>"$tmp/tcp.err"
  probe_fail "$name" "$host:$port" "$status" "$tmp/tcp.err"
  return 1
}

# Deterministic Kafka TLS round trip, same pattern as the VM smoke test: end
# offset -> unique token -> synchronous produce (acks=all) -> consume exactly
# one record from that offset in assign mode (no consumer group) -> exact match.
kafka_tls_probe() {
  local name=kafka-tls target="$KAFKA_HOST:$KAFKA_PORT" topic=researchtrack.deployment-smoke
  local offset token records status
  probe_start "$name" "$target (TLS produce/consume, topic $topic)"
  export KAFKA_HEAP_OPTS="${KAFKA_HEAP_OPTS:--Xmx256m}"

  printf '%s\n' "$KAFKA_CA_PEM" >"$tmp/ca.crt"
  printf 'security.protocol=SSL\nssl.truststore.type=PEM\nssl.truststore.location=%s\n' "$tmp/ca.crt" >"$tmp/client.properties"
  : >"$tmp/kafka.err"

  "$kafka_bin/kafka-get-offsets.sh" --bootstrap-server "$target" --command-config "$tmp/client.properties" \
    --topic "$topic" --partitions 0 --time -1 >"$tmp/offsets.out" 2>>"$tmp/kafka.err"
  status=$?
  offset="$(awk -F: -v t="$topic" '$1 == t && $2 == 0 {print $3}' "$tmp/offsets.out" | tr -d '\r')"
  if ((status != 0)) || [[ ! "$offset" =~ ^[0-9]+$ ]]; then
    { echo "step: get end offset over TLS"; echo "stdout:"; cat "$tmp/offsets.out"; } >>"$tmp/kafka.err"
    probe_fail "$name" "$target" "$status" "$tmp/kafka.err"
    return 1
  fi

  token="aca-probe-$(date -u +%Y%m%dT%H%M%SZ)-$RANDOM$RANDOM"
  "$kafka_bin/kafka-console-producer.sh" --bootstrap-server "$target" --producer.config "$tmp/client.properties" \
    --topic "$topic" --sync --request-required-acks all <<<"$token" >"$tmp/producer.out" 2>>"$tmp/kafka.err"
  status=$?
  if ((status != 0)); then
    echo "step: produce token (sync, acks=all)" >>"$tmp/kafka.err"
    probe_fail "$name" "$target" "$status" "$tmp/kafka.err"
    return 1
  fi

  "$kafka_bin/kafka-console-consumer.sh" --bootstrap-server "$target" --consumer.config "$tmp/client.properties" \
    --topic "$topic" --partition 0 --offset "$offset" --max-messages 1 --timeout-ms 30000 \
    </dev/null >"$tmp/consumer.out" 2>>"$tmp/kafka.err"
  status=$?
  records="$(tr -d '\r' <"$tmp/consumer.out" | sed '/^[[:space:]]*$/d')"
  if [[ "$records" != "$token" ]]; then
    { echo "step: consume 1 record at offset $offset (consumer exit $status)"; echo "expected: $token"; echo "consumer stdout:"; cat "$tmp/consumer.out"; } >>"$tmp/kafka.err"
    probe_fail "$name" "$target" "${status:-1}" "$tmp/kafka.err"
    return 1
  fi
  probe_pass "$name" "$target" "token at offset $offset"
}

# GET /health/ready on an app's internal address. Uses curl when the image has
# it, otherwise plain HTTP over bash /dev/tcp.
http_status() {
  local host="$1" port="$2" path="$3"
  if command -v curl >/dev/null 2>&1; then
    curl -s -o /dev/null -m 10 -w '%{http_code}' "http://$host:$port$path"
    return
  fi
  timeout 10 bash -c '
    exec 3<>"/dev/tcp/$1/$2" || exit 1
    printf "GET %s HTTP/1.1\r\nHost: %s\r\nConnection: close\r\n\r\n" "$3" "$1" >&3
    IFS=" " read -r _ code _ <&3 && printf "%s" "$code"' _ "$host" "$port" "$path"
}

app_probe() {
  local service="$1" host="rt-$1-prod" code
  probe_start "app-$service" "http://$host/health/ready"
  code="$(http_status "$host" 80 /health/ready 2>"$tmp/http.err")"
  # The Gateway ingress is HTTPS-only (allowInsecure=false): an HTTP redirect
  # proves it is deployed and routed; the backends must answer ready.
  if [[ "$code" == 200 || ( "$service" == gateway && "$code" =~ ^30[178]$ ) ]]; then
    probe_pass "app-$service" "$host" "HTTP $code"
    return 0
  fi
  echo "HTTP status: '${code:-none}'" >>"$tmp/http.err"
  probe_fail "app-$service" "$host:80" "${code:-none}" "$tmp/http.err"
  return 1
}

for key in MYSQL_HOST MYSQL_PORT KAFKA_HOST KAFKA_PORT KAFKA_CA_PEM; do
  [[ -n "${!key:-}" ]] || { echo "FAIL  config: $key is not set"; exit 2; }
done

echo "== Container Apps environment -> infrastructure VM"
tcp_probe mysql-tcp "$MYSQL_HOST" "$MYSQL_PORT" || true
tcp_probe kafka-tcp "$KAFKA_HOST" "$KAFKA_PORT" && kafka_tls_probe || true

if [[ "${EXPECT_APPS:-false}" == true ]]; then
  echo "== Application Container Apps (EXPECT_APPS=true)"
  for service in gateway auth project github jira meeting submission; do
    app_probe "$service" || true
  done
else
  echo "SKIP  application probes (EXPECT_APPS=${EXPECT_APPS:-false}; apps are not deployed yet)"
fi

if ((failures > 0)); then
  echo "RESULT failed: $failures probe(s)"
  exit 1
fi
echo "RESULT all probes passed"
