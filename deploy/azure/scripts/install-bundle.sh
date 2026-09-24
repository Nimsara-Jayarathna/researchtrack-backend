# shellcheck shell=bash
# Body of the "reconcile VM stack" Azure Run Command (runs as root on the VM).
#
# build-vm-bundle.sh prepends RT_BUNDLE_B64 (non-secret configuration: compose,
# Nginx, Kafka, Prometheus, Grafana, scripts). RT_RUNTIME_B64 (mysql.env,
# grafana.env, infra.env) arrives as a Run Command *protected* parameter, which
# Azure exposes to the script as an environment variable and never returns in
# the Run Command resource or its output.
set -Eeuo pipefail

opt=/opt/researchtrack
: "${RT_BUNDLE_B64:?configuration bundle missing}"
: "${RT_RUNTIME_B64:?runtime secrets missing (protected parameter)}"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Non-secret configuration: extracted world-readable (see rsync below).
umask 022
printf '%s' "$RT_BUNDLE_B64" | base64 -d | tar -xz -C "$work" --no-same-owner --no-same-permissions
# Runtime secrets: root-only.
umask 077
mkdir -p "$work/runtime"
printf '%s' "$RT_RUNTIME_B64" | base64 -d | tar -xz -C "$work/runtime" --no-same-owner
unset RT_RUNTIME_B64

for path in compose.yml scripts/reconcile-stack.sh scripts/compose.sh \
    scripts/validate-vm-stack.sh scripts/validate-infrastructure.sh \
    mysql/reconcile-databases.sh kafka/server.properties.template \
    prometheus/prometheus.yml.template prometheus/rules grafana/provisioning \
    runtime/infra.env runtime/mysql.env runtime/grafana.env; do
  [[ -e "$work/$path" ]] || { echo "Deployment bundle is missing $path" >&2; exit 1; }
done

[[ -d "$opt" ]] || { echo "$opt missing; run configure-vm.sh first." >&2; exit 1; }

# Sync in place: directories bind-mounted into running containers keep their
# inode (a replaced directory would stay mounted as the old, deleted one).
# Generated/persistent paths are excluded from --delete.
#
# -p with --chmod also repairs permissions of files that are already on the VM
# and unchanged in content: Prometheus (uid 65534), Grafana (472) and the Kafka
# tools (1000) must be able to read the rules, provisioning and client config.
umask 022
rsync -rlpt --delete \
  --chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r \
  --exclude '/runtime/' \
  --exclude '/nginx/conf.d/' \
  --exclude '/nginx/www/' \
  --exclude '/prometheus/prometheus.yml' \
  "$work/" "$opt/"

install -d -m 700 "$opt/runtime"
for file in infra.env mysql.env grafana.env; do
  install -m 600 -o root -g root "$work/runtime/$file" "$opt/runtime/$file"
done
chmod 755 "$opt/scripts"/*.sh

"$opt/scripts/reconcile-stack.sh"
