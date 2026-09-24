#!/usr/bin/env bash
# Regression tests for the Kafka TLS smoke test in validate-vm-stack.sh.
#
# Runs the real kafka_smoke function against a stub `compose` that behaves like
# the kafka container: verbose INFO logs, which go to stdout unless the CLI is
# given Kafka's tools-log4j.properties (then stderr). No Docker or Kafka needed.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
validator="$repo_root/deploy/azure/scripts/validate-vm-stack.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

smoke_fn="$(sed -n '/^kafka_smoke() {/,/^}/p' "$validator")"
[[ -n "$smoke_fn" ]] || { echo "kafka_smoke not found in $validator" >&2; exit 1; }

cat > "$work/compose" <<'STUB'
#!/usr/bin/env bash
# Stub for scripts/compose.sh: `exec -T [-e K=V]... kafka <tool> <args>`
shift 2
tools_log4j=false
while [[ "$1" == "-e" ]]; do
  [[ "$2" == KAFKA_LOG4J_OPTS=*tools-log4j.properties ]] && tools_log4j=true
  shift 2
done
shift # service name
tool="$(basename "$1")"
shift

log() {
  # Real behaviour: the broker's log4j config (console appender, stdout, INFO)
  # leaks into CLI JVMs unless tools-log4j.properties is supplied.
  local line="[2026-09-24 13:23:04,$RANDOM] INFO $* (org.apache.kafka.clients.consumer.KafkaConsumer)"
  if [[ "$tools_log4j" == true ]]; then echo "$line" >&2; else echo "$line"; fi
}

case "$tool" in
  kafka-topics.sh)
    log "Created admin client"
    ;;
  kafka-get-offsets.sh)
    log "Kafka version: 3.9.1"
    echo "researchtrack.deployment-smoke:0:41"
    ;;
  kafka-console-producer.sh)
    if [[ "$MODE" == producer-fails ]]; then
      echo "java.nio.file.AccessDeniedException: /etc/researchtrack/kafka/client-ssl.properties" >&2
      exit 1
    fi
    log "ProducerConfig values: security.protocol = SSL"
    cat > "$STATE/produced"
    ;;
  kafka-console-consumer.sh)
    [[ "$*" == *"--partition 0"* && "$*" == *"--offset 41"* && "$*" == *"--max-messages 1"* \
       && "$*" == *"--consumer.config /etc/researchtrack/kafka/client-ssl.properties"* ]] || exit 9
    log "[Consumer clientId=console-consumer] Assigned to partition(s): researchtrack.deployment-smoke-0"
    log "[Consumer clientId=console-consumer] Seeking to offset 41 for partition researchtrack.deployment-smoke-0"
    token="$(cat "$STATE/produced")"
    case "$MODE" in
      ok) printf '%s\n' "$token" ;;
      crlf) printf '%s\r\n\n' "$token" ;;
      wrong) echo "smoke-19990101T000000Z-1" ;;
      extra) printf '%s\n%s\n' "$token" "unexpected-second-record" ;;
      empty) : ;;
    esac
    echo "Processed a total of 1 messages" >&2
    ;;
esac
STUB
chmod +x "$work/compose"

failures=0
run_case() {
  local mode="$1" expect="$2" must_log="${3:-}" output
  mkdir -p "$work/$mode"
  set +e
  output="$(MODE="$mode" STATE="$work/$mode" bash -c "
    compose='$work/compose'
    INFRA_PRIVATE_IP=10.20.10.4
    kafka_log=''
    kafka_detail=''
    $smoke_fn
    if kafka_smoke; then echo \"RESULT pass \$kafka_detail\"; else echo 'RESULT fail'; cat \"\$kafka_log\"; fi
    rm -f \"\$kafka_log\"" 2>&1)"
  set -e
  if ! grep -q "^RESULT $expect" <<<"$output"; then
    echo "FAIL $mode: expected $expect"
    sed 's/^/     /' <<<"$output"
    failures=$((failures + 1))
  elif [[ -n "$must_log" ]] && ! grep -qF -- "$must_log" <<<"$output"; then
    echo "FAIL $mode: diagnostics missing '$must_log'"
    sed 's/^/     /' <<<"$output"
    failures=$((failures + 1))
  else
    echo "ok   $mode -> $expect"
  fi
}

run_case ok pass            # INFO logs + token: token alone on stdout
run_case crlf pass          # harmless CRLF / trailing blank line
run_case wrong fail "consumer stdout was"
run_case extra fail "unexpected-second-record"
run_case empty fail "expected exactly one record"
run_case producer-fails fail "AccessDeniedException"

if ((failures > 0)); then
  echo "$failures Kafka smoke-test case(s) failed." >&2
  exit 1
fi
echo "All Kafka smoke-test cases passed."
