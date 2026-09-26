#!/usr/bin/env bash
# Regression tests for runtime environment composition in deploy-container-apps.sh.
#
# Renders every service's Container App spec (and Jira's migration job) with the
# real composition functions and SYNTHETIC env files generated from the
# config/env/*/.env.example contracts. The expectation comes from the source,
# not from the deployment metadata: a service whose Program.cs calls
# AddResearchTrackJwtAuthentication must receive every shared-auth contract key
# (Jwt__Issuer, Jwt__Audience, Jwt__SigningKey); others must not. Only key names
# are asserted; synthetic values are checked never to appear in error output.
# Globals below are read by deployer functions loaded with eval:
# shellcheck disable=SC2034,SC2154
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
deployer="$repo_root/deploy/azure/scripts/deploy-container-apps.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

services=(gateway auth project github jira meeting submission)
signing_key="$(printf 'k%.0s' {1..48})"

# Synthetic env files shaped exactly like the contracts.
mkdir -p "$work/env"
for contract in shared gateway auth project github jira meeting submission; do
  file="$work/env/$contract.env"
  [[ "$contract" == shared ]] && file="$work/env/shared-auth.env"
  sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' "$repo_root/config/env/$contract/.env.example" |
    while read -r key; do
      if [[ "$key" == Jwt__SigningKey ]]; then echo "$key=$signing_key"; else echo "$key=synthetic-$contract-$key"; fi
    done > "$file"
done

# The deployer's own composition and render functions, with stub globals.
functions="$(sed -n '/^service_meta() {/,/^}/p; /^service_env_files() {/,/^}/p; /^service_required_keys() {/,/^}/p; /^service_resources() {/,/^}/p; /^render() {/,/^}/p' "$deployer")"
shared_keys_line="$(grep -E '^mapfile -t shared_auth_keys' "$deployer")"
[[ -n "$functions" && -n "$shared_keys_line" ]] || { echo "composition functions not found in $deployer" >&2; exit 1; }
script_dir="$repo_root/deploy/azure/scripts"
service_metadata="$repo_root/deploy/build/service-impact.py"
contract_root="$repo_root/config/env"
ENV_DIR="$work/env"
location=westeurope
env_id=/subscriptions/0/resourceGroups/rg/providers/Microsoft.App/managedEnvironments/cae
MIN_REPLICAS=1
MAX_REPLICAS=1
eval "$functions"
eval "$shared_keys_line"

failures=0
fail() { echo "FAIL $*"; failures=$((failures + 1)); }

[[ "${shared_auth_keys[*]}" == "Jwt__Issuer Jwt__Audience Jwt__SigningKey" ]] \
  || fail "shared-auth contract keys are '${shared_auth_keys[*]}'"

env_names() { jq -r '(.parameters.env.value // .properties.template.containers[0].env)[].name' "$1"; }

for service in "${services[@]}"; do
  project="$(service_meta "$service" project)"
  program="$repo_root/$(dirname "$project")/Program.cs"
  if grep -q 'AddResearchTrackJwtAuthentication(' "$program"; then uses_jwt=true; else uses_jwt=false; fi

  spec="$work/$service.app.json"
  if ! render app "$service" "rt-$service-prod" "ghcr.io/example/researchtrack-$service@sha256:0" "$spec" >/dev/null 2>"$work/err"; then
    fail "$service: render failed: $(cat "$work/err")"
    continue
  fi
  names="$(env_names "$spec")"
  for key in "${shared_auth_keys[@]}"; do
    if [[ "$uses_jwt" == true ]] && ! grep -qx "$key" <<<"$names"; then
      fail "$service calls AddResearchTrackJwtAuthentication but its Container App env lacks $key"
    fi
    if [[ "$uses_jwt" == false ]] && grep -qx "$key" <<<"$names"; then
      fail "$service does not use shared JWT validation but receives $key"
    fi
  done
  if [[ "$uses_jwt" == true ]]; then
    [[ "$(jq -r '.parameters.env.value[] | select(.name == "Jwt__SigningKey") | .secretRef // "plain"' "$spec")" != plain ]] \
      || fail "$service: Jwt__SigningKey is a plain env value, expected a secret reference"
  fi
  # Service-specific configuration is still present alongside the shared keys.
  own_key="$(sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' "$work/env/$service.env" | tail -n1)"
  grep -qx "$own_key" <<<"$names" || fail "$service: service-specific key $own_key missing"
  echo "ok   $service: JWT consumer=$uses_jwt, env files: $(service_env_files "$service")"
done

# Migration jobs run the same image with the same composed environment.
render job jira rt-migrate-jira-prod ghcr.io/example/researchtrack-jira@sha256:0 "$work/jira.job.json" >/dev/null
for key in "${shared_auth_keys[@]}"; do
  env_names "$work/jira.job.json" | grep -qx "$key" || fail "jira migration job env lacks $key"
done
echo "ok   jira migration job receives shared JWT configuration"

# Missing shared key: rendering fails before any spec exists, naming only the key.
cp "$work/env/shared-auth.env" "$work/shared-auth.env.bak"
sed -i.tmp '/^Jwt__SigningKey=/d' "$work/env/shared-auth.env" && rm -f "$work/env/shared-auth.env.tmp"
rm -f "$work/jira.missing.json"
if render app jira rt-jira-prod ghcr.io/example/researchtrack-jira@sha256:0 "$work/jira.missing.json" >/dev/null 2>"$work/err"; then
  fail "jira rendered without Jwt__SigningKey"
else
  grep -q 'rt-jira-prod: required configuration missing or empty in composed env files (shared-auth.env, jira.env): Jwt__SigningKey' "$work/err" \
    || fail "missing-key message: $(cat "$work/err")"
  grep -q 'synthetic-' "$work/err" && fail "error output contains a configuration value"
  [[ ! -e "$work/jira.missing.json" ]] || fail "spec written despite missing shared key"
  echo "ok   missing shared JWT key fails before a Container App spec is produced"
fi
render app gateway rt-gateway-prod ghcr.io/example/researchtrack-gateway@sha256:0 "$work/gw.json" >/dev/null \
  || fail "gateway must not depend on shared-auth.env"
cp "$work/shared-auth.env.bak" "$work/env/shared-auth.env"

# A JWT key copied into a service file is a duplicate, never a silent override.
printf 'Jwt__Issuer=synthetic-override\n' >> "$work/env/jira.env"
if render app jira rt-jira-prod ghcr.io/example/researchtrack-jira@sha256:0 "$work/jira.dup.json" >/dev/null 2>"$work/err"; then
  fail "jira.env redefining Jwt__Issuer was accepted"
else
  grep -q "Environment key 'Jwt__Issuer' is defined more than once" "$work/err" || fail "duplicate message: $(cat "$work/err")"
  echo "ok   service file cannot redefine a shared-auth key"
fi

if ((failures > 0)); then
  echo "$failures composition test(s) failed." >&2
  exit 1
fi
echo "All service environment composition tests passed."
