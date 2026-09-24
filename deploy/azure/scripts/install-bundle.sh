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
umask 077

printf '%s' "$RT_BUNDLE_B64" | base64 -d | tar -xz -C "$work" --no-same-owner
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
umask 022
rsync -rlt --delete \
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
