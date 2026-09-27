#!/usr/bin/env bash
# Runs a script on the Production infrastructure VM through Azure (managed) Run
# Command and fails if the script fails. Requires an authenticated `az` session.
# No SSH is involved. A stopped or deallocated VM is started first and left
# running: it hosts MySQL/Kafka/Nginx/monitoring for the live application.
#
# Usage: vm-run.sh <resource-group> <vm-name> <script-file> [options]
#   KEY=value            non-secret value, exported at the top of the script
#                        (stored with the Run Command resource; never secrets)
#   --protected KEY=@f   secret value read from file f, passed as a protected
#                        parameter (exposed to the script as $KEY; not returned
#                        by Azure and never written into the script)
set -euo pipefail

resource_group="${1:?resource group required}"
vm_name="${2:?vm name required}"
script_file="${3:?script file required}"
shift 3

run_name="researchtrack-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}-$(date +%s)-$RANDOM"
script="$(mktemp)"
cleanup() {
  rm -f "$script"
  az vm run-command delete -g "$resource_group" --vm-name "$vm_name" --name "$run_name" --yes >/dev/null 2>&1 || true
}
trap cleanup EXIT

power_state() {
  az vm get-instance-view \
    --resource-group "$resource_group" \
    --name "$vm_name" \
    --query "instanceView.statuses[?starts_with(code, 'PowerState/')].code | [0]" \
    --output tsv
}

# Bounded wait until the power state matches the regex; 60 x 10 s = 10 min.
wait_for_power_state() {
  local want="$1" state i
  for ((i = 1; i <= 60; i++)); do
    state="$(power_state)"
    [[ "$state" =~ $want ]] && return 0
    echo "  $vm_name is $state; waiting ($i/60) ..."
    sleep 10
  done
  echo "$vm_name did not reach $want within 10 minutes (last state: $state)." >&2
  return 1
}

ensure_vm_running() {
  local state
  state="$(power_state)"
  case "$state" in
    PowerState/running)
      return 0 ;;
    PowerState/starting)
      echo "$vm_name is starting." ;;
    *)
      if [[ "$state" == PowerState/stopping || "$state" == PowerState/deallocating ]]; then
        wait_for_power_state '^PowerState/(stopped|deallocated)$'
      fi
      echo "$vm_name is ${state:-in an unknown state}; starting it (it is left running afterwards) ..."
      az vm start --resource-group "$resource_group" --name "$vm_name" --output none ;;
  esac
  wait_for_power_state '^PowerState/running$'
  echo "$vm_name is running."

  # Run Command needs the VM agent. Wait a bounded time for it to report Ready;
  # the create retries below cover any remaining gap.
  local agent i
  for ((i = 1; i <= 30; i++)); do
    agent="$(az vm get-instance-view --resource-group "$resource_group" --name "$vm_name" \
      --query "instanceView.vmAgent.statuses[0].displayStatus" --output tsv 2>/dev/null || true)"
    [[ "$agent" == "Ready" ]] && { echo "VM agent is Ready."; return 0; }
    echo "  VM agent is ${agent:-not reporting}; waiting ($i/30) ..."
    sleep 10
  done
  echo "VM agent is not Ready after 5 minutes; trying Run Command anyway." >&2
}

protected=()
{
  echo '#!/usr/bin/env bash'
  while (($#)); do
    if [[ "$1" == "--protected" ]]; then
      pair="${2:?--protected needs KEY=@file}"
      key="${pair%%=*}"
      file="${pair#*=@}"
      [[ "$key" =~ ^[A-Z_][A-Z0-9_]*$ && -f "$file" ]] || { echo "Invalid protected parameter: $key" >&2; exit 1; }
      protected+=("$key=$(cat "$file")")
      shift 2
      continue
    fi
    key="${1%%=*}"
    value="${1#*=}"
    [[ "$key" =~ ^[A-Z_][A-Z0-9_]*$ ]] || { echo "Invalid parameter name: $key" >&2; exit 1; }
    printf 'export %s=%q\n' "$key" "$value"
    shift
  done
  cat "$script_file"
} > "$script"

args=(
  --resource-group "$resource_group"
  --vm-name "$vm_name"
  --name "$run_name"
  --script @"$script"
  --timeout-in-seconds 3000
  --async-execution false
  --output none
)
((${#protected[@]} == 0)) || args+=(--protected-parameters "${protected[@]}")

ensure_vm_running

echo "Running $(basename "$script_file") on $vm_name ..."
# create returns once the script has finished. A failing script does not fail
# create, so the instance view below is authoritative for the script result;
# create itself fails only when Azure could not run the command (e.g. the VM
# or its agent is not ready), which is retried a bounded number of times.
created=false
for ((attempt = 1; attempt <= 3; attempt++)); do
  if create_err="$(az vm run-command create "${args[@]}" 2>&1)"; then
    created=true
    break
  fi
  echo "Run Command create failed (attempt $attempt/3):" >&2
  echo "$create_err" >&2
  if ((attempt < 3)); then sleep 30; fi
done
unset protected args
$created || { echo "Could not create Run Command on $vm_name; not inspecting it." >&2; exit 1; }

result="$(az vm run-command show \
  --resource-group "$resource_group" \
  --vm-name "$vm_name" \
  --name "$run_name" \
  --instance-view \
  --output json)"

state="$(jq -r '.instanceView.executionState // "Unknown"' <<<"$result")"
exit_code="$(jq -r '.instanceView.exitCode // -1' <<<"$result")"

echo "----- stdout (last 4 KB) -----"
jq -r '.instanceView.output // ""' <<<"$result"
echo "----- stderr (last 4 KB) -----"
jq -r '.instanceView.error // ""' <<<"$result"
echo "------------------------------"
echo "Run Command state: $state, exit code: $exit_code"

[[ "$state" == "Succeeded" && "$exit_code" == "0" ]]
