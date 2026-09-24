#!/usr/bin/env bash
# Tests for the Container Apps network probe (network-probe.sh, aca-probe.sh,
# aca-job.sh) with stubs only: no Azure, Docker or Kafka needed.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
scripts="$repo_root/deploy/azure/scripts"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
failures=0
pass() { echo "ok   $*"; }
fail() { echo "FAIL $*"; failures=$((failures + 1)); }

printf -- '-----BEGIN CERTIFICATE-----\nMIIBsyntheticTESTcertificate\n-----END CERTIFICATE-----\n' > "$work/ca.crt"

# ---------------------------------------------------------------------------
# Stub az (records every call; job state kept in $AZ_STATE)
# ---------------------------------------------------------------------------
mkdir -p "$work/azbin"
cat > "$work/azbin/az" <<'STUB'
#!/usr/bin/env bash
echo "az $*" >> "$AZ_STATE/calls"
args="$*"
case "$args" in
  "account show --query id -o tsv") echo 00000000-0000-0000-0000-000000000000 ;;
  "containerapp env show -g rg-researchtrack-prod -n cae-researchtrack-prod --query id -o tsv")
    echo "/subscriptions/0/resourceGroups/rg-researchtrack-prod/providers/Microsoft.App/managedEnvironments/cae-researchtrack-prod" ;;
  "containerapp env show -g rg-researchtrack-prod -n cae-researchtrack-prod --query location -o tsv") echo eastasia ;;
  "rest --method put"*)
    body="${args#*--body @}"; body="${body%% *}"
    cp "$body" "$AZ_STATE/job-body.json"
    url="${args#*--url }"; url="${url%% *}"; echo "$url" >> "$AZ_STATE/put-urls" ;;
  "rest --method get"*"provisioningState"*) echo Succeeded ;;
  "containerapp job start"*) n=$(( $(cat "$AZ_STATE/starts" 2>/dev/null || echo 0) + 1 )); echo "$n" > "$AZ_STATE/starts"; echo "rt-netcheck-prod-exec$n" ;;
  "containerapp job execution show"*) echo "$EXEC_STATUS" ;;
  "containerapp job logs show"*) printf '%s\n' "$EXEC_LOGS" ;;
  *) echo "unexpected az call: $args" >&2; exit 64 ;;
esac
STUB
chmod +x "$work/azbin/az"
printf '#!/bin/sh\nexit 0\n' > "$work/azbin/sleep"; chmod +x "$work/azbin/sleep"

run_probe() {  # run_probe <state-dir> <EXPECT_APPS> [render-file]
  local state="$1" expect="$2" render="${3:-}"
  mkdir -p "$state"
  PATH="$work/azbin:$PATH" AZ_STATE="$state" RESOURCE_GROUP=rg-researchtrack-prod \
    ACA_ENVIRONMENT=cae-researchtrack-prod INFRA_VM=vm-researchtrack-infra-prod INFRA_PRIVATE_IP=10.20.10.4 \
    EXPECT_APPS="$expect" RT_PROBE_CA_FILE="$work/ca.crt" RT_PROBE_RENDER_ONLY="$render" \
    EXEC_STATUS="${EXEC_STATUS:-Succeeded}" EXEC_LOGS="${EXEC_LOGS:-}" \
    bash "$scripts/network-probe.sh"
}

# 1. No --yaml / ignored CLI overrides anywhere in the job lifecycle.
if grep -nE '^[^#]*--yaml' "$scripts/network-probe.sh" "$scripts/deploy-container-apps.sh" "$scripts/aca-job.sh"; then
  fail "1 --yaml still used"
else
  pass "1 no --yaml in network-probe.sh / deploy-container-apps.sh / aca-job.sh"
fi
EXEC_STATUS=Succeeded EXEC_LOGS="RESULT all probes passed" run_probe "$work/s1" false >/dev/null
if grep -Eq 'containerapp job (create|update)' "$work/s1/calls"; then fail "1 job create/update CLI used"
elif grep -q -- '--yaml' "$work/s1/calls"; then fail "1 --yaml passed to az"
else pass "1 job applied with ARM PUT only ($(grep -c 'rest --method put' "$work/s1/calls") PUT)"; fi

# 2. Every dynamic value is inside the rendered body.
run_probe "$work/s2" false "$work/body-false.json" >/dev/null
run_probe "$work/s2t" true "$work/body-true.json" >/dev/null
python3 - "$work/body-false.json" "$work/body-true.json" "$scripts/aca-probe.sh" "$work/ca.crt" <<'PY' && pass "2 dynamic values rendered into the job body (valid JSON/YAML)" || fail "2 rendered body"
import base64, json, sys
f, t, probe, ca = sys.argv[1:]
for path, expect in ((f, "false"), (t, "true")):
    body = json.load(open(path))
    try:
        import yaml; yaml.safe_load(open(path))  # JSON is YAML; parse check when PyYAML exists
    except ImportError:
        pass
    p = body["properties"]
    assert body["location"] == "eastasia"
    assert p["environmentId"].endswith("/managedEnvironments/cae-researchtrack-prod")
    cfg = p["configuration"]
    assert cfg["triggerType"] == "Manual" and cfg["replicaTimeout"] == 300 and cfg["replicaRetryLimit"] == 0
    assert cfg["manualTriggerConfig"] == {"parallelism": 1, "replicaCompletionCount": 1}
    c = p["template"]["containers"][0]
    assert c["name"] == "netcheck" and c["image"] == "apache/kafka:3.9.1"
    env = {e["name"]: e["value"] for e in c["env"]}
    assert env["MYSQL_HOST"] == "10.20.10.4" and env["MYSQL_PORT"] == "3306"
    assert env["KAFKA_HOST"] == "10.20.10.4" and env["KAFKA_PORT"] == "9092"
    assert env["EXPECT_APPS"] == expect
    assert env["KAFKA_CA_PEM"].strip() == open(ca).read().strip()
    assert base64.b64decode(env["RT_PROBE_B64"]).decode() == open(probe).read()
    launcher = c["args"][0]
    assert "$(" not in launcher and "$$" not in launcher, "launcher must survive $(VAR)/$$ expansion"
PY

# ---------------------------------------------------------------------------
# aca-probe.sh inside a simulated job container
# ---------------------------------------------------------------------------
mkdir -p "$work/cbin" "$work/kbin"
cat > "$work/cbin/timeout" <<'STUB'
#!/usr/bin/env bash
# `timeout N bash -c "exec 3<>/dev/tcp/HOST/PORT"` -> decide by $TCP_OK
last="${*: -1}"; target="${last##*/dev/tcp/}"; target="${target//\//:}"
if [[ " $TCP_OK " == *" $target "* ]]; then exit 0; fi
echo "bash: connect: Connection refused (${target})" >&2
exit 1
STUB
cat > "$work/cbin/curl" <<'STUB'
#!/usr/bin/env bash
last="${*: -1}"; host="${last#http://}"; host="${host%%:*}"; svc="${host#rt-}"; svc="${svc%-prod}"
code_var="HTTP_${svc^^}"; printf '%s' "${!code_var:-000}"
STUB
cat > "$work/kbin/kafka-get-offsets.sh" <<'STUB'
#!/usr/bin/env bash
[[ "$*" == *"--command-config"* ]] || { echo "TLS client config missing" >&2; exit 1; }
echo "INFO noisy log" >&2; echo "researchtrack.deployment-smoke:0:17"
STUB
cat > "$work/kbin/kafka-console-producer.sh" <<'STUB'
#!/usr/bin/env bash
[[ "$*" == *"--sync"* && "$*" == *"--request-required-acks all"* ]] || exit 3
cat > "$KSTATE/produced"
STUB
cat > "$work/kbin/kafka-console-consumer.sh" <<'STUB'
#!/usr/bin/env bash
[[ "$*" == *"--partition 0"* && "$*" == *"--offset 17"* && "$*" == *"--max-messages 1"* ]] || exit 9
echo "[INFO] Assigned to partition researchtrack.deployment-smoke-0" >&2
if [[ "$KAFKA_MODE" == ok ]]; then cat "$KSTATE/produced"; else echo "some-other-record"; fi
echo "Processed a total of 1 messages" >&2
STUB
chmod +x "$work/cbin/"* "$work/kbin/"*

run_container() {  # run_container <name> -> sets $out and $rc
  local name="$1"
  mkdir -p "$work/$name"
  set +e
  out="$(PATH="$work/cbin:$PATH" KAFKA_BIN="$work/kbin" KSTATE="$work/$name" \
    MYSQL_HOST=10.20.10.4 MYSQL_PORT=3306 KAFKA_HOST=10.20.10.4 KAFKA_PORT=9092 \
    KAFKA_CA_PEM="$(cat "$work/ca.crt")" bash "$scripts/aca-probe.sh" 2>&1)"
  rc=$?
  set -e
}
all_apps_ok() { export HTTP_GATEWAY=301 HTTP_AUTH=200 HTTP_PROJECT=200 HTTP_GITHUB=200 HTTP_JIRA=200 HTTP_MEETING=200 HTTP_SUBMISSION=200; }

# 3. MySQL + Kafka OK -> success.
TCP_OK="10.20.10.4:3306 10.20.10.4:9092" KAFKA_MODE=ok EXPECT_APPS=false run_container c3
[[ $rc == 0 && "$out" == *"PASS  mysql-tcp -> 10.20.10.4:3306"* && "$out" == *"PASS  kafka-tls"* && "$out" == *"RESULT all probes passed"* ]] \
  && pass "3 mysql + kafka succeed -> exit 0" || { fail "3 success case"; echo "$out"; }

# 4a. MySQL OK, Kafka TCP refused -> clear kafka diagnostic.
TCP_OK="10.20.10.4:3306" KAFKA_MODE=ok EXPECT_APPS=false run_container c4a
[[ $rc == 1 && "$out" == *"FAIL  kafka-tcp -> 10.20.10.4:9092 (exit 1)"* && "$out" == *"Connection refused"* ]] \
  && pass "4a kafka tcp failure named with target, exit status and error" || { fail "4a"; echo "$out"; }
# 4b. MySQL OK, Kafka TLS round trip wrong record -> diagnostic includes step/stdout.
TCP_OK="10.20.10.4:3306 10.20.10.4:9092" KAFKA_MODE=wrong EXPECT_APPS=false run_container c4b
[[ $rc == 1 && "$out" == *"FAIL  kafka-tls -> 10.20.10.4:9092"* && "$out" == *"consumer stdout:"* && "$out" == *"some-other-record"* ]] \
  && pass "4b kafka tls mismatch shows step, expected token and consumer output" || { fail "4b"; echo "$out"; }

# 5. EXPECT_APPS=false -> no application probes.
TCP_OK="10.20.10.4:3306 10.20.10.4:9092" KAFKA_MODE=ok EXPECT_APPS=false run_container c5
[[ $rc == 0 && "$out" == *"SKIP  application probes"* && "$out" != *"PROBE app-"* ]] \
  && pass "5 EXPECT_APPS=false skips all seven app probes" || { fail "5"; echo "$out"; }

# 6. EXPECT_APPS=true -> all seven required.
all_apps_ok
TCP_OK="10.20.10.4:3306 10.20.10.4:9092" KAFKA_MODE=ok EXPECT_APPS=true run_container c6
n="$(grep -c '^PASS  app-' <<<"$out" || true)"
[[ $rc == 0 && "$n" == 7 ]] && pass "6a EXPECT_APPS=true with all seven apps -> pass" || { fail "6a ($n apps)"; echo "$out"; }
HTTP_JIRA=000 TCP_OK="10.20.10.4:3306 10.20.10.4:9092" KAFKA_MODE=ok EXPECT_APPS=true run_container c6b
[[ $rc == 1 && "$out" == *"FAIL  app-jira -> rt-jira-prod:80"* ]] \
  && pass "6b EXPECT_APPS=true with rt-jira-prod missing -> fail naming it" || { fail "6b"; echo "$out"; }
unset HTTP_GATEWAY HTTP_AUTH HTTP_PROJECT HTTP_GITHUB HTTP_JIRA HTTP_MEETING HTTP_SUBMISSION

# 7. Failed execution -> non-zero exit and the probe output is printed.
set +e
failed_out="$(EXEC_STATUS=Failed EXEC_LOGS=$'PROBE kafka-tcp -> 10.20.10.4:9092 (tcp)\nFAIL  kafka-tcp -> 10.20.10.4:9092 (exit 1)' run_probe "$work/s7" false 2>&1)"
failed_rc=$?
set -e
[[ $failed_rc == 1 && "$failed_out" == *"FAIL  kafka-tcp -> 10.20.10.4:9092"* && "$failed_out" == *"finished with status Failed"* ]] \
  && pass "7 failed execution exits 1 and prints the probe output" || { fail "7"; echo "$failed_out"; }

# 8. Repeated runs are idempotent: same resource, PUT only, one new execution each.
EXEC_STATUS=Succeeded EXEC_LOGS="RESULT all probes passed" run_probe "$work/s8" false >/dev/null
EXEC_STATUS=Succeeded EXEC_LOGS="RESULT all probes passed" run_probe "$work/s8" false >/dev/null
urls="$(sort -u "$work/s8/put-urls" | wc -l | tr -d ' ')"
puts="$(wc -l < "$work/s8/put-urls" | tr -d ' ')"
[[ "$urls" == 1 && "$puts" == 2 && "$(cat "$work/s8/starts")" == 2 ]] && ! grep -Eq 'job (create|update|delete)' "$work/s8/calls" \
  && pass "8 two runs -> same job URL, 2 PUTs, 2 executions, no create/update/delete" || { fail "8"; cat "$work/s8/calls"; }

if ((failures > 0)); then
  echo "$failures network-probe test(s) failed." >&2
  exit 1
fi
echo "All network-probe tests passed."
