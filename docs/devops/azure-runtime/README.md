# Production runtime state-preservation

ResearchTrack Production is cost-sensitive and is not expected to stay powered on continuously. The infrastructure VM may be **Stopped** or **Deallocated**, and any of the seven application Container Apps may be **Stopped**. Temporary automation must therefore borrow the runtime safely instead of assuming it is already live or unconditionally shutting it down afterwards.

## Scope

This implementation intentionally contains **no k6 or load/stress test**. The current proof workflow performs only a short hold after readiness. A future test can replace that temporary command without changing the lifecycle controller.

Managed resources are explicit:

```text
VM
  vm-researchtrack-infra-prod

Container Apps
  rt-auth-prod
  rt-project-prod
  rt-github-prod
  rt-jira-prod
  rt-meeting-prod
  rt-submission-prod
  rt-gateway-prod
```

Other Container Apps in the resource group are never started or stopped by `runtime-power.sh`. If an undeclared app looks like another ResearchTrack Production app (name `rt-*-prod` or matching ResearchTrack/Production tags), mutation fails closed because deallocating the shared VM could still affect it.

## Lifecycle

`Azure Runtime Session - Production` executes this sequence:

```text
shared Production concurrency lock
        |
        v
Azure OIDC login
        |
        v
wait for stable states
        |
        v
capture exact snapshot
        |
        v
start VM if required
        |
        v
validate VM stack
(MySQL, Kafka, Nginx, Prometheus, Grafana)
        |
        v
start stopped managed Container Apps
        |
        v
validate all app /health/ready endpoints from the VNet
        |
        v
optional public /health/ready check
        |
        v
temporary work (currently sleep 5)
        |
        v
restore captured app states
        |
        v
restore exact VM state
        |
        v
verify exact restoration
```

The hold is **not** a startup delay. Startup is readiness-driven; if the system is ready quickly, the temporary work begins immediately. The default five seconds is only the placeholder work section.

## Stable-state rule

Snapshots never record transitional state as desired state.

Accepted VM snapshot states:

```text
PowerState/running
PowerState/stopped
PowerState/deallocated
```

Accepted Container App snapshot states:

```text
Running
Stopped
```

If the VM is Starting/Stopping/Deallocating, or a Container App reports Progressing/unknown, capture waits for a bounded period. If the resource does not settle, the workflow fails **before mutation**.

`Stopped` and `Deallocated` are preserved separately. A VM captured as `Stopped` is restored with `az vm stop`; a VM captured as `Deallocated` is restored with `az vm deallocate`.

## Snapshot safety

The snapshot schema includes:

- schema version and UTC capture time;
- GitHub run metadata;
- Azure subscription ID;
- resource group;
- VM name, resource ID, and stable state;
- all seven Container App names, resource IDs, and stable states.

Before restore, the controller checks that the active subscription, resource group, VM identity, exact app set, app resource IDs, and state values still match the snapshot. A mismatched or edited snapshot is rejected instead of being applied blindly.

The workflow uploads the snapshot as a seven-day GitHub Actions artifact before it starts the runtime. It contains resource identity/state metadata only, not application secrets.

## Readiness is stronger than `Running`

Azure control-plane `Running` is necessary but not sufficient. The wake path therefore has two readiness phases.

### 1. VM stack readiness

After the VM reaches `PowerState/running`, the controller sends `deploy/azure/scripts/runtime-readiness.sh` to the VM through Azure Run Command in `infra` mode. It waits for the MySQL, Kafka, and Nginx containers to be running/healthy, verifies the private MySQL/Kafka listeners, and validates the Nginx configuration before application replicas are started. This is deliberately narrower than the full deployment validator, so a temporary runtime session is not blocked by an unrelated Grafana/Prometheus audit.

### 2. Application readiness

After all seven managed Container Apps report `Running`, the same runtime readiness script runs on the VM in `apps` mode. It verifies every service `/health/ready` endpoint from inside the VNet and the Nginx → Gateway path in one bounded readiness loop.

If GitHub Environment variable `PRODUCTION_VERIFY_PUBLIC_ENDPOINT=true`, the controller additionally waits for:

```text
https://<PRODUCTION_API_HOSTNAME>/health/ready
```

## Failure and cancellation behaviour

`runtime-session.sh` is transactional around the power state:

```bash
./deploy/azure/scripts/runtime-session.sh /tmp/runtime-state.json -- sleep 5
```

It traps normal exit, `INT`, and `TERM` and attempts restoration up to three times. The GitHub workflow also has a separate `if: always()` restoration step, giving two cleanup layers.

Examples that still trigger restoration:

- VM/app readiness failure after a partial wake-up;
- temporary command exits non-zero;
- one Container App start action fails after other apps started;
- an ordinary GitHub step failure or cancellation that still reaches the runner.

There is one unavoidable boundary: if the GitHub-hosted runner disappears completely, code on that runner cannot execute cleanup. The pre-start snapshot artifact is kept for recovery, and the manual `Azure Runtime Power - Production` workflow remains available. A future Azure-side TTL/watchdog can be added if the project needs protection from total runner loss.

## Concurrency

Every Production workflow that can mutate the same runtime uses:

```yaml
concurrency:
  group: researchtrack-azure-production
  cancel-in-progress: false
  queue: max
```

This prevents a runtime session from restoring old states while an infrastructure reconciliation, application deployment, or manual power operation is using the same resources.

## Commands

```bash
# Inspect managed runtime
./deploy/azure/scripts/runtime-power.sh status

# Capture only stable state
./deploy/azure/scripts/runtime-power.sh capture /tmp/rt-state.json

# Validate that a snapshot belongs to this exact runtime
./deploy/azure/scripts/runtime-power.sh validate-snapshot /tmp/rt-state.json

# Start + prove readiness
./deploy/azure/scripts/runtime-power.sh start

# Explicitly stop all managed apps and deallocate the VM
./deploy/azure/scripts/runtime-power.sh stop

# Restore exact captured app + VM states
./deploy/azure/scripts/runtime-power.sh restore /tmp/rt-state.json

# Compare live state with the snapshot
./deploy/azure/scripts/runtime-power.sh verify /tmp/rt-state.json
```

## Offline lifecycle tests

CI runs:

```bash
./deploy/azure/validation/test-runtime-power.sh
```

The test uses a mocked Azure CLI and covers:

- all-off round trip;
- mixed Running/Stopped app restoration;
- exact VM `Stopped` restoration;
- undeclared ResearchTrack Production apps fail closed before mutation;
- waiting for a transitional app state to settle;
- rejecting a tampered snapshot;
- restoring after temporary work fails;
- restoring after a partial Container App start failure;
- core VM dependency readiness success/failure;
- blocking acquisition when one application readiness endpoint fails.

No real Azure resources are touched by this validation.
