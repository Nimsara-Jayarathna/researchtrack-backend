#!/usr/bin/env bash
# Shared VM smoke implementation. Caller supplies compose and INFRA_PRIVATE_IP.
# Kafka CLI output uses tools logging; operations have a bounded timeout.
# shellcheck shell=bash
# Caller supplies compose; shared result variables are consumed by validators.
# shellcheck disable=SC2154,SC2034

kafka_smoke() {
  local topic=researchtrack.deployment-smoke bin=/opt/kafka/bin tls="$INFRA_PRIVATE_IP:9092"
  local token offset out err records
  # The kafka container sets KAFKA_LOG4J_OPTS for the *broker* (console appender
  # on stdout, INFO). CLI JVMs started with `exec` would inherit it and print
  # INFO logs on stdout, mixed with the consumed record. Give the tools Kafka's
  # own tools-log4j.properties (WARN, stderr) and a small heap instead.
  local cli=("$compose" exec -T
    -e "KAFKA_LOG4J_OPTS=-Dlog4j.configuration=file:/opt/kafka/config/tools-log4j.properties"
    -e "KAFKA_HEAP_OPTS=-Xmx256m"
    kafka)
  token="smoke-$(date -u +%Y%m%dT%H%M%SZ)-$RANDOM$RANDOM"
  kafka_log="$(mktemp)"
  out="$(mktemp)"
  err="$(mktemp)"

  # Runs one CLI step with stdout and stderr in separate files; stderr is kept
  # in kafka_log for diagnostics.
  step() {
    local name="$1"
    shift
    : >"$out"
    : >"$err"
    timeout 60 "$@" >"$out" 2>"$err"
    local status=$?
    { echo "--- $name (exit $status) stderr:"; tail -n 15 "$err"; } >>"$kafka_log"
    return "$status"
  }

  step topics "${cli[@]}" "$bin/kafka-topics.sh" --bootstrap-server localhost:29092 \
    --create --if-not-exists --topic "$topic" --partitions 1 --replication-factor 1 \
    --config retention.ms=86400000 </dev/null || { rm -f "$out" "$err"; return 1; }

  step get-offsets "${cli[@]}" "$bin/kafka-get-offsets.sh" --bootstrap-server localhost:29092 \
    --topic "$topic" --partitions 0 --time -1 </dev/null || { rm -f "$out" "$err"; return 1; }
  offset="$(awk -F: -v t="$topic" '$1 == t && $2 == 0 {print $3}' "$out" | tr -d '\r')"
  [[ "$offset" =~ ^[0-9]+$ ]] || { echo "could not read end offset; stdout was:" >>"$kafka_log"; cat "$out" >>"$kafka_log"; rm -f "$out" "$err"; return 1; }

  step producer "${cli[@]}" "$bin/kafka-console-producer.sh" \
    --bootstrap-server "$tls" --producer.config /etc/researchtrack/kafka/client-ssl.properties \
    --topic "$topic" --sync --request-required-acks all <<<"$token" || { rm -f "$out" "$err"; return 1; }

  # --max-messages 1 exits as soon as the record arrives. Its exit status is
  # not trusted either way: only the record on stdout decides.
  step consumer "${cli[@]}" "$bin/kafka-console-consumer.sh" \
    --bootstrap-server "$tls" --consumer.config /etc/researchtrack/kafka/client-ssl.properties \
    --topic "$topic" --partition 0 --offset "$offset" --max-messages 1 --timeout-ms 30000 </dev/null || true

  # Normalize only line endings and blank lines; the record must then be
  # exactly one line equal to the token.
  records="$(tr -d '\r' <"$out" | sed '/^[[:space:]]*$/d')"
  if [[ "$records" != "$token" ]]; then
    {
      echo "expected exactly one record '$token' at offset $offset; consumer stdout was:"
      sed 's/^/  | /' "$out"
    } >>"$kafka_log"
    rm -f "$out" "$err"
    return 1
  fi
  rm -f "$out" "$err"
  kafka_token="$token"
  kafka_offset="$offset"
  kafka_detail="token at offset $offset"
}

# Re-read the exact pre-restart record using the same TLS listener and comparison.
kafka_verify_saved_message() {
  local records out
  out="$(mktemp)"
  timeout 60 "$compose" exec -T \
    -e "KAFKA_LOG4J_OPTS=-Dlog4j.configuration=file:/opt/kafka/config/tools-log4j.properties" \
    -e "KAFKA_HEAP_OPTS=-Xmx256m" kafka /opt/kafka/bin/kafka-console-consumer.sh \
    --bootstrap-server "$INFRA_PRIVATE_IP:9092" \
    --consumer.config /etc/researchtrack/kafka/client-ssl.properties \
    --topic researchtrack.deployment-smoke --partition 0 --offset "$kafka_offset" \
    --max-messages 1 --timeout-ms 30000 </dev/null >"$out" 2>>"$kafka_log" || true
  records="$(tr -d '\r' <"$out" | sed '/^[[:space:]]*$/d')"
  rm -f "$out"
  [[ "$records" == "$kafka_token" ]]
}
