#!/usr/bin/env bash
# Safely manage the cost-sensitive ResearchTrack Azure Production runtime.
#
# This controller owns *power state only*. It never creates or deletes Azure
# resources. It manages exactly the seven ResearchTrack Production Container
# Apps plus the infrastructure VM, and it can capture/restore their exact stable
# runtime state around temporary work such as diagnostics or future performance
# tests.
#
# Stable snapshot states:
#   VM:             PowerState/running | PowerState/stopped | PowerState/deallocated
#   Container Apps: Running | Stopped
# Transitional/unknown states are never persisted as desired state. Capture waits
# for them to settle and fails closed if they do not.
#
# Required tools: az, jq. `start` also uses sibling vm-run.sh to validate the VM
# stack and application readiness from inside the VNet.
#
# Usage:
#   runtime-power.sh status
#   runtime-power.sh capture STATE_FILE
#   runtime-power.sh start [full|deployment]
#   runtime-power.sh stop
#   runtime-power.sh restore STATE_FILE
#   runtime-power.sh verify STATE_FILE
#   runtime-power.sh validate-snapshot STATE_FILE
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-researchtrack-prod}"
INFRA_VM="${INFRA_VM:-vm-researchtrack-infra-prod}"
ACA_API_VERSION="${ACA_API_VERSION:-2026-01-01}"
WAIT_ATTEMPTS="${RUNTIME_WAIT_ATTEMPTS:-90}"
WAIT_SECONDS="${RUNTIME_WAIT_SECONDS:-10}"
READINESS_ATTEMPTS="${RUNTIME_READINESS_ATTEMPTS:-3}"
READINESS_RETRY_SECONDS="${RUNTIME_READINESS_RETRY_SECONDS:-20}"
VM_RUNNER="${RUNTIME_VM_RUNNER:-$SCRIPT_DIR/vm-run.sh}"
PUBLIC_HEALTH_URL="${RUNTIME_PUBLIC_HEALTH_URL:-}"
PUBLIC_HEALTH_ATTEMPTS="${RUNTIME_PUBLIC_HEALTH_ATTEMPTS:-30}"
PUBLIC_HEALTH_SECONDS="${RUNTIME_PUBLIC_HEALTH_SECONDS:-10}"

# Start dependencies first and the Gateway last. Stop the Gateway first.
EXPECTED_APPS=(
  rt-auth-prod
  rt-project-prod
  rt-github-prod
  rt-jira-prod
  rt-meeting-prod
  rt-submission-prod
  rt-gateway-prod
)
STOP_ORDER=(
  rt-gateway-prod
  rt-submission-prod
  rt-meeting-prod
  rt-jira-prod
  rt-github-prod
  rt-project-prod
  rt-auth-prod
)

log() { printf '%s\n' "$*"; }
warn() { printf 'WARN: %s\n' "$*" >&2; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

require_command() {
  command -v "$1" >/dev/null 2>&1 || die "Required command '$1' is not installed."
}

require_positive_integer() {
  local name="$1" value="$2"
  [[ "$value" =~ ^[1-9][0-9]*$ ]] || die "$name must be a positive integer (got '$value')."
}

require_command az
require_command jq
require_positive_integer RUNTIME_WAIT_ATTEMPTS "$WAIT_ATTEMPTS"
require_positive_integer RUNTIME_READINESS_ATTEMPTS "$READINESS_ATTEMPTS"

current_subscription_id() {
  az account show --query id --output tsv
}

vm_resource_id() {
  az vm show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$INFRA_VM" \
    --query id \
    --output tsv
}

vm_power_state() {
  az vm get-instance-view \
    --resource-group "$RESOURCE_GROUP" \
    --name "$INFRA_VM" \
    --query "instanceView.statuses[?starts_with(code, 'PowerState/')].code | [0]" \
    --output tsv
}

all_container_apps_json() {
  az containerapp list --resource-group "$RESOURCE_GROUP" --output json |
    jq '[.[] | {id, name, tags: (.tags // {}), runningStatus: (.properties.runningStatus // .runningStatus // null)}]'
}

controlled_apps_json() {
  local all expected_json
  all="$(all_container_apps_json)" || return 1
  expected_json="$(printf '%s\n' "${EXPECTED_APPS[@]}" | jq -R . | jq -s .)"
  jq --argjson expected "$expected_json" '[.[] | select(.name as $n | $expected | index($n))]' <<<"$all"
}

validate_controlled_resources() {
  local vm_id apps expected_json missing extras count
  vm_id="$(vm_resource_id 2>/dev/null || true)"
  [[ -n "$vm_id" ]] || die "Infrastructure VM '$INFRA_VM' does not exist in resource group '$RESOURCE_GROUP'."

  apps="$(all_container_apps_json)" || die "Could not list Container Apps in '$RESOURCE_GROUP'."
  expected_json="$(printf '%s\n' "${EXPECTED_APPS[@]}" | jq -R . | jq -s .)"
  missing="$(jq -r --argjson expected "$expected_json" '[ $expected[] as $n | select(any(.[]; .name == $n) | not) | $n ] | join(", ")' <<<"$apps")"
  [[ -z "$missing" ]] || die "Missing managed Production Container App(s): $missing"

  count="$(jq --argjson expected "$expected_json" '[.[] | select(.name as $n | $expected | index($n))] | length' <<<"$apps")"
  [[ "$count" -eq "${#EXPECTED_APPS[@]}" ]] || die "Expected ${#EXPECTED_APPS[@]} managed Container Apps, found $count."

  # Fail closed if a new ResearchTrack Production app appears. Although this
  # controller would not directly start/stop it, deallocating the shared VM
  # could still break an undeclared dependency. Explicit ownership must be
  # reviewed before mutation continues.
  extras="$(jq -r --argjson expected "$expected_json" '[.[] | select(((.name | test("^rt-.*-prod$")) or ((.tags.application // "") == "researchtrack" and (.tags.environment // "") == "production")) and ((.name as $n | $expected | index($n)) | not)) | .name] | join(", ")' <<<"$apps")"
  [[ -z "$extras" ]] || die "Unmanaged ResearchTrack Production Container App(s) detected: $extras. Update EXPECTED_APPS only after reviewing shared-runtime dependencies."
}

is_stable_vm_state() {
  [[ "$1" == "PowerState/running" || "$1" == "PowerState/stopped" || "$1" == "PowerState/deallocated" ]]
}

is_stable_app_state() {
  [[ "$1" == "Running" || "$1" == "Stopped" ]]
}

wait_for_stable_vm() {
  local state="" i
  for ((i = 1; i <= WAIT_ATTEMPTS; i++)); do
    state="$(vm_power_state 2>/dev/null || true)"
    if is_stable_vm_state "$state"; then
      printf '%s\n' "$state"
      return 0
    fi
    log "VM $INFRA_VM is ${state:-unknown}; waiting for a stable state ($i/$WAIT_ATTEMPTS) ..." >&2
    sleep "$WAIT_SECONDS"
  done
  die "VM $INFRA_VM did not reach a stable state (last state: ${state:-unknown})."
}

stable_controlled_apps_json() {
  local apps="" unstable="" i name status expected_json missing
  expected_json="$(printf '%s\n' "${EXPECTED_APPS[@]}" | jq -R . | jq -s .)"

  for ((i = 1; i <= WAIT_ATTEMPTS; i++)); do
    apps="$(controlled_apps_json 2>/dev/null || true)"
    if [[ -z "$apps" || "$apps" == "null" ]]; then
      log "Could not read managed Container App states; retrying ($i/$WAIT_ATTEMPTS) ..." >&2
      sleep "$WAIT_SECONDS"
      continue
    fi

    missing="$(jq -r --argjson expected "$expected_json" '[ $expected[] as $n | select(any(.[]; .name == $n) | not) | $n ] | join(", ")' <<<"$apps")"
    [[ -z "$missing" ]] || die "Managed Container App(s) disappeared while observing state: $missing"

    unstable="$(jq -r '[.[] | select((.runningStatus != "Running") and (.runningStatus != "Stopped")) | "\(.name)=\(.runningStatus // "unknown")"] | join(", ")' <<<"$apps")"
    if [[ -z "$unstable" ]]; then
      printf '%s\n' "$apps"
      return 0
    fi

    log "Container Apps are transitioning: $unstable; waiting ($i/$WAIT_ATTEMPTS) ..." >&2
    sleep "$WAIT_SECONDS"
  done

  die "Container Apps did not settle into stable Running/Stopped states (last: ${unstable:-unknown})."
}

wait_for_vm_state() {
  local expected="$1" state="" i
  for ((i = 1; i <= WAIT_ATTEMPTS; i++)); do
    state="$(vm_power_state 2>/dev/null || true)"
    if [[ "$state" == "$expected" ]]; then
      log "VM $INFRA_VM reached $state."
      return 0
    fi
    log "VM $INFRA_VM is ${state:-unknown}; waiting for $expected ($i/$WAIT_ATTEMPTS) ..."
    sleep "$WAIT_SECONDS"
  done
  die "VM $INFRA_VM did not reach $expected (last state: ${state:-unknown})."
}

post_app_action() {
  local id="$1" action="$2"
  az rest \
    --method POST \
    --url "https://management.azure.com${id}/${action}?api-version=${ACA_API_VERSION}" \
    --output none
}

wait_for_app_state_map() {
  local desired_json="$1" apps="" mismatches="" i
  for ((i = 1; i <= WAIT_ATTEMPTS; i++)); do
    apps="$(controlled_apps_json 2>/dev/null || true)"
    if [[ -n "$apps" && "$apps" != "null" ]]; then
      mismatches="$(jq -r --argjson desired "$desired_json" '
        [ $desired[] as $want
          | (.[] | select(.name == $want.name)) as $current
          | select(($current.runningStatus // "unknown") != $want.runningStatus)
          | "\($want.name)=\($current.runningStatus // "unknown") (want \($want.runningStatus))"
        ] | join(", ")' <<<"$apps")"
      if [[ -z "$mismatches" ]]; then
        return 0
      fi
    else
      mismatches="unable to read app states"
    fi
    log "Waiting for Container Apps: $mismatches ($i/$WAIT_ATTEMPTS) ..."
    sleep "$WAIT_SECONDS"
  done
  die "Container Apps did not reach their desired states: ${mismatches:-unknown}."
}

ensure_vm_running() {
  local state
  state="$(wait_for_stable_vm)"
  case "$state" in
    PowerState/running)
      log "VM $INFRA_VM is already running."
      ;;
    PowerState/stopped|PowerState/deallocated)
      log "Starting VM $INFRA_VM from $state ..."
      az vm start --resource-group "$RESOURCE_GROUP" --name "$INFRA_VM" --output none
      wait_for_vm_state PowerState/running
      ;;
    *)
      die "Refusing to start VM from unsupported state '$state'."
      ;;
  esac
}

run_vm_readiness() {
  local mode="$1" label="$2" attempt
  [[ -x "$VM_RUNNER" ]] || die "VM runner is not executable: $VM_RUNNER"
  [[ -x "$SCRIPT_DIR/runtime-readiness.sh" ]] || die "Runtime readiness script is not executable."

  for ((attempt = 1; attempt <= READINESS_ATTEMPTS; attempt++)); do
    log "$label (attempt $attempt/$READINESS_ATTEMPTS) ..."
    if "$VM_RUNNER" "$RESOURCE_GROUP" "$INFRA_VM" "$SCRIPT_DIR/runtime-readiness.sh" \
        "RUNTIME_READINESS_MODE=$mode"; then
      log "$label passed."
      return 0
    fi
    if ((attempt < READINESS_ATTEMPTS)); then
      warn "$label failed; dependencies may still be recovering after power-up. Retrying in ${READINESS_RETRY_SECONDS}s."
      sleep "$READINESS_RETRY_SECONDS"
    fi
  done

  die "$label failed after $READINESS_ATTEMPTS attempt(s)."
}

wait_for_vm_stack_ready() {
  # Prove only the core dependencies required by application traffic. Full
  # monitoring/configuration validation remains the deployment workflow's job.
  run_vm_readiness infra "VM dependency readiness validation"
}

wait_for_application_ready() {
  # The probe runs inside the VNet and checks every /health/ready endpoint plus
  # Nginx -> Gateway. Azure Running status alone is not considered readiness.
  run_vm_readiness apps "Application readiness validation"
}

start_apps() {
  local apps desired id name status
  apps="$(stable_controlled_apps_json)"

  for name in "${EXPECTED_APPS[@]}"; do
    id="$(jq -r --arg name "$name" '.[] | select(.name == $name) | .id' <<<"$apps")"
    status="$(jq -r --arg name "$name" '.[] | select(.name == $name) | .runningStatus' <<<"$apps")"
    case "$status" in
      Running)
        log "Container App $name is already running."
        ;;
      Stopped)
        log "Starting Container App $name ..."
        post_app_action "$id" start
        ;;
      *)
        die "Refusing to start $name from unstable/unknown state '$status'."
        ;;
    esac
  done

  desired="$(printf '%s\n' "${EXPECTED_APPS[@]}" | jq -R '{name:., runningStatus:"Running"}' | jq -s .)"
  wait_for_app_state_map "$desired"
  log "All managed Container Apps report Running."
}

wait_for_public_health_if_configured() {
  local i
  [[ -n "$PUBLIC_HEALTH_URL" ]] || return 0
  require_command curl
  require_positive_integer RUNTIME_PUBLIC_HEALTH_ATTEMPTS "$PUBLIC_HEALTH_ATTEMPTS"
  log "Waiting for optional public health endpoint: $PUBLIC_HEALTH_URL"
  for ((i = 1; i <= PUBLIC_HEALTH_ATTEMPTS; i++)); do
    if curl -fsS --max-time 10 "$PUBLIC_HEALTH_URL" >/dev/null; then
      log "Public health endpoint is ready."
      return 0
    fi
    log "Public endpoint not ready ($i/$PUBLIC_HEALTH_ATTEMPTS) ..."
    sleep "$PUBLIC_HEALTH_SECONDS"
  done
  die "Public health endpoint did not become ready: $PUBLIC_HEALTH_URL"
}

stop_apps() {
  local apps desired id name status
  apps="$(stable_controlled_apps_json)"

  for name in "${STOP_ORDER[@]}"; do
    id="$(jq -r --arg name "$name" '.[] | select(.name == $name) | .id' <<<"$apps")"
    status="$(jq -r --arg name "$name" '.[] | select(.name == $name) | .runningStatus' <<<"$apps")"
    case "$status" in
      Stopped)
        log "Container App $name is already stopped."
        ;;
      Running)
        log "Stopping Container App $name ..."
        post_app_action "$id" stop
        ;;
      *)
        die "Refusing to stop $name from unstable/unknown state '$status'."
        ;;
    esac
  done

  desired="$(printf '%s\n' "${EXPECTED_APPS[@]}" | jq -R '{name:., runningStatus:"Stopped"}' | jq -s .)"
  wait_for_app_state_map "$desired"
  log "All managed Container Apps report Stopped."
}

capture_state() {
  local output="${1:?state file path required}" subscription vm_id vm_state apps captured_at
  validate_controlled_resources
  subscription="$(current_subscription_id)"
  [[ -n "$subscription" ]] || die "Could not determine active Azure subscription."
  vm_id="$(vm_resource_id)"

  log "Waiting for a stable VM state before capture ..."
  vm_state="$(wait_for_stable_vm)"
  log "Waiting for stable Container App states before capture ..."
  apps="$(stable_controlled_apps_json)"
  captured_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

  umask 077
  jq -n \
    --argjson schemaVersion 2 \
    --arg capturedAt "$captured_at" \
    --arg githubRunId "${GITHUB_RUN_ID:-local}" \
    --arg githubRunAttempt "${GITHUB_RUN_ATTEMPT:-0}" \
    --arg subscriptionId "$subscription" \
    --arg resourceGroup "$RESOURCE_GROUP" \
    --arg vmName "$INFRA_VM" \
    --arg vmId "$vm_id" \
    --arg vmState "$vm_state" \
    --argjson apps "$apps" \
    '{
      schemaVersion: $schemaVersion,
      capturedAt: $capturedAt,
      githubRunId: $githubRunId,
      githubRunAttempt: $githubRunAttempt,
      subscriptionId: $subscriptionId,
      resourceGroup: $resourceGroup,
      vm: {name: $vmName, id: $vmId, state: $vmState},
      containerApps: ($apps | map({name, id, state: .runningStatus}))
    }' > "$output"
  chmod 600 "$output"
  log "Captured stable Azure runtime state in $output."
}

validate_snapshot() {
  local input="${1:?state file path required}" current_subscription current_vm_id expected_json current_apps snapshot_names bad_state bad_id name snapshot_id live_id
  [[ -f "$input" ]] || die "State file not found: $input"

  jq -e '
    .schemaVersion == 2 and
    (.capturedAt | type == "string") and
    (.subscriptionId | type == "string" and length > 0) and
    (.resourceGroup | type == "string" and length > 0) and
    (.vm.name | type == "string" and length > 0) and
    (.vm.id | type == "string" and length > 0) and
    (.vm.state == "PowerState/running" or .vm.state == "PowerState/stopped" or .vm.state == "PowerState/deallocated") and
    (.containerApps | type == "array")
  ' "$input" >/dev/null || die "Snapshot '$input' has an unsupported or malformed schema."

  current_subscription="$(current_subscription_id)"
  [[ "$(jq -r '.subscriptionId' "$input")" == "$current_subscription" ]] || die "Snapshot subscription does not match the active Azure subscription."
  [[ "$(jq -r '.resourceGroup' "$input")" == "$RESOURCE_GROUP" ]] || die "Snapshot resource group does not match RESOURCE_GROUP='$RESOURCE_GROUP'."
  [[ "$(jq -r '.vm.name' "$input")" == "$INFRA_VM" ]] || die "Snapshot VM name does not match INFRA_VM='$INFRA_VM'."

  current_vm_id="$(vm_resource_id)"
  [[ "$(jq -r '.vm.id' "$input")" == "$current_vm_id" ]] || die "Snapshot VM resource ID no longer matches the current VM."

  expected_json="$(printf '%s\n' "${EXPECTED_APPS[@]}" | jq -R . | jq -s 'sort')"
  snapshot_names="$(jq '[.containerApps[].name] | sort' "$input")"
  [[ "$snapshot_names" == "$expected_json" ]] || die "Snapshot does not contain exactly the seven managed Production Container Apps."

  bad_state="$(jq -r '[.containerApps[] | select(.state != "Running" and .state != "Stopped") | "\(.name)=\(.state // "unknown")"] | join(", ")' "$input")"
  [[ -z "$bad_state" ]] || die "Snapshot contains unsupported Container App state(s): $bad_state"

  validate_controlled_resources
  current_apps="$(controlled_apps_json)"
  bad_id=""
  for name in "${EXPECTED_APPS[@]}"; do
    snapshot_id="$(jq -r --arg name "$name" '.containerApps[] | select(.name == $name) | .id' "$input")"
    live_id="$(jq -r --arg name "$name" '.[] | select(.name == $name) | .id' <<<"$current_apps")"
    if [[ -z "$snapshot_id" || "$snapshot_id" != "$live_id" ]]; then
      bad_id+=" ${name}"
    fi
  done
  [[ -z "$bad_id" ]] || die "Snapshot Container App resource ID mismatch for:$bad_id"

  log "Snapshot is valid for the current Azure Production runtime."
}

restore_apps_from_snapshot() {
  local input="$1" current desired id name current_state wanted_state
  current="$(stable_controlled_apps_json)"
  desired="$(jq '[.containerApps[] | {name, runningStatus: .state}]' "$input")"

  for name in "${EXPECTED_APPS[@]}"; do
    id="$(jq -r --arg name "$name" '.[] | select(.name == $name) | .id' <<<"$current")"
    current_state="$(jq -r --arg name "$name" '.[] | select(.name == $name) | .runningStatus' <<<"$current")"
    wanted_state="$(jq -r --arg name "$name" '.containerApps[] | select(.name == $name) | .state' "$input")"

    [[ "$current_state" == "$wanted_state" ]] && continue
    case "$wanted_state" in
      Running)
        log "Restoring $name: $current_state -> Running"
        post_app_action "$id" start
        ;;
      Stopped)
        log "Restoring $name: $current_state -> Stopped"
        post_app_action "$id" stop
        ;;
      *)
        die "Unsupported desired state '$wanted_state' for $name."
        ;;
    esac
  done

  wait_for_app_state_map "$desired"
}

restore_vm_state() {
  local wanted="$1" current
  current="$(wait_for_stable_vm)"
  [[ "$current" == "$wanted" ]] && { log "VM already matches captured state $wanted."; return 0; }

  case "$wanted" in
    PowerState/running)
      ensure_vm_running
      ;;
    PowerState/stopped)
      # A deallocated VM cannot be converted directly to Stopped (allocated):
      # start it, then stop/power-off without deallocation.
      if [[ "$current" == "PowerState/deallocated" ]]; then
        log "Re-allocating VM so exact captured state PowerState/stopped can be restored ..."
        az vm start --resource-group "$RESOURCE_GROUP" --name "$INFRA_VM" --output none
        wait_for_vm_state PowerState/running
      fi
      current="$(wait_for_stable_vm)"
      if [[ "$current" == "PowerState/running" ]]; then
        log "Stopping VM $INFRA_VM without deallocating ..."
        az vm stop --resource-group "$RESOURCE_GROUP" --name "$INFRA_VM" --output none
        wait_for_vm_state PowerState/stopped
      elif [[ "$current" != "PowerState/stopped" ]]; then
        die "Could not restore VM to PowerState/stopped from '$current'."
      fi
      ;;
    PowerState/deallocated)
      log "Deallocating VM $INFRA_VM to restore captured state ..."
      az vm deallocate --resource-group "$RESOURCE_GROUP" --name "$INFRA_VM" --output none
      wait_for_vm_state PowerState/deallocated
      ;;
    *)
      die "Unsupported captured VM state '$wanted'."
      ;;
  esac
}

verify_snapshot() {
  local input="${1:?state file path required}" current_vm wanted_vm current_apps mismatches desired
  validate_snapshot "$input" >/dev/null
  current_vm="$(wait_for_stable_vm)"
  wanted_vm="$(jq -r '.vm.state' "$input")"
  [[ "$current_vm" == "$wanted_vm" ]] || die "VM state mismatch: current=$current_vm captured=$wanted_vm"

  current_apps="$(stable_controlled_apps_json)"
  desired="$(jq '[.containerApps[] | {name, runningStatus: .state}]' "$input")"
  mismatches="$(jq -r --argjson desired "$desired" '
    [ $desired[] as $want
      | (.[] | select(.name == $want.name)) as $current
      | select($current.runningStatus != $want.runningStatus)
      | "\($want.name)=\($current.runningStatus) (captured \($want.runningStatus))"
    ] | join(", ")' <<<"$current_apps")"
  [[ -z "$mismatches" ]] || die "Container App state mismatch after restore: $mismatches"
  log "Verified: Azure runtime exactly matches the captured stable state."
}

restore_state() {
  local input="${1:?state file path required}" wanted_vm
  validate_snapshot "$input"
  log "Restoring managed Container Apps to their captured states ..."
  restore_apps_from_snapshot "$input"
  wanted_vm="$(jq -r '.vm.state' "$input")"
  log "Restoring VM to captured state $wanted_vm ..."
  restore_vm_state "$wanted_vm"
  verify_snapshot "$input"
  log "Azure runtime restoration completed successfully."
}

show_status() {
  local state apps all expected_json extras
  state="$(vm_power_state 2>/dev/null || true)"
  echo "VM"
  printf '%-34s %s\n' "$INFRA_VM" "${state:-unknown}"
  echo
  echo "Managed Container Apps"
  apps="$(controlled_apps_json 2>/dev/null || echo '[]')"
  if command -v column >/dev/null 2>&1; then
    jq -r '.[] | [.name, (.runningStatus // "unknown")] | @tsv' <<<"$apps" | sort | column -t
  else
    jq -r '.[] | [.name, (.runningStatus // "unknown")] | @tsv' <<<"$apps" | sort
  fi

  all="$(all_container_apps_json 2>/dev/null || echo '[]')"
  expected_json="$(printf '%s\n' "${EXPECTED_APPS[@]}" | jq -R . | jq -s .)"
  extras="$(jq -r --argjson expected "$expected_json" '[.[] | select(.name as $n | ($expected | index($n) | not)) | .name] | sort | .[]' <<<"$all")"
  if [[ -n "$extras" ]]; then
    echo
    echo "Other Container Apps (not managed by this controller)"
    printf '%s\n' "$extras"
  fi
}

start_runtime() {
  local mode="${1:-full}"
  case "$mode" in
    full|deployment) ;;
    *) die "Unsupported start mode '$mode' (expected full|deployment)." ;;
  esac

  validate_controlled_resources
  ensure_vm_running
  # Never wake application replicas until the VM-hosted dependencies they use
  # (MySQL, Kafka and Nginx) are proven ready after boot.
  wait_for_vm_stack_ready
  start_apps

  if [[ "$mode" == "full" ]]; then
    # Temporary runtime sessions/tests require the currently deployed
    # application to be healthy before work begins.
    wait_for_application_ready
    wait_for_public_health_if_configured
    log "ResearchTrack Azure Production runtime is fully ready."
  else
    # A deployment must be able to repair a currently unhealthy application.
    # Requiring the *old* revision to pass /health/ready here could prevent the
    # very deployment intended to fix it. Control-plane Running + healthy VM
    # dependencies are sufficient; the deployment workflow performs full
    # application verification after rollout.
    log "ResearchTrack Azure Production runtime is acquired for deployment; application health is deferred until post-deploy verification."
  fi
}

stop_runtime() {
  validate_controlled_resources
  stop_apps
  local state
  state="$(wait_for_stable_vm)"
  if [[ "$state" == "PowerState/deallocated" ]]; then
    log "VM $INFRA_VM is already deallocated."
  else
    log "Deallocating VM $INFRA_VM ..."
    az vm deallocate --resource-group "$RESOURCE_GROUP" --name "$INFRA_VM" --output none
    wait_for_vm_state PowerState/deallocated
  fi
  log "ResearchTrack Azure Production runtime is stopped/deallocated."
}

case "${1:-}" in
  status)
    show_status
    ;;
  capture)
    capture_state "${2:-}"
    ;;
  start)
    start_runtime "${2:-full}"
    ;;
  stop)
    stop_runtime
    ;;
  restore)
    restore_state "${2:-}"
    ;;
  verify)
    verify_snapshot "${2:-}"
    ;;
  validate-snapshot)
    validate_snapshot "${2:-}"
    ;;
  *)
    echo "Usage: $0 <status|capture STATE_FILE|start [full|deployment]|stop|restore STATE_FILE|verify STATE_FILE|validate-snapshot STATE_FILE>" >&2
    exit 2
    ;;
esac
