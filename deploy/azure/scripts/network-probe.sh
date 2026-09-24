#!/usr/bin/env bash
# Proves the Container Apps -> VM direction of private networking from *inside*
# the internal Container Apps environment, using the short-lived Container Apps
# Job rt-netcheck-prod (not one of the seven application apps). The probe logic
# is aca-probe.sh:
#   - Container Apps -> VM:3306 MySQL (TCP; TLS is proven by the service
#     dbcheck/migration jobs that use SslMode=Required)
#   - Container Apps -> VM:9092 Kafka (TCP, then TLS produce/consume with the
#     broker certificate verified against the Kafka CA)
#   - EXPECT_APPS=true only: all seven application Container Apps answer
#
# The job is applied as one complete ARM resource (aca-job.sh); every dynamic
# value is rendered into that body. No `--yaml`, no competing CLI flags.
#
# Required environment: RESOURCE_GROUP ACA_ENVIRONMENT INFRA_VM INFRA_PRIVATE_IP
# Optional: EXPECT_APPS (default false), RT_PROBE_RENDER_ONLY=<file> (tests:
#           write the job body there and stop before calling Azure)
set -Eeuo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=aca-job.sh
. "$script_dir/aca-job.sh"

for key in RESOURCE_GROUP ACA_ENVIRONMENT INFRA_VM INFRA_PRIVATE_IP; do
  [[ -n "${!key:-}" ]] || { echo "$key is required." >&2; exit 1; }
done
expect_apps="${EXPECT_APPS:-false}"
[[ "$expect_apps" == true || "$expect_apps" == false ]] || { echo "EXPECT_APPS must be true or false." >&2; exit 1; }

job=rt-netcheck-prod
container=netcheck
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The Kafka CA certificate is public material; fetch it from the VM.
if [[ -z "${RT_PROBE_CA_FILE:-}" ]]; then
  printf '%s\n' 'echo "-----RT-CA-BEGIN-----"; cat /data/researchtrack/kafka/tls/ca.crt; echo "-----RT-CA-END-----"' > "$work/ca.sh"
  "$script_dir/vm-run.sh" "$RESOURCE_GROUP" "$INFRA_VM" "$work/ca.sh" > "$work/ca.out"
  sed -n '/-----RT-CA-BEGIN-----/,/-----RT-CA-END-----/p' "$work/ca.out" | sed '1d;$d' > "$work/ca.crt"
else
  cp "$RT_PROBE_CA_FILE" "$work/ca.crt"
fi
grep -q 'BEGIN CERTIFICATE' "$work/ca.crt" || { echo "Could not read the Kafka CA certificate from the VM." >&2; exit 1; }

env_id="$(az containerapp env show -g "$RESOURCE_GROUP" -n "$ACA_ENVIRONMENT" --query id -o tsv)"
location="$(az containerapp env show -g "$RESOURCE_GROUP" -n "$ACA_ENVIRONMENT" --query location -o tsv)"

# The probe script travels base64-encoded in an env var and is decoded by a
# fixed launcher: container args are subject to Kubernetes-style $(VAR) and $$
# expansion, which must never rewrite the probe.
probe_b64="$(base64 < "$script_dir/aca-probe.sh" | tr -d '\n')"
launcher='printf %s "$RT_PROBE_B64" | base64 -d > /tmp/aca-probe.sh && exec bash /tmp/aca-probe.sh'

jq -n \
  --arg location "$location" \
  --arg env "$env_id" \
  --arg launcher "$launcher" \
  --arg probe "$probe_b64" \
  --arg ca "$(cat "$work/ca.crt")" \
  --arg host "$INFRA_PRIVATE_IP" \
  --arg expect "$expect_apps" '{
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
          args: [$launcher],
          env: [
            { name: "RT_PROBE_B64", value: $probe },
            { name: "MYSQL_HOST", value: $host },
            { name: "MYSQL_PORT", value: "3306" },
            { name: "KAFKA_HOST", value: $host },
            { name: "KAFKA_PORT", value: "9092" },
            { name: "KAFKA_CA_PEM", value: $ca },
            { name: "EXPECT_APPS", value: $expect },
            { name: "KAFKA_HEAP_OPTS", value: "-Xmx256m" }
          ],
          resources: { cpu: 0.5, memory: "1Gi" }
        }]
      }
    }
  }' > "$work/job.json"

if [[ -n "${RT_PROBE_RENDER_ONLY:-}" ]]; then
  cp "$work/job.json" "$RT_PROBE_RENDER_ONLY"
  exit 0
fi

echo "Applying Container Apps Job $job (EXPECT_APPS=$expect_apps, target $INFRA_PRIVATE_IP)"
aca_job_put "$RESOURCE_GROUP" "$job" "$work/job.json"

execution="$(aca_job_start "$RESOURCE_GROUP" "$job")"
[[ -n "$execution" ]] || { echo "Could not start $job." >&2; exit 1; }
echo "Network probe execution: $execution"
status="$(aca_job_wait "$RESOURCE_GROUP" "$job" "$execution" 60)"

echo "----- $job/$execution output -----"
aca_job_logs "$RESOURCE_GROUP" "$job" "$execution" "$container"
echo "----------------------------------"

if [[ "$status" == Succeeded ]]; then
  echo "Container Apps -> MySQL and Kafka (TLS) verified$([[ "$expect_apps" == true ]] && echo ', all seven apps reachable')."
  exit 0
fi
echo "Network probe execution $execution finished with status $status. See the PROBE/FAIL lines above." >&2
exit 1
