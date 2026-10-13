#!/usr/bin/env python3
"""Capture, classify and safely restore the infrastructure runtime around Bicep.

No application is started/stopped here. The only mutation is restoring an existing
VM's original off state, and only when no Container App is running. A missing or
ambiguous control-plane response fails closed. No credentials are stored.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time

APPS = tuple(f"rt-{name}-prod" for name in
             ("gateway", "auth", "project", "github", "jira", "meeting", "submission"))
VM_STATES = {"PowerState/running", "PowerState/stopped", "PowerState/deallocated"}
APP_STATES = {"Running", "Stopped"}


def az(*args: str) -> str:
    result = subprocess.run(["az", *args], text=True, capture_output=True, check=False)
    if result.returncode:
        raise ValueError(f"Azure read/action failed ({' '.join(args[:3])}): {result.stderr.strip()[:350]}")
    return result.stdout.strip()


def apps(group: str) -> dict[str, str]:
    raw = json.loads(az("containerapp", "list", "--resource-group", group, "--output", "json"))
    if not isinstance(raw, list):
        raise ValueError("Azure Container Apps list response is not an array")
    found = {}
    for entry in raw:
        name = entry.get("name")
        if name not in APPS:
            # Unknown ResearchTrack Production services may depend on this VM.
            if isinstance(name, str) and name.startswith("rt-") and name.endswith("-prod"):
                raise ValueError(f"Unmanaged production Container App {name}; refusing unsafe VM state changes")
            continue
        status = (entry.get("properties") or {}).get("runningStatus", entry.get("runningStatus"))
        if name in found or status not in APP_STATES:
            raise ValueError(f"Unknown/unstable/duplicate Container App {name} state {status!r}")
        found[name] = status
    return found


def vm_state(group: str, vm: str) -> str:
    state = az("vm", "get-instance-view", "-g", group, "-n", vm,
               "--query", "instanceView.statuses[?starts_with(code, 'PowerState/')].code | [0]", "-o", "tsv")
    if state not in VM_STATES:
        raise ValueError(f"VM {vm} is transitioning/unknown: {state!r}")
    return state


def snapshot(group: str, vm: str, output: Path) -> None:
    # A newly provisioned environment has no prior VM state to restore.
    # az group exists works before the first subscription-scope Bicep deployment.
    if az("group", "exists", "--name", group).lower() == "false":
        data = {"schema": 1, "group": group, "vm": vm, "vm_state": None, "apps": {}}
    else:
        vms = json.loads(az("vm", "list", "-g", group, "-o", "json"))
        if not isinstance(vms, list):
            raise ValueError("Azure VM list response is not an array")
        present = [item for item in vms if item.get("name") == vm]
        if len(present) > 1:
            raise ValueError("Duplicate infrastructure VM resources")
        observed_apps = apps(group)
        state = vm_state(group, vm) if present else None
        # An existing stopped infrastructure VM and running applications
        # indicate an inconsistent environment. Never make it less safe.
        if state in ("PowerState/stopped", "PowerState/deallocated") and "Running" in observed_apps.values():
            raise ValueError("VM is off while some production Container Apps are running; investigate before reconciliation")
        if present and not present[0].get("id"):
            raise ValueError("VM resource identity missing from Azure response")
        data = {"schema": 1, "group": group, "vm": vm, "vm_state": state,
                "vm_id": present[0].get("id") if present else None,
                "apps": observed_apps}
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=output.parent,
                                     prefix=".infra-state-", delete=False) as tmp:
        json.dump(data, tmp, sort_keys=True)
        tmp.write("\n")
        temporary = Path(tmp.name)
    temporary.chmod(0o600)
    temporary.replace(output)
    print(f"Captured infrastructure VM power state: {data['vm_state'] or 'new resource'}; "
          f"managed Container Apps: {len(data['apps'])}")


def classify(group: str, env_file: Path) -> None:
    found = apps(group)
    count = len(found)
    running = sum(s == "Running" for s in found.values())
    stopped = count - running
    if count == len(APPS) and running == len(APPS):
        mode = "online"
        expect = "true"
    elif count == len(APPS) and stopped == len(APPS):
        mode = "paused"
        expect = "false"
    elif count == 0:
        mode = "bootstrap"
        expect = "false"
    elif stopped == count:
        mode = "partial-stopped"
        expect = "false"
    else:
        details = ", ".join(f"{name}={state}" for name, state in sorted(found.items()))
        raise ValueError(f"Mixed/partial active Container Apps ({count}/7): {details}. "
                         "Do not bypass monitoring; restore a consistent state or diagnose unhealthy apps.")
    with env_file.open("a", encoding="utf-8") as f:
        f.write(f"EXPECT_APPS={expect}\nAPP_RUNTIME_MODE={mode}\n")
    print(f"Application runtime: {mode} ({running} running, {stopped} stopped, {len(APPS)-count} not deployed); "
          f"EXPECT_APPS={expect}")


def restore(group: str, vm: str, input_file: Path) -> None:
    data = json.loads(input_file.read_text(encoding="utf-8"))
    if data.get("schema") != 1 or data.get("group") != group or data.get("vm") != vm:
        raise ValueError("Invalid or mismatched infrastructure runtime snapshot")
    before = data.get("vm_state")
    if before is None:
        print("No pre-existing VM: nothing to restore (first infrastructure deployment)")
        return
    if before not in VM_STATES:
        raise ValueError("Snapshot has unsupported original VM state")
    captured_id = data.get("vm_id")
    actual_id = az("vm", "show", "-g", group, "-n", vm, "--query", "id", "-o", "tsv")
    if not captured_id or captured_id.lower() != actual_id.lower():
        raise ValueError("Infrastructure VM resource identity changed; refusing automatic power restoration")
    if vm_state(group, vm) == before:
        print(f"Infrastructure VM already matches original power state {before}")
        return
    if before == "PowerState/running":
        # If Bicep or a failed VM action interrupted an originally live VM,
        # restore availability rather than leaving production powered off.
        print(f"Restarting {vm} to restore its original running state")
        az("vm", "start", "-g", group, "-n", vm, "--output", "none")
    else:
        # Do not take down infrastructure required by any running/new app.
        now = apps(group)
        previous = data.get("apps")
        if not isinstance(previous, dict) or any(s not in APP_STATES for s in previous.values()):
            raise ValueError("Invalid captured Container App states")
        if now != previous or any(s == "Running" for s in now.values()):
            raise ValueError("Container App states changed since snapshot; refusing VM shutdown (requires review)")
        if before == "PowerState/deallocated":
            print(f"Restoring {vm} to deallocated (original) state")
            az("vm", "deallocate", "-g", group, "-n", vm, "--output", "none")
        else:
            print(f"Restoring {vm} to stopped (allocated) original state")
            az("vm", "stop", "-g", group, "-n", vm, "--output", "none")
    for _ in range(45):
        # Azure transitions can take several minutes.
        try:
            if vm_state(group, vm) == before:
                print(f"Restored and verified original VM state: {before}")
                return
        except ValueError:
            pass
        time.sleep(10)
    raise ValueError(f"Infrastructure VM did not return to {before}; inspect it in Azure")

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("snapshot", "classify", "restore"))
    parser.add_argument("--group", required=True)
    parser.add_argument("--vm", default="vm-researchtrack-infra-prod")
    parser.add_argument("--file", required=True, type=Path)
    args = parser.parse_args()
    try:
        if args.command == "snapshot":
            snapshot(args.group, args.vm, args.file)
        elif args.command == "classify":
            classify(args.group, args.file)
        else:
            restore(args.group, args.vm, args.file)
    except (ValueError, OSError, json.JSONDecodeError) as ex:
        print(f"INFRA RUNTIME GUARD: {ex}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
