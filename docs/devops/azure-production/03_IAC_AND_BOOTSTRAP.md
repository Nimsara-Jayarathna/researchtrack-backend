# Infrastructure as Code and Bootstrap

## Manual one-time bootstrap

The human configures only Azure/GitHub trust.

Create/configure:

- Azure deployment identity,
- GitHub OIDC federation,
- Azure RBAC,
- GitHub Production Environment Azure identifiers.

No long-lived Azure client secret.

## RBAC

Because Bicep may create the production Resource Group, the deployment identity requires enough subscription-scope permission to create/manage the production resources.

Use least privilege practical for the subscription.

Do not grant Owner by default.

## Automated infrastructure

Bicep owns:

```text
Resource Group deployment/resources
VNet
subnets
NSG
public IP
NIC
VM
persistent disk
Container Apps environment
Container Apps
Private DNS
```

## Recommended file structure

```text
deploy/azure/
├── main.bicep
├── parameters/
│   └── production.bicepparam
├── modules/
│   ├── network.bicep
│   ├── private-dns.bicep
│   ├── nsg.bicep
│   ├── public-ip.bicep
│   ├── vm.bicep
│   ├── managed-disk.bicep
│   ├── container-apps-environment.bicep
│   └── container-app.bicep
└── scripts/
    ├── configure-vm.sh
    ├── validate-infrastructure.sh
    └── validate-vm-stack.sh
```

## Workflow

```text
validate Bicep
   ↓
Azure OIDC
   ↓
What-If
   ↓
Bicep deployment
   ↓
Azure Run Command
   ↓
VM reconcile
   ↓
infrastructure validation
```

## Idempotency

Do not treat "exists" as "correct".

Bicep should reconcile desired state.

VM scripts also reconcile desired state without destroying data.

## Portal drift

Do not permanently change Bicep-owned resources manually in the Azure Portal.

Make the change in Bicep and deploy it.

## Infrastructure vs application workflow

Infrastructure changes:
- `deploy/azure/**`
- dedicated infrastructure workflow.

Application changes:
- normal backend code,
- production app deployment workflow.

Do not reconcile the entire VNet simply because one service changed.
