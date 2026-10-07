#!/usr/bin/env bash
# Offline validation for deploy/azure/scripts/runtime-power.sh and
# runtime-session.sh. No Azure subscription is required: a tiny mock az CLI
# persists VM/Container App state in JSON so lifecycle transitions can be
# asserted deterministically.
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
power="$repo_root/deploy/azure/scripts/runtime-power.sh"
session="$repo_root/deploy/azure/scripts/runtime-session.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin"

state="$work/azure.json"
export MOCK_AZURE_STATE="$state"
export RESOURCE_GROUP=rg-researchtrack-prod
export INFRA_VM=vm-researchtrack-infra-prod
export RUNTIME_WAIT_ATTEMPTS=30
export RUNTIME_WAIT_SECONDS=0.05
export RUNTIME_READINESS_ATTEMPTS=1
export RUNTIME_READINESS_RETRY_SECONDS=0
export RUNTIME_RESTORE_ATTEMPTS=2
export RUNTIME_RESTORE_RETRY_SECONDS=0

cat > "$work/bin/az" <<'MOCKAZ'
#!/usr/bin/env bash
set -Eeuo pipefail
state="${MOCK_AZURE_STATE:?}"
cmd1="${1:-}"; cmd2="${2:-}"; cmd3="${3:-}"

get_arg() {
  local want="$1" i
  shift
  for ((i=1; i<=$#; i++)); do
    if [[ "${!i}" == "$want" ]]; then
      i=$((i+1)); printf '%s\n' "${!i}"; return 0
    fi
  done
  return 1
}

case "$cmd1 $cmd2 $cmd3" in
  "account show --query")
    jq -r '.subscription' "$state"
    ;;
  "vm show --resource-group"|"vm show -g")
    jq -r '.vm.id' "$state"
    ;;
  "vm get-instance-view --resource-group"|"vm get-instance-view -g")
    jq -r '.vm.state' "$state"
    ;;
  "vm start --resource-group"|"vm start -g")
    tmp="${state}.tmp"; jq '.vm.state="PowerState/running"' "$state" > "$tmp"; mv "$tmp" "$state"
    ;;
  "vm stop --resource-group"|"vm stop -g")
    tmp="${state}.tmp"; jq '.vm.state="PowerState/stopped"' "$state" > "$tmp"; mv "$tmp" "$state"
    ;;
  "vm deallocate --resource-group"|"vm deallocate -g")
    tmp="${state}.tmp"; jq '.vm.state="PowerState/deallocated"' "$state" > "$tmp"; mv "$tmp" "$state"
    ;;
  "containerapp list --resource-group"|"containerapp list -g")
    jq '[.apps[] | {id,name,tags:(.tags // {}),properties:{runningStatus:.state}}]' "$state"
    ;;
  "extension add --name")
    :
    ;;
  "rest --method POST")
    url="$(get_arg --url "$@")"
    if [[ "$url" =~ /containerApps/([^/]+)/(start|stop)\? ]]; then
      name="${BASH_REMATCH[1]}"; action="${BASH_REMATCH[2]}"
      if [[ "${MOCK_FAIL_APP_ACTION:-}" == "$name:$action" ]]; then
        echo "mock action failure for $name:$action" >&2
        exit 17
      fi
      desired=Running; [[ "$action" == stop ]] && desired=Stopped
      tmp="${state}.tmp"
      jq --arg n "$name" --arg s "$desired" '(.apps[] | select(.name==$n) | .state)=$s' "$state" > "$tmp"
      mv "$tmp" "$state"
    else
      echo "unexpected mock REST URL: $url" >&2
      exit 2
    fi
    ;;
  *)
    echo "mock az: unsupported invocation: $*" >&2
    exit 2
    ;;
esac
MOCKAZ
chmod +x "$work/bin/az"

cat > "$work/bin/mock-vm-run.sh" <<'MOCKRUN'
#!/usr/bin/env bash
set -euo pipefail
if [[ "${MOCK_VM_RUN_FAIL:-false}" == true ]]; then
  echo "mock VM validation failure" >&2
  exit 19
fi
for arg in "$@"; do
  if [[ -n "${MOCK_VM_RUN_FAIL_MODE:-}" && "$arg" == "RUNTIME_READINESS_MODE=${MOCK_VM_RUN_FAIL_MODE}" ]]; then
    echo "mock VM validation failure for ${MOCK_VM_RUN_FAIL_MODE}" >&2
    exit 20
  fi
done
exit 0
MOCKRUN
chmod +x "$work/bin/mock-vm-run.sh"
export RUNTIME_VM_RUNNER="$work/bin/mock-vm-run.sh"
export PATH="$work/bin:$PATH"

managed=(
  rt-auth-prod rt-project-prod rt-github-prod rt-jira-prod
  rt-meeting-prod rt-submission-prod rt-gateway-prod
)

write_state() {
  local vm_state="$1"; shift
  local specs=("$@") apps='[]' spec name app_state i=0
  for name in "${managed[@]}"; do
    app_state=Stopped
    if (( i < ${#specs[@]} )); then app_state="${specs[$i]}"; fi
    apps="$(jq -c --arg n "$name" --arg s "$app_state" --arg id "/subscriptions/sub-test/resourceGroups/rg-researchtrack-prod/providers/Microsoft.App/containerApps/$name" '. + [{name:$n,id:$id,state:$s}]' <<<"$apps")"
    i=$((i+1))
  done
  jq -n --arg sub sub-test --arg vm "$vm_state" --argjson apps "$apps" '{subscription:$sub,vm:{id:"/subscriptions/sub-test/resourceGroups/rg-researchtrack-prod/providers/Microsoft.Compute/virtualMachines/vm-researchtrack-infra-prod",state:$vm},apps:$apps}' > "$state"
}

assert_vm() {
  local expected="$1" actual
  actual="$(jq -r '.vm.state' "$state")"
  [[ "$actual" == "$expected" ]] || { echo "ASSERT VM: expected $expected, got $actual" >&2; exit 1; }
}

assert_apps_from_snapshot() {
  local snapshot="$1" mismatches
  mismatches="$(jq -n --slurpfile live "$state" --slurpfile snap "$snapshot" '
    [ $snap[0].containerApps[] as $s
      | ($live[0].apps[] | select(.name == $s.name)) as $l
      | select($l.state != $s.state)
      | "\($s.name):\($l.state)!=\($s.state)"
    ] | join(",")' -r)"
  [[ -z "$mismatches" ]] || { echo "ASSERT APPS: $mismatches" >&2; exit 1; }
}

run_test() {
  local name="$1"; shift
  echo "== $name"
  "$@"
  echo "PASS: $name"
}

test_all_off_round_trip() {
  write_state PowerState/deallocated
  snap="$work/all-off.json"
  "$power" capture "$snap" >/dev/null
  "$power" start >/dev/null
  assert_vm PowerState/running
  [[ "$(jq '[.apps[] | select(.state=="Running")] | length' "$state")" -eq 7 ]]
  "$power" restore "$snap" >/dev/null
  "$power" verify "$snap" >/dev/null
  assert_vm PowerState/deallocated
  assert_apps_from_snapshot "$snap"
}

test_mixed_state_round_trip() {
  write_state PowerState/running Running Stopped Running Stopped Running Stopped Stopped
  snap="$work/mixed.json"
  "$power" capture "$snap" >/dev/null
  "$power" start >/dev/null
  "$power" restore "$snap" >/dev/null
  "$power" verify "$snap" >/dev/null
  assert_vm PowerState/running
  assert_apps_from_snapshot "$snap"
}

test_exact_stopped_vm_restore() {
  write_state PowerState/stopped
  snap="$work/stopped.json"
  "$power" capture "$snap" >/dev/null
  "$power" start >/dev/null
  "$power" restore "$snap" >/dev/null
  "$power" verify "$snap" >/dev/null
  assert_vm PowerState/stopped
}

test_unmanaged_production_app_fails_closed() {
  write_state PowerState/deallocated
  tmp="${state}.tmp"
  jq '.apps += [{name:"rt-future-prod",id:"/subscriptions/sub-test/resourceGroups/rg-researchtrack-prod/providers/Microsoft.App/containerApps/rt-future-prod",state:"Running",tags:{application:"researchtrack",environment:"production"}}]' "$state" > "$tmp" && mv "$tmp" "$state"
  snap="$work/extra.json"
  if "$power" capture "$snap" >/dev/null 2>&1; then
    echo "capture unexpectedly accepted an undeclared ResearchTrack Production app" >&2
    exit 1
  fi
  assert_vm PowerState/deallocated
  [[ "$(jq -r '.apps[] | select(.name=="rt-future-prod") | .state' "$state")" == Running ]]
}

test_capture_waits_for_transient_app() {
  write_state PowerState/running
  tmp="${state}.tmp"
  jq '(.apps[] | select(.name=="rt-auth-prod") | .state)="Progressing"' "$state" > "$tmp" && mv "$tmp" "$state"
  (
    sleep 0.15
    tmp2="${state}.transition"
    jq '(.apps[] | select(.name=="rt-auth-prod") | .state)="Running"' "$state" > "$tmp2" && mv "$tmp2" "$state"
  ) &
  bg=$!
  snap="$work/transient.json"
  "$power" capture "$snap" >/dev/null 2>&1
  wait "$bg"
  [[ "$(jq -r '.containerApps[] | select(.name=="rt-auth-prod") | .state' "$snap")" == Running ]]
}

test_tampered_snapshot_is_rejected() {
  write_state PowerState/deallocated
  snap="$work/good.json"; bad="$work/bad.json"
  "$power" capture "$snap" >/dev/null
  jq '.subscriptionId="wrong-subscription"' "$snap" > "$bad"
  if "$power" restore "$bad" >/dev/null 2>&1; then
    echo "tampered snapshot unexpectedly restored" >&2
    exit 1
  fi
  assert_vm PowerState/deallocated
}

test_failed_session_work_restores_state() {
  write_state PowerState/deallocated
  snap="$work/session-failure.json"
  active="$work/session-failure.active"
  RUNTIME_SESSION_ACTIVE_FILE="$active" \
    "$session" "$snap" -- bash -c 'exit 23' >/dev/null 2>&1 && {
      echo "session command unexpectedly succeeded" >&2
      exit 1
    }
  [[ ! -e "$active" ]] || { echo "session ownership marker was not cleared" >&2; exit 1; }
  "$power" verify "$snap" >/dev/null
  assert_vm PowerState/deallocated
  assert_apps_from_snapshot "$snap"
}


test_reused_snapshot_refuses_pre_start_drift() {
  write_state PowerState/deallocated
  snap="$work/prestart-drift.json"
  active="$work/prestart-drift.active"
  "$power" capture "$snap" >/dev/null

  # Simulate an out-of-band operator starting one app after capture but before
  # the session takes ownership. Reused snapshots must detect this and refuse
  # to mutate or "restore" somebody else's change.
  tmp="${state}.tmp"
  jq '(.apps[] | select(.name=="rt-auth-prod") | .state)="Running"' "$state" > "$tmp" && mv "$tmp" "$state"

  if RUNTIME_SESSION_REUSE_SNAPSHOT=true RUNTIME_SESSION_ACTIVE_FILE="$active" \
      "$session" "$snap" -- sleep 0 >/dev/null 2>&1; then
    echo "session unexpectedly accepted pre-start drift" >&2
    exit 1
  fi
  [[ ! -e "$active" ]] || { echo "ownership marker should not exist when pre-start drift is detected" >&2; exit 1; }
  [[ "$(jq -r '.apps[] | select(.name=="rt-auth-prod") | .state' "$state")" == Running ]] || {
    echo "pre-start drift was incorrectly overwritten" >&2
    exit 1
  }
  assert_vm PowerState/deallocated
}


test_deployment_start_defers_old_app_health() {
  write_state PowerState/deallocated
  # Simulate the old application revision failing readiness. Deployment mode
  # must still acquire infrastructure so a new revision can repair it.
  MOCK_VM_RUN_FAIL_MODE=apps "$power" start deployment >/dev/null
  assert_vm PowerState/running
  [[ "$(jq '[.apps[] | select(.state=="Running")] | length' "$state")" -eq 7 ]]
}

test_full_start_requires_app_health() {
  write_state PowerState/deallocated
  if MOCK_VM_RUN_FAIL_MODE=apps "$power" start full >/dev/null 2>&1; then
    echo "full runtime start unexpectedly ignored application readiness failure" >&2
    exit 1
  fi
}

test_partial_start_failure_restores_state() {
  write_state PowerState/deallocated
  snap="$work/partial-start.json"
  active="$work/partial-start.active"
  export MOCK_FAIL_APP_ACTION=rt-jira-prod:start
  if RUNTIME_SESSION_ACTIVE_FILE="$active" "$session" "$snap" -- sleep 0 >/dev/null 2>&1; then
    echo "partial-start session unexpectedly succeeded" >&2
    exit 1
  fi
  [[ ! -e "$active" ]] || { echo "partial-start ownership marker was not cleared" >&2; exit 1; }
  unset MOCK_FAIL_APP_ACTION
  "$power" verify "$snap" >/dev/null
  assert_vm PowerState/deallocated
  assert_apps_from_snapshot "$snap"
}

run_test "all-off state round trip" test_all_off_round_trip
run_test "mixed app state round trip" test_mixed_state_round_trip
run_test "exact VM Stopped restoration" test_exact_stopped_vm_restore
run_test "unmanaged Production app fails closed" test_unmanaged_production_app_fails_closed
run_test "capture waits for transitional app state" test_capture_waits_for_transient_app
run_test "tampered snapshot rejected" test_tampered_snapshot_is_rejected
run_test "failed session work still restores" test_failed_session_work_restores_state
run_test "reused snapshot refuses pre-start drift" test_reused_snapshot_refuses_pre_start_drift
run_test "deployment start defers old app health" test_deployment_start_defers_old_app_health
run_test "full start still requires app health" test_full_start_requires_app_health
run_test "partial start failure still restores" test_partial_start_failure_restores_state

echo "All runtime power/session validations passed."
