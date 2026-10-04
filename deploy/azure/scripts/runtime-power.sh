#!/usr/bin/env bash
# Manage the cost-sensitive ResearchTrack Azure Production runtime.
#
# This script does not create infrastructure. It only changes the power/running
# state of the existing infrastructure VM and Container Apps.
#
# Required environment:
#   RESOURCE_GROUP   (default: rg-researchtrack-prod)
#   INFRA_VM         (default: vm-researchtrack-infra-prod)
#
# Usage:
#   ./deploy/azure/scripts/runtime-power.sh status
#   ./deploy/azure/scripts/runtime-power.sh capture /tmp/runtime-state.json
#   ./deploy/azure/scripts/runtime-power.sh start
#   ./deploy/azure/scripts/runtime-power.sh stop
#   ./deploy/azure/scripts/runtime-power.sh restore /tmp/runtime-state.json
set -euo pipefail

RESOURCE_GROUP="${RESOURCE_GROUP:-rg-researchtrack-prod}"
INFRA_VM="${INFRA_VM:-vm-researchtrack-infra-prod}"
ACA_API_VERSION="${ACA_API_VERSION:-2026-01-01}"
WAIT_ATTEMPTS="${RUNTIME_WAIT_ATTEMPTS:-90}"
WAIT_SECONDS="${RUNTIME_WAIT_SECONDS:-10}"

require_command() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Required command '$1' is not installed." >&2
    exit 1
  }
}

require_command az
require_command jq

vm_power_state() {
  az vm get-instance-view \
    --resource-group "$RESOURCE_GROUP" \
    --name "$INFRA_VM" \
    --query "instanceView.statuses[?starts_with(code, 'PowerState/')].code | [0]" \
    --output tsv
}

container_apps_json() {
  az containerapp list \
    --resource-group "$RESOURCE_GROUP" \
    --query '[].{id:id,name:name,runningStatus:properties.runningStatus}' \
    --output json
}

container_app_status() {
  local name="$1"
  az containerapp show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$name" \
    --query properties.runningStatus \
    --output tsv
}

wait_for_vm() {
  local expected_regex="$1" state="" i
  for ((i = 1; i <= WAIT_ATTEMPTS; i++)); do
    state="$(vm_power_state 2>/dev/null || true)"
    if [[ "$state" =~ $expected_regex ]]; then
      echo "VM $INFRA_VM reached $state."
      return 0
    fi
    echo "VM $INFRA_VM is ${state:-unknown}; waiting ($i/$WAIT_ATTEMPTS) ..."
    sleep "$WAIT_SECONDS"
  done
  echo "VM $INFRA_VM did not reach $expected_regex (last state: ${state:-unknown})." >&2
  return 1
}

wait_for_app() {
  local name="$1" expected="$2" status="" i
  for ((i = 1; i <= WAIT_ATTEMPTS; i++)); do
    status="$(container_app_status "$name" 2>/dev/null || true)"
    if [[ "$status" == "$expected" ]]; then
      echo "Container App $name reached $status."
      return 0
    fi
    echo "Container App $name is ${status:-unknown}; waiting for $expected ($i/$WAIT_ATTEMPTS) ..."
    sleep "$WAIT_SECONDS"
  done
  echo "Container App $name did not reach $expected (last state: ${status:-unknown})." >&2
  return 1
}

post_app_action() {
  local id="$1" action="$2"
  az rest \
    --method POST \
    --url "https://management.azure.com${id}/${action}?api-version=${ACA_API_VERSION}" \
    --output none
}

ensure_vm_running() {
  local state
  state="$(vm_power_state 2>/dev/null || true)"
  case "$state" in
    PowerState/running)
      echo "VM $INFRA_VM is already running."
      return 0
      ;;
    PowerState/starting)
      echo "VM $INFRA_VM is already starting."
      ;;
    PowerState/stopping|PowerState/deallocating)
      echo "VM $INFRA_VM is transitioning down; waiting before restart."
      wait_for_vm '^PowerState/(stopped|deallocated)$'
      az vm start --resource-group "$RESOURCE_GROUP" --name "$INFRA_VM" --output none
      ;;
    *)
      echo "Starting VM $INFRA_VM from ${state:-unknown} ..."
      az vm start --resource-group "$RESOURCE_GROUP" --name "$INFRA_VM" --output none
      ;;
  esac
  wait_for_vm '^PowerState/running$'
}

start_apps() {
  local apps id name status
  apps="$(container_apps_json)"
  if [[ "$(jq 'length' <<<"$apps")" -eq 0 ]]; then
    echo "No Container Apps found in $RESOURCE_GROUP." >&2
    return 1
  fi

  while IFS=$'\t' read -r id name status; do
    if [[ "$status" == "Running" ]]; then
      echo "Container App $name is already running."
      continue
    fi
    echo "Starting Container App $name from ${status:-unknown} ..."
    post_app_action "$id" start
  done < <(jq -r '.[] | [.id, .name, (.runningStatus // "")] | @tsv' <<<"$apps")

  while IFS= read -r name; do
    wait_for_app "$name" Running
  done < <(jq -r '.[].name' <<<"$apps")
}

stop_apps() {
  local apps id name status
  apps="$(container_apps_json)"
  while IFS=$'\t' read -r id name status; do
    if [[ "$status" == "Stopped" ]]; then
      echo "Container App $name is already stopped."
      continue
    fi
    echo "Stopping Container App $name from ${status:-unknown} ..."
    post_app_action "$id" stop
  done < <(jq -r '.[] | [.id, .name, (.runningStatus // "")] | @tsv' <<<"$apps")

  while IFS= read -r name; do
    wait_for_app "$name" Stopped
  done < <(jq -r '.[].name' <<<"$apps")
}

capture_state() {
  local output="${1:?state file path required}" vm_state apps
  vm_state="$(vm_power_state)"
  apps="$(container_apps_json)"
  jq -n \
    --arg resourceGroup "$RESOURCE_GROUP" \
    --arg vm "$INFRA_VM" \
    --arg vmState "$vm_state" \
    --argjson apps "$apps" \
    '{resourceGroup:$resourceGroup, vm:$vm, vmState:$vmState, apps:$apps}' > "$output"
  chmod 600 "$output"
  echo "Captured runtime state in $output."
}

show_status() {
  local state apps
  state="$(vm_power_state 2>/dev/null || true)"
  echo "VM"
  printf '%-34s %s\n' "$INFRA_VM" "${state:-unknown}"
  echo
  echo "Container Apps"
  apps="$(container_apps_json)"
  if command -v column >/dev/null 2>&1; then
    jq -r '.[] | [.name, (.runningStatus // "unknown")] | @tsv' <<<"$apps" | column -t
  else
    jq -r '.[] | [.name, (.runningStatus // "unknown")] | @tsv' <<<"$apps"
  fi
}

start_runtime() {
  ensure_vm_running
  # The VM hosts MySQL/Kafka/Nginx. Start it before application replicas.
  start_apps
  echo "ResearchTrack Azure runtime is running."
}

stop_runtime() {
  # Stop application replicas first so they do not continuously fail while the
  # VM-hosted MySQL/Kafka dependencies are being taken down.
  stop_apps
  local state
  state="$(vm_power_state 2>/dev/null || true)"
  if [[ "$state" == "PowerState/deallocated" ]]; then
    echo "VM $INFRA_VM is already deallocated."
  else
    echo "Deallocating VM $INFRA_VM ..."
    az vm deallocate --resource-group "$RESOURCE_GROUP" --name "$INFRA_VM" --output none
    wait_for_vm '^PowerState/deallocated$'
  fi
  echo "ResearchTrack Azure runtime is stopped/deallocated."
}

restore_state() {
  local input="${1:?state file path required}"
  [[ -f "$input" ]] || { echo "State file not found: $input" >&2; exit 1; }

  local original_vm_state apps current id name original_status current_status
  original_vm_state="$(jq -r '.vmState' "$input")"
  apps="$(jq -c '.apps' "$input")"

  # If the VM was expected up, make sure it remains available while app states
  # are restored. This also handles a partial cleanup/retry safely.
  if [[ "$original_vm_state" =~ ^PowerState/(running|starting)$ ]]; then
    ensure_vm_running
  fi

  while IFS=$'\t' read -r id name original_status; do
    current_status="$(container_app_status "$name" 2>/dev/null || true)"
    if [[ "$original_status" =~ ^(Running|Progressing)$ ]]; then
      if [[ "$current_status" != "Running" ]]; then
        echo "Restoring $name to running (was $original_status) ..."
        post_app_action "$id" start
        wait_for_app "$name" Running
      fi
    else
      if [[ "$current_status" != "Stopped" ]]; then
        echo "Restoring $name to stopped (was ${original_status:-unknown}) ..."
        post_app_action "$id" stop
        wait_for_app "$name" Stopped
      fi
    fi
  done < <(jq -r '.[] | [.id, .name, (.runningStatus // "")] | @tsv' <<<"$apps")

  if [[ ! "$original_vm_state" =~ ^PowerState/(running|starting)$ ]]; then
    current="$(vm_power_state 2>/dev/null || true)"
    if [[ "$current" != "PowerState/deallocated" ]]; then
      echo "Restoring VM $INFRA_VM to deallocated (was ${original_vm_state:-unknown}) ..."
      az vm deallocate --resource-group "$RESOURCE_GROUP" --name "$INFRA_VM" --output none
      wait_for_vm '^PowerState/deallocated$'
    fi
  fi

  echo "Azure runtime restored to its captured pre-test state."
}

case "${1:-}" in
  status)
    show_status
    ;;
  capture)
    capture_state "${2:-}"
    ;;
  start)
    start_runtime
    ;;
  stop)
    stop_runtime
    ;;
  restore)
    restore_state "${2:-}"
    ;;
  *)
    echo "Usage: $0 <status|capture STATE_FILE|start|stop|restore STATE_FILE>" >&2
    exit 2
    ;;
esac
