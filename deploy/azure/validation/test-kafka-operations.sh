#!/usr/bin/env bash
# Offline regression tests of real Kafka operation scripts, with synthetic state.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
scripts="$work/opt/scripts"
mkdir -p "$scripts" "$work/data/kafka/data" "$work/bin" "$work/opt/runtime/kafka" "$work/opt/kafka" "$work/data/kafka/tls/container"
cp "$repo_root/deploy/azure/vm/scripts/"*.sh "$scripts/"
# Rewrite only the fixed test data path; production scripts retain /data/researchtrack.
sed "s|^data=/data/researchtrack$|data=$work/data|" "$scripts/verify-kafka.sh" > "$work/verify"
mv "$work/verify" "$scripts/verify-kafka.sh"
export KTEST="$work" PATH="$work/bin:$PATH"
failures=0
pass() { echo "ok   $*"; }
fail() { echo "FAIL $*"; failures=$((failures+1)); }

# TLS identity guards operate only on synthetic files.
# Identity safety: all operations are inspections; verify no generated ID appears.
# shellcheck source=../vm/scripts/kafka-identity.sh
. "$repo_root/deploy/azure/vm/scripts/kafka-identity.sh"
root="$work/identity"; mkdir -p "$root/data"
if kafka_identity_safe "$root"; then pass 'empty first deployment allowed'; else fail 'fresh identity'; fi
printf 'data\n' > "$root/data/log"
if kafka_identity_safe "$root" 2>/dev/null; then fail 'lost identity accepted existing data'; else pass 'existing data blocks identity generation'; fi
rm "$root/data/log"; touch "$root/cluster-id"
if kafka_identity_safe "$root" 2>/dev/null; then fail 'empty identity accepted'; else pass 'empty existing identity blocked'; fi
printf 'abcdefghijklmnopqrstuv\n' > "$root/cluster-id"
printf 'cluster.id=abcdefghijklmnopqrstuv\n' > "$root/data/meta.properties"
if kafka_identity_safe "$root"; then pass 'matching identity preserved'; else fail 'valid identity'; fi
printf 'cluster.id=abcdefghijklmnopqrstuX\n' > "$root/data/meta.properties"
if kafka_identity_safe "$root" 2>/dev/null; then fail 'mismatch accepted'; else pass 'mismatched metadata rejected'; fi
rm "$root/data/meta.properties"; touch "$root/.inputs-hash"
if kafka_identity_safe "$root" 2>/dev/null; then fail 'lost metadata accepted'; else pass 'lost deployed metadata rejected'; fi

tlsroot="$work/tls-identity"; mkdir -p "$tlsroot/tls"
if kafka_tls_identity_safe "$tlsroot"; then pass 'fresh TLS generation allowed'; else fail 'fresh TLS'; fi
printf test > "$tlsroot/tls/ca.key"
if kafka_tls_identity_safe "$tlsroot" 2>/dev/null; then fail 'partial CA accepted'; else pass 'partial CA rejected'; fi
printf test > "$tlsroot/tls/ca.crt"; printf test > "$tlsroot/tls/broker.p12"
if kafka_tls_identity_safe "$tlsroot" 2>/dev/null; then fail 'missing keystore password accepted'; else pass 'missing keystore password rejected'; fi
printf test > "$tlsroot/tls/keystore.pass"
if kafka_tls_identity_safe "$tlsroot"; then pass 'existing TLS material preserved'; else fail 'valid TLS'; fi
rm "$tlsroot/tls/"*; touch "$tlsroot/.inputs-hash"
if kafka_tls_identity_safe "$tlsroot" 2>/dev/null; then fail 'lost deployed CA accepted'; else pass 'lost deployed CA rejected'; fi

# Real OpenSSL fixture exercises the actual renewal validity function: an IP
# prefix must not satisfy the SAN check. No production certificates are read.
mkdir -p "$work/cert-fixture"
openssl req -x509 -newkey rsa:2048 -nodes -days 60 -subj /CN=synthetic-test \
  -addext subjectAltName=IP:10.20.10.40 -keyout "$work/cert-fixture/key" \
  -out "$work/cert-fixture/broker.crt" >/dev/null 2>&1
cp "$work/cert-fixture/broker.crt" "$work/cert-fixture/ca.crt"
printf synthetic > "$work/cert-fixture/broker.p12"
cert_fn="$(sed -n '/^broker_cert_valid() {/,/^}/p' "$repo_root/deploy/azure/vm/scripts/reconcile-stack.sh")"
[[ -n "$cert_fn" ]] || { fail 'certificate validity function missing'; exit 1; }
eval "$cert_fn"
export tls="$work/cert-fixture"
export INFRA_PRIVATE_IP=10.20.10.40
if broker_cert_valid; then pass 'real certificate chain and exact SAN accepted'; else fail 'matching certificate rejected'; fi
export INFRA_PRIVATE_IP=10.20.10.4
if broker_cert_valid; then fail 'IP prefix accepted as SAN'; else pass 'real certificate rejects private-IP prefix mismatch'; fi

cat > "$scripts/compose.sh" <<'STUB'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$KTEST/calls"
case "$1" in
  ps) echo "$3-id"; exit 0 ;;
  restart) [[ "$*" == 'restart --no-deps kafka' ]] || exit 9; touch "$KTEST/restarted"; exit 0 ;;
esac
[[ "$1 $2" == 'exec -T' ]] || exit 9
shift 2
while [[ "$1" == -e ]]; do shift 2; done
[[ "$1" == kafka ]] || exit 9
shift
tool="$(basename "$1")"; shift
case "$tool" in
  sh|kafka-broker-api-versions.sh) exit 0 ;;
  kafka-topics.sh)
    case " $* " in
      *' --list '*) [[ "${MODE:-}" != topic-error ]] || exit 1; echo researchtrack.deployment-smoke; [[ ! -e "$KTEST/topic" ]] || cat "$KTEST/topic" ;;
      *' --create '*) [[ "$*" == *'--if-not-exists'* && "$*" == *'--replication-factor 1'* ]] || exit 9
        topic=""; while (($#)); do if [[ "$1" == --topic ]]; then topic="$2"; break; fi; shift; done; printf '%s\n' "$topic" > "$KTEST/topic" ;;
      *' --describe '*)
        if [[ "${MODE:-}" == partition-mismatch ]]; then p=2; else p=3; fi
        rf=1; retention=86400000
        [[ "${MODE:-}" != replication-mismatch ]] || rf=2
        [[ "${MODE:-}" != retention-prefix ]] || retention=864000000
        echo "Topic: owned.events PartitionCount: $p ReplicationFactor: $rf Configs: retention.ms=$retention" ;;
      *) exit 9 ;;
    esac ;;
  kafka-configs.sh) echo 'retention.ms=86400000' ;;
  kafka-get-offsets.sh) echo researchtrack.deployment-smoke:0:41 ;;
  kafka-console-producer.sh) cat > "$KTEST/produced" ;;
  kafka-console-consumer.sh)
    if [[ "${MODE:-}" == lost-message && -e "$KTEST/restarted" ]]; then echo wrong; else cat "$KTEST/produced"; fi ;;
  *) exit 9 ;;
esac
STUB
cat > "$work/bin/docker" <<'STUB'
#!/usr/bin/env bash
[[ "$1" == inspect ]] || exit 9
if [[ "$2" != --format ]]; then cat "$KTEST/inspection.json"; exit; fi
case "$3" in
  *Health*)
    if [[ "${MODE:-}" == no-recovery && -e "$KTEST/restarted" ]]; then echo unhealthy; else echo healthy; fi ;;
  *StartedAt*)
    if [[ "${MODE:-}" == unrelated-restart && -e "$KTEST/restarted" ]]; then echo 'true changed'; else echo 'true original'; fi ;;
  *) exit 9 ;;
esac
STUB
cat > "$work/bin/stat" <<'STUB'
#!/usr/bin/env bash
case "${*: -1}" in
  */kafka/data) echo 1000:1000:750 ;;
  */server.properties) echo 0:1000:640 ;;
  */broker.p12) echo 0:1000:440 ;;
  */ca.key|*/keystore.pass) echo 0:0:600 ;;
  *) exit 9 ;;
esac
STUB
cat > "$work/bin/ss" <<'STUB'
#!/usr/bin/env bash
case "${MODE:-}" in
  host-controller) echo 'LISTEN 0 128 0.0.0.0:9093 0.0.0.0:*' ;;
  host-public) echo 'LISTEN 0 128 [::]:9092 [::]:*' ;;
  socket-error) exit 1 ;;
  *) : ;; # Docker NAT with no userspace socket is valid.
esac
STUB
cat > "$work/bin/openssl" <<'STUB'
#!/usr/bin/env bash
[[ "${MODE:-}" != tls-error ]]
STUB
printf '#!/bin/sh\nexit 0\n' > "$work/bin/mountpoint"
printf '#!/bin/sh\nexit 0\n' > "$work/bin/sleep"
printf '#!/bin/sh\nexit 0\n' > "$work/bin/flock"
cat > "$work/bin/timeout" <<'STUB'
#!/usr/bin/env bash
shift; exec "$@"
STUB
# Linux-only sha256sum is absent on some macOS installations.
cat > "$work/bin/sha256sum" <<'STUB'
#!/usr/bin/env bash
shasum -a 256 "$@"
STUB
chmod +x "$scripts/"*.sh "$work/bin/"*
# Keep the lock in the disposable fixture too.
sed "s|/run/lock/researchtrack-kafka-verification.lock|$work/restart.lock|" "$scripts/verify-kafka.sh" > "$work/verify"
mv "$work/verify" "$scripts/verify-kafka.sh"; chmod +x "$scripts/verify-kafka.sh"
printf 'INFRA_PRIVATE_IP=10.20.10.4\nKAFKA_RETENTION_HOURS=72\n' > "$work/opt/runtime/infra.env"
sed -e 's/__INFRA_PRIVATE_IP__/10.20.10.4/g' -e 's/__KAFKA_RETENTION_HOURS__/72/g' \
  -e 's/__KAFKA_KEYSTORE_PASSWORD__/SYNTHETIC_TEST_ONLY/g' "$repo_root/deploy/azure/vm/kafka/server.properties.template" > "$work/opt/runtime/kafka/server.properties"
cp "$repo_root/deploy/azure/vm/kafka/client-ssl.properties" "$work/opt/kafka/"
printf 'abcdefghijklmnopqrstuv\n' > "$work/data/kafka/cluster-id"
printf 'cluster.id=abcdefghijklmnopqrstuv\n' > "$work/data/kafka/data/meta.properties"

fixture() {
  python3 - "$work" "$1" <<'PY'
import json,sys
w, mode=sys.argv[1:]
bindings={'9092/tcp':[{'HostIp':'10.20.10.4','HostPort':'9092'}]}
if mode=='public-binding': bindings['9092/tcp'][0]['HostIp']='0.0.0.0'
if mode=='missing-binding': bindings={}
if mode=='controller-binding': bindings['9093/tcp']=[{'HostIp':'0.0.0.0','HostPort':'9093'}]
mounts=[]
for source,dest,rw in [('data/kafka/data','/var/lib/kafka/data',True),('data/kafka/cluster-id','/etc/kafka/cluster-id',False),('opt/kafka','/etc/researchtrack/kafka',False),('opt/runtime/kafka','/etc/researchtrack/kafka-runtime',False),('data/kafka/tls/container','/etc/researchtrack/kafka-tls',False)]:
    mounts.append(dict(Source=w+'/'+source,Destination=dest,RW=rw))
if mode=='wrong-mount': mounts[0]['Source']=w+'/empty'
v=[dict(State=dict(Running=True,Health=dict(Status='unhealthy' if mode=='unhealthy' else 'healthy')),HostConfig=dict(PortBindings=bindings,RestartPolicy=dict(Name='unless-stopped')),Config=dict(User='1000:1000'),NetworkSettings=dict(Ports=bindings),Mounts=mounts)]
json.dump(v,open(w+'/inspection.json','w'))
PY
}
run_case() {
  local mode="$1" flag="$2" expected="$3" output rc
  rm -f "$work/restarted" "$work/calls" "$work/produced"
  fixture "$mode"
  set +e
  output="$(MODE="$mode" bash "$scripts/verify-kafka.sh" "$flag" 2>&1)"; rc=$?
  set -e
  if [[ "$rc" == "$expected" ]]; then pass "$flag $mode -> $rc"; else fail "$flag $mode expected $expected got $rc"; echo "$output"; fi
  [[ "$output" != *SYNTHETIC_TEST_ONLY* ]] || fail 'credential printed'
  if [[ "$flag" == --check ]]; then
    [[ ! -e "$work/restarted" && ! -e "$work/produced" ]] || fail 'default check mutated broker'
  fi
  if [[ "$expected" != 0 && "$mode" != lost-message && "$mode" != unrelated-restart && "$mode" != no-recovery ]]; then
    [[ ! -e "$work/restarted" ]] || fail 'restart despite failed preflight'
  fi
}
run_case ok --check 0
run_case ok --smoke 0
run_case ok --restart 0
[[ -e "$work/restarted" && "$(grep -c '^restart --no-deps kafka$' "$work/calls")" == 1 ]] || fail 'restart command scope'
for mode in public-binding missing-binding controller-binding host-controller host-public socket-error wrong-mount unhealthy tls-error; do
  run_case "$mode" --restart 1
done
run_case lost-message --restart 1
run_case unrelated-restart --restart 1
run_case no-recovery --restart 1

fixture ok
bash "$scripts/kafka-topics.sh" create owned.events 3 24 >/dev/null
bash "$scripts/kafka-topics.sh" create owned.events 3 24 >/dev/null
if bash "$scripts/kafka-topics.sh" exists owned.events >/dev/null; then pass 'idempotent creation and existence'; else fail 'idempotent creation/existence'; fi
bash "$scripts/kafka-topics.sh" describe owned.events >/dev/null
bash "$scripts/kafka-topics.sh" config owned.events >/dev/null && pass 'describe and configuration inspection'
if bash "$scripts/kafka-topics.sh" exists missing.events >/dev/null; then fail 'missing topic accepted'; else pass 'missing topic returns nonzero'; fi
if MODE=topic-error bash "$scripts/kafka-topics.sh" exists owned.events >/dev/null; then fail 'admin failure accepted'; else pass 'admin failure returns nonzero'; fi
if MODE=partition-mismatch bash "$scripts/kafka-topics.sh" create owned.events 3 >/dev/null 2>&1; then fail 'existing partition mismatch accepted'; else pass 'existing partition mismatch rejected'; fi
if bash "$scripts/kafka-topics.sh" create owned.events 3 48 >/dev/null 2>&1; then fail 'existing retention mismatch accepted'; else pass 'existing retention mismatch rejected'; fi
if MODE=replication-mismatch bash "$scripts/kafka-topics.sh" create owned.events 3 >/dev/null 2>&1; then fail 'replication mismatch accepted'; else pass 'single-broker replication enforced'; fi
if MODE=retention-prefix bash "$scripts/kafka-topics.sh" create owned.events 3 24 >/dev/null 2>&1; then fail 'retention prefix accepted'; else pass 'retention must match entire value'; fi
for args in 'delete owned.events' 'create ..' 'create bad/name' 'create good -1' 'create good 0' 'create good 3 0'; do
  # Word splitting intentional: these are fixed synthetic CLI arguments.
  # shellcheck disable=SC2086
  if bash "$scripts/kafka-topics.sh" $args >/dev/null 2>&1; then fail "invalid arguments accepted: $args"; else pass "invalid arguments rejected: $args"; fi
done
# Bundle test uses only generated synthetic runtime values, never real env files.
mkdir -p "$work/env" "$work/bundle"
printf 'SYNTHETIC_MYSQL=test\n' > "$work/env/mysql.env"
printf 'SYNTHETIC_GRAFANA=test\n' > "$work/env/grafana.env"
INFRA_PRIVATE_IP=10.20.10.4 API_HOSTNAME=api.example.invalid GRAFANA_HOSTNAME=grafana.example.invalid \
  ACA_ENV_DOMAIN=aca.example.invalid KAFKA_RETENTION_HOURS=96 \
  bash "$repo_root/deploy/azure/scripts/build-vm-bundle.sh" "$work/env" "$work/bundle" >/dev/null
if python3 - "$work/bundle" <<'PYTEST'
import base64,io,tarfile,sys
from pathlib import Path
p=Path(sys.argv[1])
script=(p/'reconcile-vm.sh').read_text()
encoded=script.splitlines()[0].split('=',1)[1]
with tarfile.open(fileobj=io.BytesIO(base64.b64decode(encoded)),mode='r:gz') as t:
    for name in ('kafka-common.sh','kafka-identity.sh','kafka-topics.sh','verify-kafka.sh'):
        assert t.getmember('./scripts/'+name).mode==0o755
    assert t.getmember('./kafka/client-ssl.properties').mode==0o644
    assert not any(m.name.endswith('.env') for m in t.getmembers())
with tarfile.open(fileobj=io.BytesIO(base64.b64decode((p/'runtime.b64').read_text())),mode='r:gz') as t:
    assert b'KAFKA_RETENTION_HOURS=96' in t.extractfile('./infra.env').read()
    assert t.getmember('./infra.env').mode==0o600
PYTEST
then pass 'bundle includes Kafka scripts and modes; runtime retention externalized'; else fail 'bundle contract'; fi
((failures == 0)) || exit 1
echo 'All offline Kafka operations cases passed.'
