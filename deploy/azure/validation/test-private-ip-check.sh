#!/usr/bin/env bash
# Tests the private-IP safety check in deploy/azure/vm/scripts/reconcile-stack.sh
# (the block between "BEGIN private-ip-check" and "END private-ip-check") with a
# stubbed `ip` command. No VM or network changes needed.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
source_script="$repo_root/deploy/azure/vm/scripts/reconcile-stack.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

block="$(sed -n '/# BEGIN private-ip-check/,/# END private-ip-check/p' "$source_script")"
[[ -n "$block" ]] || { echo "private-ip-check block not found in $source_script" >&2; exit 1; }

mkdir -p "$work/bin"
# `ip` stub. SCENARIO chooses when (which attempt) which addresses exist.
cat > "$work/bin/ip" <<'STUB'
#!/usr/bin/env bash
count_file="$STATE/calls"
calls=$(( $(cat "$count_file" 2>/dev/null || echo 0) + 1 ))
echo "$calls" > "$count_file"

if [[ "$*" == "-4 route show" ]]; then
  echo "default via 10.20.10.1 dev eth0 proto dhcp src 10.20.10.4 metric 100"
  exit 0
fi

line() { printf '%s: %s    inet %s brd 255.255.255.255 scope global %s\\       valid_lft forever preferred_lft forever\n' "$1" "$2" "$3" "$2"; }
docker_lines() { line 3 docker0 172.17.0.1/16; line 4 br-5f1e2d 172.18.0.1/16; }

case "$SCENARIO" in
  present) line 2 eth0 10.20.10.4/24; docker_lines ;;
  late)
    # Address appears on the 3rd lookup (guest networking converging).
    (( calls >= 3 )) && line 2 eth0 10.20.10.4/24
    docker_lines ;;
  never) docker_lines ;;
  other-ip) line 2 eth0 10.20.10.5/24; docker_lines ;;
  prefix) line 2 eth0 10.20.10.40/24; docker_lines ;;
  slow-writer)
    # Writes one record at a time after the match, as iproute2 can; the old
    # `ip | grep -q` check under pipefail reported "missing" here (SIGPIPE).
    line 2 eth0 10.20.10.4/24; sleep 0.2; docker_lines ;;
  ip-fails-once)
    (( calls == 1 )) && { echo "Cannot talk to rtnetlink" >&2; exit 1; }
    line 2 eth0 10.20.10.4/24 ;;
esac
STUB
chmod +x "$work/bin/ip"
printf '#!/bin/sh\nexit 0\n' > "$work/bin/sleep"   # no real waiting in tests
chmod +x "$work/bin/sleep"

failures=0
run_case() {
  local name="$1" scenario="$2" raw_ip="$3" expect="$4" must="${5:-}" output state
  state="$work/$name"
  mkdir -p "$state"
  set +e
  output="$(PATH="$work/bin:$PATH" SCENARIO="$scenario" STATE="$state" RAW_IP="$raw_ip" bash -c '
    set -Eeuo pipefail
    log() { printf "%s\n" "$*"; }
    '"$block"'
    ip_value="$(normalize_ipv4 "$RAW_IP")" || { echo "RESULT invalid"; exit 0; }
    if wait_for_private_ip "$ip_value" 12 5; then echo "RESULT pass"; else echo "RESULT fail"; fi
    echo "lookups=$(cat "$STATE/calls")"' 2>&1)"
  set -e
  if ! grep -q "^RESULT $expect\$" <<<"$output"; then
    echo "FAIL $name: expected $expect"; sed 's/^/     /' <<<"$output"; failures=$((failures + 1))
  elif [[ -n "$must" ]] && ! grep -qF -- "$must" <<<"$output"; then
    echo "FAIL $name: output missing '$must'"; sed 's/^/     /' <<<"$output"; failures=$((failures + 1))
  else
    echo "ok   $name -> $expect ($(grep -o 'lookups=[0-9]*' <<<"$output"))"
  fi
}

run_case present-immediately   present       "10.20.10.4"        pass
run_case appears-after-retries late          "10.20.10.4"        pass "present after 3 attempts"
run_case never-appears         never         "10.20.10.4"        fail "checked 12 times"
run_case other-private-ip-only other-ip      "10.20.10.4"        fail "Global IPv4 addresses: 10.20.10.5 172.17.0.1 172.18.0.1"
run_case prefix-not-a-match    prefix        "10.20.10.4"        fail "is not assigned"
run_case trailing-whitespace   present       $'10.20.10.4 \r\n'  pass
run_case slow-writer-sigpipe   slow-writer   "10.20.10.4"        pass
run_case ip-command-hiccup     ip-fails-once "10.20.10.4"        pass
run_case diagnostics-on-fail   never         "10.20.10.4"        fail "default via 10.20.10.1 dev eth0"
run_case malformed-ip          present       "10.20.10"          invalid

if ((failures > 0)); then
  echo "$failures private-IP check case(s) failed." >&2
  exit 1
fi
echo "All private-IP check cases passed."
