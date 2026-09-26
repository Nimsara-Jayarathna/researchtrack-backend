#!/usr/bin/env bash
# Runs a script on the Production infrastructure VM through Azure (managed) Run
# Command and fails if the script fails. Requires an authenticated `az` session.
# No SSH is involved.
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

echo "Running $(basename "$script_file") on $vm_name ..."
# create returns once the script has finished; its own exit status does not
# reflect script failures, so the instance view is authoritative.
az vm run-command create "${args[@]}" || true
unset protected args

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
