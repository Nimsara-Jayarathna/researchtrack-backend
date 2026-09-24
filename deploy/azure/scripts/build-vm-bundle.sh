#!/usr/bin/env bash
# Builds the two Run Command inputs for reconciling the VM stack:
#   <out-dir>/reconcile-vm.sh   script body with the non-secret bundle inlined
#   <out-dir>/runtime.b64       base64 tar of runtime env files (secret; passed
#                               only as a protected parameter, then deleted)
#
# Usage: build-vm-bundle.sh <validated-env-dir> <out-dir>
# Required environment: INFRA_PRIVATE_IP API_HOSTNAME GRAFANA_HOSTNAME ACA_ENV_DOMAIN
# Optional: LETSENCRYPT_EMAIL, ACA_TLS_VERIFY (on), KAFKA_RETENTION_HOURS (72),
#           PROMETHEUS_RETENTION (15d)
set -euo pipefail

env_dir="${1:?validated env dir required}"
out_dir="${2:?output dir required}"
azure_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
repo_root="$(cd "$azure_dir/../.." && pwd)"

for key in INFRA_PRIVATE_IP API_HOSTNAME GRAFANA_HOSTNAME ACA_ENV_DOMAIN; do
  [[ -n "${!key:-}" ]] || { echo "$key is required." >&2; exit 1; }
done

umask 077
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
mkdir -p "$stage/config" "$stage/runtime" "$out_dir"

# Non-secret configuration. Monitoring rules/dashboards and the MySQL
# reconciliation script are shared with the Test stack, not copied.
cfg="$stage/config"
cp "$azure_dir/vm/compose.yml" "$cfg/"
cp -R "$azure_dir/vm/nginx" "$azure_dir/vm/kafka" "$cfg/"
mkdir -p "$cfg/scripts" "$cfg/prometheus" "$cfg/grafana" "$cfg/mysql"
cp "$azure_dir/vm/scripts/"*.sh "$cfg/scripts/"
cp "$azure_dir/scripts/validate-vm-stack.sh" "$azure_dir/scripts/validate-infrastructure.sh" "$cfg/scripts/"
cp "$azure_dir/vm/prometheus/prometheus.yml.template" "$cfg/prometheus/"
cp -R "$repo_root/deploy/monitoring/rules" "$cfg/prometheus/rules"
cp -R "$repo_root/deploy/monitoring/grafana/provisioning" "$cfg/grafana/provisioning"
cp "$repo_root/deploy/mysql/reconcile-databases.sh" "$cfg/mysql/"

# Runtime values.
cp "$env_dir/mysql.env" "$env_dir/grafana.env" "$stage/runtime/"
cat > "$stage/runtime/infra.env" <<EOF
INFRA_PRIVATE_IP=$INFRA_PRIVATE_IP
API_HOSTNAME=$API_HOSTNAME
GRAFANA_HOSTNAME=$GRAFANA_HOSTNAME
ACA_ENV_DOMAIN=$ACA_ENV_DOMAIN
ACA_TLS_VERIFY=${ACA_TLS_VERIFY:-on}
LETSENCRYPT_EMAIL=${LETSENCRYPT_EMAIL:-}
KAFKA_RETENTION_HOURS=${KAFKA_RETENTION_HOURS:-72}
PROMETHEUS_RETENTION=${PROMETHEUS_RETENTION:-15d}
EOF

bundle_b64="$(tar -C "$cfg" -czf - . | base64 | tr -d '\n')"
{
  printf 'RT_BUNDLE_B64=%q\nexport RT_BUNDLE_B64\n' "$bundle_b64"
  cat "$azure_dir/scripts/install-bundle.sh"
} > "$out_dir/reconcile-vm.sh"

tar -C "$stage/runtime" -czf - . | base64 | tr -d '\n' > "$out_dir/runtime.b64"

echo "VM bundle: $(wc -c < "$out_dir/reconcile-vm.sh") bytes script, $(wc -c < "$out_dir/runtime.b64") bytes protected runtime."
