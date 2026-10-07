#!/usr/bin/env bash
# Shared VPS SSH transport for performance execution and egress-IP discovery.
# Reuses the same four repository secrets as backend-deploy-reusable.yml.
# ssh-keyscan deliberately mirrors Test CI; its first-use trust is NOT a
# cryptographically pinned host identity. See tests/performance/README.md.

k6_ssh_prepare() {
  local key
  for key in SSH_HOST SSH_PORT SSH_USER SSH_PRIVATE_KEY; do
    [[ -n "${!key:-}" ]] || { echo "Missing existing repository SSH secret: $key" >&2; return 2; }
  done
  [[ "$SSH_HOST" =~ ^[a-zA-Z0-9.-]+$ ]] || { echo 'SSH_HOST must be a DNS name or IPv4 address.' >&2; return 2; }
  [[ "$SSH_PORT" =~ ^[0-9]{1,5}$ ]] && ((10#$SSH_PORT > 0 && 10#$SSH_PORT <= 65535)) || {
    echo 'SSH_PORT must be 1..65535.' >&2; return 2;
  }
  [[ "$SSH_USER" =~ ^[a-z_][a-zA-Z0-9_-]*$ ]] || {
    echo 'SSH_USER must be a Unix username.' >&2; return 2;
  }
  k6_ssh_work="$(mktemp -d)"
  chmod 700 "$k6_ssh_work"
  printf '%s\n' "$SSH_PRIVATE_KEY" | tr -d '\r' > "$k6_ssh_work/id_ed25519"
  chmod 600 "$k6_ssh_work/id_ed25519"
  : > "$k6_ssh_work/known_hosts"
  chmod 600 "$k6_ssh_work/known_hosts"
  # The deployment workflow already uses ssh-keyscan for this SAME VPS. Do not
  # use StrictHostKeyChecking=no. A fingerprint pin should be added to both
  # workflows in a future hardening pass if the VPS identity can be provisioned.
  if ! ssh-keyscan -T 15 -H -p "$SSH_PORT" "$SSH_HOST" > "$k6_ssh_work/known_hosts" 2>/dev/null ||
     [[ ! -s "$k6_ssh_work/known_hosts" ]]; then
    echo 'Unable to discover VPS SSH host key.' >&2
    return 1
  fi
  k6_ssh_remote="${SSH_USER}@${SSH_HOST}"
  k6_ssh_args=(-i "$k6_ssh_work/id_ed25519" -p "$SSH_PORT" -o BatchMode=yes
    -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$k6_ssh_work/known_hosts"
    -o ConnectTimeout=15)
  k6_scp_args=(-i "$k6_ssh_work/id_ed25519" -P "$SSH_PORT" -o BatchMode=yes
    -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$k6_ssh_work/known_hosts"
    -o ConnectTimeout=15)
}

k6_ssh_cleanup() {
  if [[ -n "${k6_ssh_work:-}" && -d "$k6_ssh_work" ]]; then
    rm -rf -- "$k6_ssh_work"
  fi
}
