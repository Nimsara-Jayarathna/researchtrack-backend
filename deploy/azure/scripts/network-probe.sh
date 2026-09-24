#!/usr/bin/env bash
# Proves the Container Apps -> VM direction of private networking from *inside*
# the internal Container Apps environment, using a short-lived Container Apps
# Job (not one of the seven application apps):
#   - Container Apps subnet -> VM:3306 (TCP; TLS is proven by the service
#     dbcheck/migration jobs that use SslMode=Required)
#   - Container Apps subnet -> VM:9092 Kafka, TLS produce/consume with the
#     broker certificate verified against the Kafka CA
#
# Required environment: RESOURCE_GROUP ACA_ENVIRONMENT INFRA_VM INFRA_PRIVATE_IP
set -Eeuo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
for key in RESOURCE_GROUP ACA_ENVIRONMENT INFRA_VM INFRA_PRIVATE_IP; do
  [[ -n "${!key:-}" ]] || { echo "$key is required." >&2; exit 1; }
done

job=rt-netcheck-prod
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The Kafka CA certificate is public material; fetch it from the VM.
printf '%s\n' 'echo "-----RT-CA-BEGIN-----"; cat /data/researchtrack/kafka/tls/ca.crt; echo "-----RT-CA-END-----"' > "$work/ca.sh"
"$script_dir/vm-run.sh" "$RESOURCE_GROUP" "$INFRA_VM" "$work/ca.sh" > "$work/ca.out"
sed -n '/-----RT-CA-BEGIN-----/,/-----RT-CA-END-----/p' "$work/ca.out" | sed '1d;$d' > "$work/ca.crt"
grep -q 'BEGIN CERTIFICATE' "$work/ca.crt" || { echo "Could not read the Kafka CA certificate from the VM." >&2; exit 1; }

env_id="$(az containerapp env show -g "$RESOURCE_GROUP" -n "$ACA_ENVIRONMENT" --query id -o tsv)"
location="$(az containerapp env show -g "$RESOURCE_GROUP" -n "$ACA_ENVIRONMENT" --query location -o tsv)"

probe_script='set -eu
timeout 5 bash -c "</dev/tcp/$MYSQL_HOST/3306" && echo "mysql: tcp $MYSQL_HOST:3306 reachable"
printf "%s\n" "$KAFKA_CA_PEM" > /tmp/ca.crt
printf "security.protocol=SSL\nssl.truststore.type=PEM\nssl.truststore.location=/tmp/ca.crt\n" > /tmp/client.properties
marker="aca-probe-$(date +%s)-$$"
echo "$marker" | /opt/kafka/bin/kafka-console-producer.sh --bootstrap-server "$KAFKA_BOOTSTRAP" \
  --producer.config /tmp/client.properties --topic researchtrack.deployment-smoke
out="$(/opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server "$KAFKA_BOOTSTRAP" \
  --consumer.config /tmp/client.properties --topic researchtrack.deployment-smoke \
  --from-beginning --timeout-ms 30000 2>/dev/null || true)"
printf "%s\n" "$out" | grep -qx "$marker"
echo "kafka: TLS produce/consume via $KAFKA_BOOTSTRAP OK"'

jq -n \
  --arg location "$location" \
  --arg env "$env_id" \
  --arg script "$probe_script" \
  --arg ca "$(cat "$work/ca.crt")" \
  --arg mysql "$INFRA_PRIVATE_IP" \
  --arg kafka "$INFRA_PRIVATE_IP:9092" '{
    location: $location,
    properties: {
      environmentId: $env,
      workloadProfileName: "Consumption",
      configuration: {
        triggerType: "Manual",
        replicaTimeout: 300,
        replicaRetryLimit: 0,
        manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      },
      template: {
        containers: [{
          name: "netcheck",
          image: "apache/kafka:3.9.1",
          command: ["/bin/bash", "-c"],
          args: [$script],
          env: [
            { name: "MYSQL_HOST", value: $mysql },
            { name: "KAFKA_BOOTSTRAP", value: $kafka },
            { name: "KAFKA_CA_PEM", value: $ca },
            { name: "KAFKA_HEAP_OPTS", value: "-Xmx256m" }
          ],
          resources: { cpu: 0.5, memory: "1Gi" }
        }]
      }
    }
  }' > "$work/job.json"

if az containerapp job show -g "$RESOURCE_GROUP" -n "$job" -o none 2>/dev/null; then
  az containerapp job update -g "$RESOURCE_GROUP" -n "$job" --yaml "$work/job.json" -o none
else
  az containerapp job create -g "$RESOURCE_GROUP" -n "$job" --yaml "$work/job.json" -o none
fi

execution="$(az containerapp job start -g "$RESOURCE_GROUP" -n "$job" --query name -o tsv)"
echo "Network probe execution: $execution"
for _ in $(seq 1 60); do
  status="$(az containerapp job execution show -g "$RESOURCE_GROUP" -n "$job" \
    --job-execution-name "$execution" --query properties.status -o tsv)"
  case "$status" in
    Succeeded)
      echo "Container Apps -> MySQL (TCP) and Container Apps -> Kafka (TLS) verified."
      exit 0
      ;;
    Failed|Stopped|Degraded)
      echo "Network probe failed ($status)." >&2
      az containerapp job logs show -g "$RESOURCE_GROUP" -n "$job" --execution "$execution" --container netcheck 2>/dev/null | tail -40 >&2 || true
      exit 1
      ;;
  esac
  sleep 10
done
echo "Timed out waiting for the network probe." >&2
exit 1
