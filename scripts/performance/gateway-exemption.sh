#!/usr/bin/env bash
# Reversible, session-scoped gateway rate-limit allowance for the existing VPS.
# Azure Container Apps is deployed in SINGLE-revision mode (Bicep contract).
# A transient secret and a COPY of the original revision enable the allowance.
# After k6, copying the original revision back recreates its entire original
# template (including image, environment, resources and probes), so we do not
# reconstruct or accidentally omit other gateway settings.
#
# Usage: gateway-exemption.sh capture STATE.json
#        gateway-exemption.sh enable STATE.json IPV4
#        gateway-exemption.sh restore STATE.json
#        gateway-exemption.sh verify STATE.json
#
# Requires: Azure CLI logged into the correct subscription, jq, python3.
# STATE contains only non-secret Gateway settings and original revision name.
set -Eeuo pipefail
resource_group="${RESOURCE_GROUP:-rg-researchtrack-prod}"
gateway_name="${K6_GATEWAY_APP_NAME:-rt-gateway-prod}"
wait_attempts="${K6_GATEWAY_WAIT_ATTEMPTS:-72}"
wait_seconds="${K6_GATEWAY_WAIT_SECONDS:-5}"
cmd="${1:-}"
state="${2:-}"
[[ "$cmd" =~ ^(capture|enable|restore|verify)$ && -n "$state" ]] || {
  echo 'Usage: gateway-exemption.sh {capture|enable|restore|verify} STATE_FILE [IPV4]' >&2
  exit 2
}
[[ "$wait_attempts" =~ ^[1-9][0-9]*$ && "$wait_seconds" =~ ^[0-9]+$ ]] || {
  echo 'Invalid gateway readiness retry configuration.' >&2; exit 2;
}

log() { echo "[k6 gateway] $*" >&2; }
app_json() { az containerapp show -g "$resource_group" -n "$gateway_name" --output json; }
revision_name() { jq -r '.properties.latestRevisionName // ""'; }
ready_revision_name() { jq -r '.properties.latestReadyRevisionName // ""'; }
original_revision() { jq -r '.originalRevision'; }
secret_name() { jq -r '.temporarySecretName'; }

ensure_public_ip() {
  K6_CHECK_IP="$1" python3 - <<'PY'
import ipaddress, os, sys
try:
    ip = ipaddress.IPv4Address(os.environ['K6_CHECK_IP'])
except ipaddress.AddressValueError:
    sys.exit('Expected a valid public IPv4 address from the VPS.')
if not ip.is_global:
    sys.exit('VPS egress address must be globally routable.')
PY
}

require_state() {
  [[ -f "$state" ]] || { log "Gateway snapshot not found: $state"; exit 2; }
  jq -e --arg app "$gateway_name" --arg rg "$resource_group" '
    .schema == 1 and .app == $app and .resourceGroup == $rg
    and (.originalRevision | type == "string" and length > 0)
    and .revisionMode == "Single"
    and (.temporarySecretName | type == "string" and length > 0)
    and (.originalTemplate | type == "object")
    and (.originalTraffic | type == "array")' "$state" >/dev/null || {
    log 'Invalid or mismatched Gateway snapshot.'; exit 2;
  }
}

# app-level secret is *not* revision-scoped. Do not accidentally reuse or
# overwrite a secret belonging to a different session.
secret_exists() {
  local name="$1"
  az containerapp secret list -g "$resource_group" -n "$gateway_name" --output json | \
    jq -e --arg name "$name" 'any(.[]; .name == $name)' >/dev/null
}

capture() {
  local raw="" revision="" mode="" secret="" run_id="" run_attempt=""
  [[ ! -e "$state" ]] || { log 'Refusing to overwrite an existing Gateway snapshot.'; exit 2; }
  raw="$(app_json)"
  mode="$(jq -r '.properties.configuration.activeRevisionsMode // ""' <<<"$raw")"
  [[ "$mode" == Single ]] || {
    log "Expected Single-revision Gateway; got '$mode'. Refusing to change traffic."; exit 1;
  }
  revision="$(ready_revision_name <<<"$raw")"
  [[ -n "$revision" && "$revision" == "$(revision_name <<<"$raw")" ]] || {
    log 'Gateway has a pending or unavailable revision; refusing to change it.'; exit 1;
  }
  # A named fixed-revision traffic route would not follow a new revision;
  # our single-mode deployment must route 100% to latest (or omit the rule).
  jq -e '(.properties.configuration.ingress.traffic // []) as $rules |
    ($rules | length == 0) or
    (($rules | length == 1) and $rules[0].latestRevision == true and $rules[0].weight == 100)' \
    <<<"$raw" >/dev/null || {
    log 'Gateway does not use latest=100 traffic routing; refusing a transient revision.'; exit 1;
  }
  # The allowance is session-scoped: refuse to override an existing exemption.
  jq -e '[.properties.template.containers[0].env[]? |
      select(.name == "RateLimiting__PerformanceTest__Enabled") | .value] |
      (length == 0) or .[0] == "false"' <<<"$raw" >/dev/null || {
    log 'Gateway performance exemption is already enabled; refusing to override it.'; exit 1;
  }
  run_id="${GITHUB_RUN_ID:-manual}"
  run_attempt="${GITHUB_RUN_ATTEMPT:-1}"
  [[ "$run_id" =~ ^([0-9]+|manual)$ && "$run_attempt" =~ ^[0-9]+$ ]] || exit 2
  secret="k6-perf-${run_id}-${run_attempt}"
  ((${#secret} < 64)) || { log 'Session secret name is too long.'; exit 2; }
  if secret_exists "$secret"; then
    log "A secret for this run already exists: $secret. Recover it first."; exit 1
  fi
  umask 077
  mkdir -p "$(dirname "$state")"
  jq --arg app "$gateway_name" --arg rg "$resource_group" --arg secret "$secret" --arg revision "$revision" '
    {schema:1, app:$app, resourceGroup:$rg, temporarySecretName:$secret,
     originalRevision:$revision,
     revisionMode:.properties.configuration.activeRevisionsMode,
     originalTraffic:(.properties.configuration.ingress.traffic // []),
     originalTemplate:.properties.template}' <<<"$raw" > "$state"
  chmod 600 "$state"
  require_state
  log "Captured original Gateway revision $revision (configuration is disabled)."
}

# Compare original/target template contents excluding only revisionSuffix, a
# revision-generated field. This also detects unintended changes to image etc.
template_matches() {
  local current="$1"
  jq -e --argjson current "$current" '
    def canonical:
      del(.revisionSuffix)
      | (.containers |= (map(.env |= sort_by(.name)) | sort_by(.name)));
    (.originalTemplate | canonical) == ($current | canonical)
  ' "$state" >/dev/null
}

wait_for_revision() {
  local expectation="$1" ip="${2:-}" secret="${3:-}" i raw latest ready env found
  for ((i=1; i<=wait_attempts; i++)); do
    raw="$(app_json 2>/dev/null || true)"
    if [[ -n "$raw" ]]; then
      latest="$(revision_name <<<"$raw")"
      ready="$(ready_revision_name <<<"$raw")"
      if [[ -n "$latest" && "$latest" == "$ready" ]]; then
        env="$(jq -c '.properties.template.containers[0].env // []' <<<"$raw")"
        if [[ "$expectation" == original ]]; then
          if template_matches "$(jq -c '.properties.template' <<<"$raw")"; then
            return 0
          fi
        elif jq -e --arg ip "$ip" --arg secret "$secret" '
          any(.[]; .name == "RateLimiting__PerformanceTest__Enabled" and .value == "true") and
          any(.[]; .name == "RateLimiting__PerformanceTest__AllowedIp" and .value == $ip) and
          any(.[]; .name == "RateLimiting__PerformanceTest__Token" and .secretRef == $secret)
        ' <<<"$env" >/dev/null; then
          return 0
        fi
      fi
    fi
    ((i < wait_attempts)) && sleep "$wait_seconds"
  done
  log "Gateway revision did not become ready with $expectation configuration."
  return 1
}

enable() {
  local ip="${1:-}" raw rev secret token
  require_state
  ensure_public_ip "$ip"
  token="${K6_PERFORMANCE_TOKEN:-}"
  [[ ${#token} -ge 32 && "$token" != *CHANGE_ME* ]] || {
    log 'K6_PERFORMANCE_TOKEN must be a configured, 32+ character secret.'; exit 2;
  }
  raw="$(app_json)"
  rev="$(original_revision < "$state")"
  [[ "$(revision_name <<<"$raw")" == "$rev" && "$(ready_revision_name <<<"$raw")" == "$rev" ]] || {
    log 'Gateway revision changed after capture; refusing to overwrite unrelated changes.'; exit 1;
  }
  secret="$(secret_name < "$state")"
  if secret_exists "$secret"; then
    log 'Refusing to overwrite an existing Gateway secret.'; exit 1
  fi
  # New app-scoped secret; value is never written in revision env or artifacts.
  az containerapp secret set -g "$resource_group" -n "$gateway_name" \
    --secrets "${secret}=${token}" --output none --only-show-errors
  # A revision copy also preserves the original image, resource sizing and
  # *all* other environment entries (vs updating handcrafted env templates).
  az containerapp revision copy -g "$resource_group" -n "$gateway_name" \
    --from-revision "$rev" \
    --set-env-vars \
      'RateLimiting__PerformanceTest__Enabled=true' \
      "RateLimiting__PerformanceTest__AllowedIp=${ip}" \
      "RateLimiting__PerformanceTest__Token=secretref:${secret}" \
    --output none --only-show-errors
  wait_for_revision enabled "$ip" "$secret"
  log 'Gateway temporary, IP-and-token-scoped general-rate-limit exemption is ready.'
}

verify() {
  local raw
  require_state
  raw="$(app_json)"
  [[ "$(jq -r '.properties.configuration.activeRevisionsMode // ""' <<<"$raw")" == Single ]] || {
    log 'Gateway revision mode changed unexpectedly.'; return 1;
  }
  template_matches "$(jq -c '.properties.template' <<<"$raw")" || {
    log 'Gateway template does not match the original pre-test configuration.'; return 1;
  }
  # Revision identity *may* differ: Azure single-mode rollback creates a new
  # ready revision with the original full template.
  [[ -n "$(revision_name <<<"$raw")" && "$(revision_name <<<"$raw")" == "$(ready_revision_name <<<"$raw")" ]] || {
    log 'Gateway original configuration is not ready.'; return 1;
  }
  jq -e --slurpfile state "$state" '
    (.properties.configuration.ingress.traffic // []) == $state[0].originalTraffic
  ' <<<"$raw" >/dev/null || {
    log 'Gateway traffic weights changed during the session.'; return 1;
  }
  ! secret_exists "$(secret_name < "$state")" || {
    log 'Gateway still has a temporary k6 secret.'; return 1;
  }
  log 'Original Gateway template, single revision mode, traffic weights and temporary-secret cleanup verified.'
}

restore() {
  local raw secret
  require_state
  raw="$(app_json)"
  secret="$(secret_name < "$state")"
  if ! template_matches "$(jq -c '.properties.template' <<<"$raw")"; then
    log 'Recreating the original Gateway template from its captured revision.'
    az containerapp revision copy -g "$resource_group" -n "$gateway_name" \
      --from-revision "$(original_revision < "$state")" \
      --output none --only-show-errors
  fi
  wait_for_revision original
  # Old test revisions are inactive in Single mode; clear the now-unreferenced
  # secret from app scope once the original configuration is active.
  if secret_exists "$secret"; then
    az containerapp secret remove -g "$resource_group" -n "$gateway_name" \
      --secret-names "$secret" --output none --only-show-errors
  fi
  verify
}

case "$cmd" in
  capture) capture;;
  enable) enable "${3:-}";;
  restore) restore;;
  verify) verify;;
esac
