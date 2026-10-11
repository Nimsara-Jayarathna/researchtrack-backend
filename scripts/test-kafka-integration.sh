#!/usr/bin/env bash
# Isolated TLS Kafka + MySQL. No real env files, Azure calls or production topics.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
for command in docker dotnet openssl python3; do
  command -v "$command" >/dev/null || { echo "NOT EXECUTED: missing $command" >&2; exit 2; }
done
docker info >/dev/null 2>&1 || { echo 'NOT EXECUTED: Docker daemon unavailable; isolated Kafka/MySQL tests require Docker.' >&2; exit 2; }
docker compose version >/dev/null
export RT_KAFKA_TEST_WORK
RT_KAFKA_TEST_WORK="$(mktemp -d "${TMPDIR:-/tmp}/researchtrack-kafka-fixture.XXXXXX")"
export RT_KAFKA_TEST_UID RT_KAFKA_TEST_GID RT_KAFKA_TEST_PASSWORD RT_KAFKA_TEST_CLUSTER_ID
RT_KAFKA_TEST_UID="$(id -u)"
RT_KAFKA_TEST_GID="$(id -g)"
RT_KAFKA_TEST_PASSWORD="$(openssl rand -hex 24)"
RT_KAFKA_TEST_CLUSTER_ID="$(python3 -c 'import base64,uuid;print(base64.urlsafe_b64encode(uuid.uuid4().bytes).decode().rstrip("="))')"
project="rt-kafka-fixture-$(openssl rand -hex 6)"
compose=(docker compose --env-file /dev/null --project-name "$project" -f "$root/tests/Kafka/compose.yml")
cleanup() {
  # Only the unique fixture project/volume and our mktemp directory are removed.
  "${compose[@]}" down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -rf -- "$RT_KAFKA_TEST_WORK"
}
trap cleanup EXIT
mkdir -p "$RT_KAFKA_TEST_WORK/metrics" "$RT_KAFKA_TEST_WORK/monitoring/rules" "$RT_KAFKA_TEST_WORK/tls" "$RT_KAFKA_TEST_WORK/private" "$RT_KAFKA_TEST_WORK/kafka-data" "$RT_KAFKA_TEST_WORK/mysql-init"
cp "$root/tests/Kafka/prometheus.yml" "$RT_KAFKA_TEST_WORK/monitoring/prometheus.yml"
# Only fixture stabilization durations are shortened; expressions and thresholds are unchanged.
sed -E 's/for: [0-9]+[ms]/for: 10s/g' "$root/deploy/monitoring/rules/researchtrack-alerts.yml"   > "$RT_KAFKA_TEST_WORK/monitoring/rules/researchtrack-alerts.yml"
chmod -R a+rX "$RT_KAFKA_TEST_WORK/metrics" "$RT_KAFKA_TEST_WORK/monitoring"
umask 077
openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj '/CN=ResearchTrack isolated Kafka CA' \
  -addext 'basicConstraints=critical,CA:TRUE' -keyout "$RT_KAFKA_TEST_WORK/private/ca.key" \
  -out "$RT_KAFKA_TEST_WORK/tls/ca.crt" >/dev/null 2>&1
openssl req -new -newkey rsa:2048 -nodes -subj '/CN=localhost' \
  -keyout "$RT_KAFKA_TEST_WORK/private/broker.key" -out "$RT_KAFKA_TEST_WORK/private/broker.csr" >/dev/null 2>&1
printf '%s\n' 'subjectAltName=DNS:localhost,IP:127.0.0.1' 'extendedKeyUsage=serverAuth' \
  > "$RT_KAFKA_TEST_WORK/tls/broker.ext"
openssl x509 -req -days 2 -in "$RT_KAFKA_TEST_WORK/private/broker.csr" \
  -CA "$RT_KAFKA_TEST_WORK/tls/ca.crt" -CAkey "$RT_KAFKA_TEST_WORK/private/ca.key" -CAcreateserial \
  -extfile "$RT_KAFKA_TEST_WORK/tls/broker.ext" -out "$RT_KAFKA_TEST_WORK/tls/broker.crt" >/dev/null 2>&1
openssl pkcs12 -export -name kafka -inkey "$RT_KAFKA_TEST_WORK/private/broker.key" \
  -in "$RT_KAFKA_TEST_WORK/tls/broker.crt" -certfile "$RT_KAFKA_TEST_WORK/tls/ca.crt" \
  -passout env:RT_KAFKA_TEST_PASSWORD -out "$RT_KAFKA_TEST_WORK/tls/broker.p12"
cat > "$RT_KAFKA_TEST_WORK/tls/server.properties" <<EOF
process.roles=broker,controller
node.id=1
controller.quorum.voters=1@localhost:9093
listeners=EXTERNAL://:9092,INTERNAL://:29092,CONTROLLER://:9093
advertised.listeners=EXTERNAL://localhost:19092,INTERNAL://kafka:29092
listener.security.protocol.map=EXTERNAL:SSL,INTERNAL:PLAINTEXT,CONTROLLER:PLAINTEXT
controller.listener.names=CONTROLLER
inter.broker.listener.name=INTERNAL
ssl.keystore.type=PKCS12
ssl.keystore.location=/fixture/broker.p12
ssl.keystore.password=$RT_KAFKA_TEST_PASSWORD
ssl.key.password=$RT_KAFKA_TEST_PASSWORD
ssl.truststore.type=PEM
ssl.truststore.location=/fixture/ca.crt
ssl.client.auth=none
log.dirs=/fixture-data
offsets.topic.replication.factor=1
transaction.state.log.replication.factor=1
transaction.state.log.min.isr=1
auto.create.topics.enable=false
group.initial.rebalance.delay.ms=0
EOF
cat > "$RT_KAFKA_TEST_WORK/mysql-init/01-fixture.sql" <<EOF
CREATE DATABASE researchtrack_kafka_github;
CREATE DATABASE researchtrack_kafka_jira;
CREATE USER 'fixture'@'%' IDENTIFIED BY '$RT_KAFKA_TEST_PASSWORD';
GRANT ALL ON researchtrack_kafka_github.* TO 'fixture'@'%';
GRANT ALL ON researchtrack_kafka_jira.* TO 'fixture'@'%';
EOF
# The MySQL entrypoint runs as its own UID; the isolated init SQL contains only disposable credentials.
chmod 644 "$RT_KAFKA_TEST_WORK/mysql-init/01-fixture.sql"
chmod 755 "$RT_KAFKA_TEST_WORK/mysql-init"
"${compose[@]}" up -d
deadline=$((SECONDS + 180))
for service in kafka mysql; do
  while true; do
    container="$("${compose[@]}" ps -q "$service")"
    state="$(docker inspect --format '{{.State.Health.Status}}' "$container" 2>/dev/null || true)"
    [[ "$state" == healthy ]] && break
    if ((SECONDS >= deadline)); then echo "FAIL: isolated $service fixture did not become healthy." >&2; exit 1; fi
    sleep 2
  done
done
"${compose[@]}" exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:29092   --create --if-not-exists --topic researchtrack.deployment-smoke --partitions 1 --replication-factor 1
for domain in github jira; do
  "${compose[@]}" exec -T kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:29092 \
    --create --if-not-exists --topic "researchtrack.$domain.events.v1" --partitions 1 \
    --replication-factor 1 --config retention.ms=259200000
done
export RT_KAFKA_TEST_PROMETHEUS=http://127.0.0.1:19090
export RT_KAFKA_TEST_METRICS_DIRECTORY="$RT_KAFKA_TEST_WORK/metrics"
export RT_KAFKA_TEST_ENABLE=1 RT_KAFKA_TEST_ENDPOINT=localhost:19092 RT_KAFKA_TEST_CA="$RT_KAFKA_TEST_WORK/tls/ca.crt"
export RT_KAFKA_TEST_GITHUB_CONNECTION="Server=127.0.0.1;Port=13307;Database=researchtrack_kafka_github;User=fixture;Password=$RT_KAFKA_TEST_PASSWORD;SslMode=Disabled"
export RT_KAFKA_TEST_JIRA_CONNECTION="Server=127.0.0.1;Port=13307;Database=researchtrack_kafka_jira;User=fixture;Password=$RT_KAFKA_TEST_PASSWORD;SslMode=Disabled"
cd "$root"
dotnet restore ResearchTrack.sln --verbosity minimal
dotnet build ResearchTrack.sln -c Release --no-restore --verbosity minimal
for service in GitHub Jira; do
  dotnet test "tests/ResearchTrack.${service}Service.Tests" -c Release --no-build \
    --filter 'Category=KafkaIntegration' --logger "trx;LogFileName=kafka-$service.trx" \
    --results-directory "$root/artifacts/kafka-integration"
done
# Broker failure is simulated ONLY inside this newly created disposable project.
python3 "$root/tests/Kafka/verify_observability.py" healthy
"${compose[@]}" stop kafka
python3 "$root/tests/Kafka/verify_observability.py" unavailable
"${compose[@]}" start kafka
python3 "$root/tests/Kafka/verify_observability.py" healthy
mkdir -p "$root/artifacts/kafka-integration/metrics"
cp "$RT_KAFKA_TEST_WORK/metrics/"*.prom "$root/artifacts/kafka-integration/metrics/"
echo 'PASS: isolated TLS Kafka application and observability checks. This is not Azure production evidence.'
