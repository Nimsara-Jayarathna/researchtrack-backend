#!/usr/bin/env bash
# Discover the *egress* IPv4 of the existing Test VPS, not the SSH ingress DNS.
# Writes only the validated IPv4 to stdout, suitable for GITHUB_ENV.
set -Eeuo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/ssh-common.sh"
trap k6_ssh_cleanup EXIT
k6_ssh_prepare
# Two independent HTTPS observers protect against misleading DNS/NAT routing.
# The remote command is static; no remote shell input is interpolated.
public_ip="$(ssh "${k6_ssh_args[@]}" "$k6_ssh_remote" \
  'set -eu; command -v curl >/dev/null; curl -4 -fsS --max-time 15 https://api.ipify.org; printf "\n"; curl -4 -fsS --max-time 15 https://ipv4.icanhazip.com')"
K6_DISCOVERED_IPS="$public_ip" python3 - <<'PY'
import ipaddress
import os
import sys
ips = os.environ['K6_DISCOVERED_IPS'].splitlines()
if len(ips) != 2:
    sys.exit('VPS egress-IP discovery failed: expected exactly two HTTPS responses.')
try:
    parsed = [ipaddress.IPv4Address(item.strip()) for item in ips]
except ipaddress.AddressValueError:
    sys.exit('VPS egress-IP discovery returned an invalid IPv4 address.')
if parsed[0] != parsed[1] or not parsed[0].is_global:
    sys.exit('VPS observers disagree or returned a non-public IPv4 address.')
print(parsed[0])
PY
