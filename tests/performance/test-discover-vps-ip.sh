#!/usr/bin/env bash
# Verify egress discovery uses Test CI SSH secrets and refuses bad/mismatched IP.
set -Eeuo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin"
cat > "$work/bin/ssh-keyscan" <<'SH'
#!/usr/bin/env bash
printf 'example.test ssh-ed25519 SYNTHETIC\n'
SH
cat > "$work/bin/ssh" <<'SH'
#!/usr/bin/env bash
# The real test calls two HTTPS IP observers on the remote VPS.
[[ " $* " == *"https://api.ipify.org"* ]] || exit 3
[[ " $* " == *"https://ipv4.icanhazip.com"* ]] || exit 3
printf '%s\n' "${FAKE_EGRESS_IP1:-8.8.8.8}" "${FAKE_EGRESS_IP2:-8.8.8.8}"
SH
chmod +x "$work/bin/ssh" "$work/bin/ssh-keyscan"
export PATH="$work/bin:$PATH"
export SSH_HOST=example.test SSH_PORT=2222 SSH_USER=runner SSH_PRIVATE_KEY=synthetic
out="$("$repo/scripts/performance/discover-vps-ip.sh")"
[[ "$out" == '8.8.8.8' ]] || exit 1
export FAKE_EGRESS_IP2=1.1.1.1
if "$repo/scripts/performance/discover-vps-ip.sh" >/dev/null 2>&1; then
  echo 'Mismatched egress observers were accepted.' >&2;exit 1
fi
export FAKE_EGRESS_IP1=127.0.0.1 FAKE_EGRESS_IP2=127.0.0.1
if "$repo/scripts/performance/discover-vps-ip.sh" >/dev/null 2>&1; then
  echo 'Loopback IP was accepted.' >&2;exit 1
fi
printf 'Mocked VPS dynamic egress-IP validation tests passed.\n'
