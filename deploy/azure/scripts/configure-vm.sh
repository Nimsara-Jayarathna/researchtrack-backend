#!/usr/bin/env bash
# Idempotent OS bootstrap for the infrastructure VM. Runs as root through Azure
# Run Command (no SSH). Safe to re-run on every infrastructure deployment:
#   - never formats a disk that already has a filesystem,
#   - never removes data under /data/researchtrack,
#   - only restarts Docker when its daemon configuration changed.
set -Eeuo pipefail

data_mount=/data/researchtrack
data_label=rt-data
data_disk=/dev/disk/azure/scsi1/lun0   # LUN 0 in modules/vm.bicep

log() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }

log "[1/7] Wait for first-boot provisioning"
if command -v cloud-init >/dev/null; then
  cloud-init status --wait >/dev/null 2>&1 || true
fi

log "[2/7] Packages (Ubuntu archive: Docker, Compose plugin, certbot)"
export DEBIAN_FRONTEND=noninteractive
apt_get() {
  # Wait for unattended-upgrades to release the dpkg lock instead of failing.
  apt-get -o DPkg::Lock::Timeout=600 "$@"
}
apt_get update -q
apt_get upgrade -yq
apt_get install -yq --no-install-recommends \
  docker.io docker-compose-v2 certbot openssl jq curl rsync ca-certificates \
  unattended-upgrades

log "[3/7] Automatic security updates and time sync"
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF
timedatectl set-ntp true 2>/dev/null || true

log "[4/7] Docker daemon (bounded json-file logs)"
daemon_json='{
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "5" },
  "live-restore": true
}'
install -d -m 755 /etc/docker
if [[ ! -f /etc/docker/daemon.json || "$(cat /etc/docker/daemon.json)" != "$daemon_json" ]]; then
  printf '%s\n' "$daemon_json" > /etc/docker/daemon.json
  systemctl enable docker >/dev/null
  systemctl restart docker
fi
systemctl enable --now docker >/dev/null
docker compose version >/dev/null

log "[5/7] Persistent data disk -> $data_mount"
for _ in $(seq 1 30); do
  [[ -e "$data_disk" ]] && break
  sleep 2
done
[[ -e "$data_disk" ]] || { echo "Data disk not found at $data_disk (LUN 0)." >&2; exit 1; }
device="$(readlink -f "$data_disk")"

fs_type="$(blkid -o value -s TYPE "$device" 2>/dev/null || true)"
if [[ -z "$fs_type" ]]; then
  # Only a brand-new disk (no filesystem, no partitions) is ever formatted.
  if lsblk -no NAME "$device" | tail -n +2 | grep -q .; then
    echo "Data disk $device has partitions but no filesystem on the whole device; refusing to format." >&2
    exit 1
  fi
  log "      formatting new empty disk $device"
  mkfs.ext4 -q -L "$data_label" "$device"
  fs_type=ext4
fi
uuid="$(blkid -o value -s UUID "$device")"
[[ -n "$uuid" ]] || { echo "Cannot read UUID of $device." >&2; exit 1; }

install -d -m 755 "$data_mount"
fstab_line="UUID=$uuid $data_mount $fs_type defaults,nofail,x-systemd.device-timeout=30s 0 2"
if ! grep -q "^UUID=$uuid " /etc/fstab; then
  # Drop any stale entry for the mount point, then add the current disk.
  sed -i "\\| $data_mount |d" /etc/fstab
  printf '%s\n' "$fstab_line" >> /etc/fstab
  systemctl daemon-reload
fi
mountpoint -q "$data_mount" || mount "$data_mount"
mountpoint -q "$data_mount" || { echo "$data_mount failed to mount." >&2; exit 1; }

log "[6/7] Directories"
install -d -m 755 /opt/researchtrack
install -d -m 700 /opt/researchtrack/runtime
for dir in mysql kafka prometheus grafana backups; do
  [[ -d "$data_mount/$dir" ]] || install -d -m 750 "$data_mount/$dir"
done

log "[7/7] Summary"
df -h / "$data_mount" | sed 's/^/      /'
echo "VM bootstrap complete."
