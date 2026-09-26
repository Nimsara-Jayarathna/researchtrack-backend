# Azure Production — Frozen Decisions

Read this file before changing Azure production deployment code.

## Frozen architecture

```text
Container Apps
├── Gateway
├── Auth
├── Project
├── GitHub
├── Jira
├── Meeting
└── Submission

Infrastructure VM
├── Nginx
├── MySQL
├── Kafka
├── Prometheus
└── Grafana
```

## Frozen rules

- Test/develop deployment stays unchanged.
- Production/main deploys to Azure.
- Only the OIDC/RBAC bootstrap is manual.
- Azure infrastructure is Bicep-managed.
- The VM is infrastructure only; no ResearchTrack microservice runs directly on it.
- Only VM ports 80/443 are public.
- No public SSH.
- MySQL/Kafka are VNet-private.
- Prometheus is private.
- Grafana is exposed only through Nginx.
- Gateway is private behind Nginx.
- GHCR remains the image registry.
- Existing `.env.example` files remain the configuration contract.
- Never read real `.env` files.
- Production values remain in GitHub Environment `production`.
- Start Container Apps at one replica each.
- Blob Storage/file submission integration is deferred.
- Preserve existing Prometheus/Grafana assets where possible.
- Reuse existing dbcheck/migration mechanisms.
- Do not add AKS, App Service, ACR, managed MySQL, Event Hubs, or managed Grafana unless the owner changes this decision.

## Network intent

```text
Internet
   ↓ 80/443
Infrastructure VM / Nginx
   ↓ private VNet
Internal Container Apps Environment
```

Internal services:

```text
Container Apps → VM:3306 MySQL
Container Apps → VM:9092 Kafka
VM Prometheus → Container Apps /metrics
VM Nginx → Gateway
```
