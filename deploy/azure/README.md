# Azure Production deployment files

Production only. The Test VPS deployment (`deploy/compose.yml`, `deploy/scripts/`, `deploy/validate-env-files.sh`) is not affected by anything here.

Architecture and decisions: `docs/devops/azure-production/`. Operator steps: `docs/devops/azure-production/RUNBOOK.md`.

```text
deploy/azure/
├── main.bicep                    subscription scope: RG, network, NSG, IP, disk, VM, ACA env, private DNS
├── parameters/production.bicepparam
├── modules/                      one module per resource family; container-app.bicep = the 7 apps
├── vm/                           what runs on the infrastructure VM (/opt/researchtrack)
│   ├── compose.yml               nginx, mysql, kafka, prometheus, grafana
│   ├── nginx/ kafka/ prometheus/ templates rendered on the VM
│   └── scripts/                  reconcile-stack.sh, backups, timers, compose wrapper
├── scripts/
│   ├── configure-vm.sh           OS/Docker/data-disk bootstrap (Run Command)
│   ├── validate-vm-stack.sh      stack health, TLS, ports, Kafka, Prometheus, Grafana (on VM)
│   ├── validate-infrastructure.sh private DNS, VM -> apps, Nginx -> Gateway (on VM)
│   ├── network-probe.sh          Container Apps -> MySQL/Kafka (short-lived ACA job)
│   ├── whatif-gate.sh            What-If; fails on deletions unless allowed
│   ├── preflight.sh              application workflow: infrastructure must exist
│   ├── deploy-container-apps.sh  migrations + per-service Bicep rollout + rollback
│   ├── render-containerapp.py    env files -> Bicep parameters / job spec
│   ├── build-vm-bundle.sh, install-bundle.sh, vm-run.sh   Run Command delivery
│   └── bootstrap-oidc.sh         the one manual step (managed identity, OIDC, RBAC)
└── validation/
    ├── validate-azure-env-files.sh       Production env validator
    └── test-validate-azure-env-files.sh  tests with synthetic values only
```

Workflows: `.github/workflows/azure-infrastructure.yml` (infrastructure) and `backend-deploy-production.yml` → `backend-deploy-azure-production.yml` (application releases).
