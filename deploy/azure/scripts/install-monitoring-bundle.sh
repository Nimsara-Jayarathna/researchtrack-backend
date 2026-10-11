# shellcheck shell=bash
# Monitoring-only Azure Run Command body. No runtime secrets are delivered.
set -Eeuo pipefail
opt=/opt/researchtrack
: "${RT_BUNDLE_B64:?configuration bundle missing}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
umask 022
printf '%s' "$RT_BUNDLE_B64" | base64 -d | tar -xz -C "$work" --no-same-owner --no-same-permissions
for path in compose.yml scripts/reconcile-monitoring.sh prometheus/prometheus.yml.template prometheus/rules grafana/provisioning; do
  [[ -e "$work/$path" ]] || { echo "Monitoring bundle missing $path" >&2; exit 1; }
done
[[ -f "$opt/runtime/infra.env" && -x "$opt/scripts/compose.sh" ]] || { echo 'Existing VM stack required.' >&2; exit 1; }
# Save configuration only; persistent monitoring/broker/database data is untouched.
backup="$opt/monitoring-backups/$(date -u +%Y%m%dT%H%M%SZ)-$$"
install -d -m 700 "$backup" "$backup/scripts"
cp "$opt/compose.yml" "$backup/"
cp -R "$opt/prometheus" "$opt/grafana" "$backup/"
cp "$opt/scripts/validate-vm-stack.sh" "$backup/scripts/"
install -m 644 "$work/compose.yml" "$opt/compose.yml"
install -m 644 "$work/prometheus/prometheus.yml.template" "$opt/prometheus/"
rsync -rlpt --delete --chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r "$work/prometheus/rules/" "$opt/prometheus/rules/"
rsync -rlpt --delete --chmod=Du=rwx,Dgo=rx,Fu=rw,Fgo=r "$work/grafana/provisioning/" "$opt/grafana/provisioning/"
install -m 755 "$work/scripts/reconcile-monitoring.sh" "$work/scripts/validate-vm-stack.sh" "$opt/scripts/"
echo "Monitoring configuration backup: $backup"
"$opt/scripts/reconcile-monitoring.sh"
