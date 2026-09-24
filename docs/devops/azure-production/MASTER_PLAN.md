# ResearchTrack Azure Production Deployment — Master Implementation Plan

## 1. Purpose

This document is the authoritative implementation plan for the ResearchTrack **production backend deployment on Microsoft Azure**.

The existing development/Test deployment must remain unchanged.

The final production design is:

```text
Azure Container Apps
├── Gateway
├── Auth
├── Project
├── GitHub
├── Jira
├── Meeting
└── Submission

Azure Infrastructure VM
├── Nginx
├── MySQL 8.4
├── Apache Kafka
├── Prometheus
└── Grafana
```

The VM is an **infrastructure + ingress host**, not the host for the ResearchTrack application microservices.

Azure Blob Storage is intentionally outside the current implementation scope. It can be added later for submission files without changing the core production architecture.

---

# 2. Non-Negotiable Architecture Decisions

These decisions are frozen unless the project owner explicitly changes them.

1. The existing Test deployment on `develop` remains unchanged.
2. Production deploys from `main`.
3. Production application services run in Azure Container Apps.
4. Exactly seven application Container Apps are used:
   - Gateway
   - Auth
   - Project
   - GitHub
   - Jira
   - Meeting
   - Submission
5. Nginx, MySQL, Kafka, Prometheus, and Grafana run on one Azure Linux VM using Docker Compose.
6. Only Nginx is publicly reachable.
7. The Gateway Container App is **not directly public**.
8. Grafana is exposed only through Nginx.
9. MySQL, Kafka, Prometheus, and Grafana native ports are not public.
10. Azure Container Apps and the VM communicate through one Azure VNet.
11. MySQL remains Dockerized on the VM.
12. MySQL data, Kafka data, Prometheus data, and Grafana state use persistent VM storage.
13. MySQL traffic from Container Apps uses TLS.
14. Kafka is private and must use a secured configuration; TLS should be implemented where the client integration supports it.
15. GitHub Container Registry remains the image registry.
16. Azure Container Registry is not required.
17. GitHub Actions authenticate to Azure using OIDC.
18. No long-lived Azure client secret is stored in GitHub.
19. Azure/GitHub trust and RBAC are bootstrapped manually once.
20. Azure production infrastructure is then created/reconciled automatically with Bicep.
21. Bicep is the source of truth for Azure networking and production infrastructure.
22. Existing `.env.example` contracts remain authoritative.
23. Real `.env` files must never be read as documentation or committed.
24. GitHub Environment `production` continues to hold real production variables/secrets.
25. Existing DB check/migration mechanisms should be reused.
26. Existing Prometheus alert rules and Grafana dashboards should be reused where possible.
27. Service replicas begin at `1` until statelessness has been confirmed.
28. File-upload/Blob Storage application integration is deferred.

---

# 3. Final Topology

```text
                                   INTERNET
                                      │
                                 TCP 80 / 443
                                      │
                                      ▼
                         Static Azure Public IPv4
                                      │
                                      ▼
                    ┌────────────────────────────────┐
                    │ Azure Infrastructure VM        │
                    │                                │
                    │ Docker Compose                 │
                    │                                │
                    │ ├── Nginx                      │
                    │ ├── MySQL 8.4                  │
                    │ ├── Apache Kafka               │
                    │ ├── Prometheus                 │
                    │ └── Grafana                    │
                    │                                │
                    └──────────────┬─────────────────┘
                                   │
                               Azure VNet
                                   │
                                   ▼
              ┌────────────────────────────────────────────┐
              │ Internal Azure Container Apps Environment │
              │                                            │
              │ ├── rt-gateway-prod                        │
              │ ├── rt-auth-prod                           │
              │ ├── rt-project-prod                        │
              │ ├── rt-github-prod                         │
              │ ├── rt-jira-prod                           │
              │ ├── rt-meeting-prod                        │
              │ └── rt-submission-prod                     │
              │                                            │
              └────────────────────────────────────────────┘
```

Public routing:

```text
https://api.researchtrack.blipzo.xyz
                │
                ▼
             Nginx
                │
                ▼
       private Gateway Container App
```

Monitoring UI:

```text
https://grafana.researchtrack.blipzo.xyz
                │
                ▼
             Nginx
                │
                ▼
          grafana:3000
```

Prometheus, MySQL and Kafka have no public UI or endpoint.

---

# 4. Environment Separation

## 4.1 Test

The current Test deployment remains untouched.

```text
develop
   │
   ▼
existing Test GitHub Actions workflow
   │
   ▼
existing VPS
   │
   ▼
Docker Compose
```

Existing Test-specific assumptions may remain, including Docker DNS names such as:

```text
auth
project
mysql
http://auth:8080
```

The current Test configuration validator must not be weakened or modified to make Azure Production fit.

## 4.2 Production

```text
main
  │
  ▼
production CI/CD
  │
  ├── build/test
  ├── detect changed services
  ├── push images to GHCR
  ├── Azure OIDC login
  ├── infrastructure preflight
  ├── DB check/migration
  ├── update changed Container Apps
  ├── health verification
  ├── rollback if unhealthy
  └── end-to-end production verification
```

There is no automatic fallback from Azure Production to the legacy VPS Production path.

---

# 5. Azure Resource Model

Recommended production resource names:

```text
rg-researchtrack-prod

vnet-researchtrack-prod
snet-researchtrack-infra-prod
snet-researchtrack-containerapps-prod

nsg-researchtrack-infra-prod

pip-researchtrack-prod
nic-researchtrack-infra-prod
vm-researchtrack-infra-prod
disk-researchtrack-data-prod

cae-researchtrack-prod

rt-gateway-prod
rt-auth-prod
rt-project-prod
rt-github-prod
rt-jira-prod
rt-meeting-prod
rt-submission-prod
```

Deterministic names are important. Do not generate a new production resource name on every deployment.

---

# 6. VNet Plan

Recommended address space:

```text
VNet:
10.20.0.0/16

Infrastructure subnet:
10.20.10.0/24

Container Apps subnet:
10.20.20.0/23
```

The exact CIDRs may be changed before first deployment, but once the Container Apps environment has been created, subnet sizing should be treated carefully.

The Container Apps subnet is dedicated to the Container Apps environment.

The infrastructure subnet hosts the VM NIC.

Conceptually:

```text
10.20.0.0/16
│
├── 10.20.10.0/24
│   └── Infrastructure VM
│
└── 10.20.20.0/23
    └── Internal Container Apps Environment
```

---

# 7. Container Apps Environment Networking

The Container Apps environment must be configured as **internal-only**.

That means the environment has no public application endpoint.

For apps that the infrastructure VM must reach — including the Gateway and Prometheus scrape targets — configure app ingress so that the app is reachable at the environment's internal VNet boundary.

Important Azure terminology:

- The **environment** is internal.
- App-level ingress may be configured as `external` relative to the environment.
- In an internal environment, this does **not** publish the app to the internet.
- It makes the app reachable from the VNet through the environment's internal load balancer.

Because Prometheus on the VM must scrape all seven services, all seven service ingress endpoints need to be reachable from the VNet.

The applications are still internet-private because the Container Apps environment itself is internal.

---

# 8. Private DNS for the Container Apps Environment

The VM must be able to resolve the Container Apps environment's internal application FQDNs.

For an internal Container Apps environment, create Azure Private DNS for the environment default domain.

After the Container Apps environment exists:

1. Retrieve its default domain.
2. Retrieve its environment static IP.
3. Create an Azure Private DNS zone using the environment default domain.
4. Link the Private DNS zone to `vnet-researchtrack-prod`.
5. Add a wildcard `A` record pointing to the Container Apps environment static IP.

Conceptually:

```text
*.{container-apps-environment-default-domain}
                       │
                       ▼
        Container Apps Environment static IP
```

This is required so that the VM can resolve addresses such as the Gateway and service FQDNs privately.

Bicep should own this DNS configuration once implemented.

---

# 9. Infrastructure VM Sizing

Initial recommended VM profile:

```text
Linux / Ubuntu LTS
2 vCPU
8 GiB RAM
64 GiB or larger persistent data disk
Standard / non-Spot
Pay-as-you-go
```

The VM must be stable during the project evaluation period.

Do **not** use Spot for this VM because it hosts:

- MySQL
- Kafka
- monitoring state
- public ingress

The exact SKU should remain a parameter in Bicep, not hard-coded in multiple files.

Example parameter concept:

```text
vmSize = Standard_D2as_v5
```

If that SKU is unavailable in the selected region, use another stable 2-vCPU/8-GiB general-purpose SKU.

---

# 10. VM Disk Layout

Separate the operating system from persistent application data.

Recommended logical design:

```text
OS Disk
└── Ubuntu
    ├── Docker Engine
    ├── Docker Compose
    └── deployment scripts

Persistent Data Disk
└── /data/researchtrack/
    ├── mysql/
    ├── kafka/
    ├── prometheus/
    └── grafana/
```

Recommended repository/deployment directory:

```text
/opt/researchtrack/
├── compose.yml
├── runtime/
├── nginx/
├── mysql/
├── kafka/
├── prometheus/
└── grafana/
```

Data paths should remain under `/data/researchtrack`.

Configuration and Compose files should remain under `/opt/researchtrack`.

This makes it clear which files are replaceable deployment configuration and which files are persistent runtime data.

---

# 11. Persistent Storage Rules

Never depend on an anonymous Docker volume for production-critical data.

Recommended mounts:

```text
MySQL
/data/researchtrack/mysql
    → /var/lib/mysql

Kafka
/data/researchtrack/kafka
    → Kafka data directory

Prometheus
/data/researchtrack/prometheus
    → /prometheus

Grafana
/data/researchtrack/grafana
    → /var/lib/grafana
```

Container recreation must not delete state.

`docker compose down` followed by `docker compose up -d` must not destroy:

- MySQL databases
- Kafka log data
- Prometheus historical data
- Grafana local state

The data disk must be treated as a persistent resource.

---

# 12. VM OS Hardening

The VM bootstrap should:

1. Use a supported Ubuntu LTS image.
2. Install current security updates.
3. Install Docker Engine from an approved source.
4. Install Docker Compose plugin.
5. Enable Docker to start on boot.
6. Create `/opt/researchtrack`.
7. Mount the persistent data disk at `/data/researchtrack`.
8. Create required subdirectories.
9. Apply ownership/permissions.
10. Enable automatic security updates if practical.
11. Do not expose public SSH for normal deployment.
12. Use Azure Run Command for automated administration.
13. Keep the VM system clock synchronized.
14. Limit unnecessary packages/services.
15. Configure container log rotation.
16. Configure disk-usage alerts/monitoring.

The bootstrap operation must be idempotent.

Running it twice must not:

- reformat the data disk,
- erase the database,
- erase Kafka,
- duplicate configuration,
- create conflicting Docker installations.

---

# 13. Docker Compose Network Layout on the VM

Use a dedicated internal Compose network, for example:

```text
researchtrack-infra
```

Containers:

```text
nginx
mysql
kafka
prometheus
grafana
```

Recommended local communication:

```text
nginx      → grafana:3000
grafana    → prometheus:9090
```

Prometheus reaches Container Apps using VNet/private DNS rather than the local Docker network.

---

# 14. Host Port Binding

Nginx is the only infrastructure container that needs public host bindings.

Recommended public bindings:

```text
0.0.0.0:80  → nginx:80
0.0.0.0:443 → nginx:443
```

MySQL and Kafka must bind only to the VM private interface where possible.

Example concept:

```text
10.20.10.4:3306 → mysql:3306
10.20.10.4:9092 → kafka:9092
```

Prometheus and Grafana do not need public host bindings.

Grafana is reached by Nginx over Docker networking.

Prometheus is reached by Grafana over Docker networking.

If host binding is technically required by the chosen Compose design, bind only to localhost/private interfaces and keep NSG rules restrictive.

---

# 15. VM Private IP

Give the VM a stable private IP.

Recommended:

```text
10.20.10.4
```

Bicep should configure the NIC private IP allocation as static.

The exact value should be parameterized but deterministic.

Container Apps DB configuration can therefore use:

```text
Server=10.20.10.4;
Port=3306;
```

Kafka clients can use the same VM private IP or a private DNS name.

---

# 16. Public IP

Create one Standard Static Public IPv4 address.

It is attached to the Infrastructure VM NIC.

This IP is used for:

```text
api.researchtrack.blipzo.xyz
grafana.researchtrack.blipzo.xyz
```

No Container App needs an internet public IP.

---

# 17. NSG / Firewall Policy

## Public inbound

Allow only:

```text
TCP 80
TCP 443
```

## Public inbound denied

Do not expose:

```text
22   SSH
3000 Grafana
3306 MySQL
9090 Prometheus
9092 Kafka
```

## VNet/internal inbound

Allow the Container Apps subnet to reach:

```text
VM TCP 3306 → MySQL
VM TCP 9092 → Kafka
```

The VM must be able to make outbound HTTPS connections to the Container Apps internal endpoints.

Prometheus needs this for scraping.

Nginx needs this for Gateway proxying.

Default deny should be maintained for unnecessary inbound traffic.

---

# 18. Nginx Responsibilities

Nginx is the single backend public edge.

It performs:

- HTTP to HTTPS redirection
- TLS termination for public domains
- reverse proxying to the private Gateway
- reverse proxying to Grafana
- forwarded-header injection
- request-size limits
- sensible proxy timeouts
- security response headers
- access/error logging

Nginx does **not** route directly to Auth, Project, GitHub, Jira, Meeting, or Submission.

All public API traffic goes through Gateway.

---

# 19. Nginx API Routing

```text
api.researchtrack.blipzo.xyz
               │
               ▼
            Nginx
               │
               ▼
private rt-gateway-prod FQDN
```

Nginx should preserve:

```text
Host
X-Real-IP
X-Forwarded-For
X-Forwarded-Proto
```

The Gateway must be configured to trust forwarded headers only from the expected proxy/network.

This is required to fix the existing IP rate-limit issue where all users otherwise appear to come from the proxy IP.

---

# 20. Nginx Grafana Routing

```text
grafana.researchtrack.blipzo.xyz
               │
               ▼
            Nginx
               │
               ▼
        grafana:3000
```

Grafana should not have a public host port.

Nginx handles TLS.

Grafana should be configured with the correct external root URL/domain so redirects and generated links use HTTPS.

---

# 21. Public TLS

Use Let's Encrypt certificates on Nginx.

DNS must point to the VM public IP before public certificate issuance succeeds.

Recommended certificate workflow:

```text
DNS A records
      ↓
VM public IP
      ↓
HTTP validation on port 80
      ↓
Let's Encrypt
      ↓
Nginx HTTPS :443
```

Certificate renewal should be automatic.

The renewal process must reload Nginx safely after renewal.

---

# 22. MySQL Container

Use MySQL 8.4 as currently planned.

Responsibilities:

- application DB persistence
- service logical databases
- application users
- migration targets
- TLS-required client connections

Recommended health check:

```text
mysqladmin ping
```

Do not use MySQL root for normal application connections.

Create application-level credentials with only the permissions required by the existing migration/application model.

---

# 23. MySQL TLS

The production goal is encrypted Container App → MySQL traffic.

Minimum acceptable initial configuration:

```text
SslMode=Required
```

This ensures encryption but may not verify server identity depending on the .NET MySQL client configuration.

A stronger later configuration is:

```text
VerifyCA / VerifyFull
```

with an internal CA trusted by the application containers.

Do not disable TLS merely because traffic is private.

VNet isolation and transport encryption solve different security problems.

---

# 24. MySQL Database Initialization

Reuse existing DB initialization logic with minimal platform-specific changes.

Conceptual sequence:

```text
MySQL starts
     ↓
required databases/users exist
     ↓
service dbcheck
     ↓
service migration
     ↓
service revision deployment
```

Database host changes from Docker DNS such as:

```text
mysql
```

to the VM private address or private DNS name.

The migration logic itself should not be redesigned merely because production is now Azure.

---

# 25. Kafka Container

Kafka runs on the VM as a single broker.

Target:

```text
KRaft mode
1 broker/controller
replication factor 1
persistent data
private network only
```

This is intentionally assignment-scale, not a high-availability commercial Kafka cluster.

The deployment must clearly document that limitation.

Kafka data lives on the persistent VM data disk.

---

# 26. Kafka Networking

Kafka is reachable only from the VNet.

Container Apps use the VM private IP/private DNS.

Do not publish `9092` to the internet.

Kafka `advertised.listeners` must advertise an address that Container Apps can resolve/reach.

Do not advertise:

```text
localhost
kafka
public VM IP
```

to Azure Container Apps.

Use the VM private IP or private DNS name.

---

# 27. Kafka TLS

Preferred production target:

```text
Container App
    │
    │ TLS
    ▼
Kafka on VM
```

Because Kafka application integration is still a later application-code phase, infrastructure deployment should:

1. support secure listener configuration,
2. create/mount Kafka certificates appropriately,
3. expose only the private listener,
4. document client trust requirements,
5. keep a deployment smoke test.

If full client TLS integration cannot be completed immediately, the fallback must remain VNet-private and documented as temporary technical debt. It must never become public plaintext Kafka.

---

# 28. Prometheus Container

Prometheus remains on the VM.

Persistent data:

```text
/data/researchtrack/prometheus
```

It should reuse:

- existing scrape interval strategy,
- existing alert rules,
- existing application metrics,
- existing Grafana dashboard queries where possible.

Only target discovery/addressing changes.

---

# 29. Prometheus Target Model

Prometheus scrapes all seven Container Apps over the VNet.

```text
Prometheus
   │
   ├── Gateway /metrics
   ├── Auth /metrics
   ├── Project /metrics
   ├── GitHub /metrics
   ├── Jira /metrics
   ├── Meeting /metrics
   └── Submission /metrics
```

Use each Container App's private/internal FQDN.

Do not use public API routing for metrics scraping.

---

# 30. Stable Prometheus Labels

Do not make alert rules depend on infrastructure-specific `instance` strings.

Prefer stable labels:

```text
service="gateway"
service="auth"
service="project"
service="github"
service="jira"
service="meeting"
service="submission"

environment="production"
```

This allows the same alert logic to survive changes to FQDNs/IPs.

Test may use:

```text
instance="github:8080"
```

while Production may use an Azure FQDN.

The semantic identity should remain:

```text
service="github"
```

---

# 31. Grafana Container

Grafana remains on the VM.

Persistent state:

```text
/data/researchtrack/grafana
```

Datasource:

```text
http://prometheus:9090
```

Existing dashboards should be provisioned from the repository.

Grafana's external URL must be:

```text
https://grafana.researchtrack.blipzo.xyz
```

---

# 32. Container App Resource Strategy

Create seven Container Apps.

Start every service with:

```text
minReplicas = 1
maxReplicas = 1
```

Do not scale to zero yet because:

- Prometheus scraping may keep waking services,
- some services may contain in-memory state,
- background synchronization may depend on a continuously running replica,
- the production period is short.

Future scaling can be added after statelessness/background-worker behavior is reviewed.

---

# 33. Container App Health Probes

Reuse existing endpoints:

```text
/health/live
/health/ready
```

Each app should have:

- liveness probe
- readiness probe

A new revision must not receive production traffic until ready.

---

# 34. Gateway Metrics Port

The existing Docker/VPS Gateway may use a separate metrics port.

For Azure Container Apps, avoid unnecessary extra exposed ports where possible.

Preferred production-compatible model:

```text
Gateway application port
├── API
├── /health/live
├── /health/ready
└── /metrics
```

Because the Container Apps environment is private, `/metrics` remains private from the internet.

Nginx must not proxy `/metrics` publicly.

---

# 35. GitHub Container Registry

Keep GHCR.

Expected image model:

```text
ghcr.io/<org>/researchtrack-gateway:<sha>
ghcr.io/<org>/researchtrack-auth:<sha>
ghcr.io/<org>/researchtrack-project:<sha>
ghcr.io/<org>/researchtrack-github:<sha>
ghcr.io/<org>/researchtrack-jira:<sha>
ghcr.io/<org>/researchtrack-meeting:<sha>
ghcr.io/<org>/researchtrack-submission:<sha>
```

Use immutable SHA/version tags for deployment.

Do not deploy production using only a mutable `latest` tag.

---

# 36. Environment/Secret Contract

The source of truth for variable names remains:

```text
config/env/**/.env.example
```

Real values remain in GitHub Environment:

```text
production
```

Do not create a second parallel secret-management model merely for Azure.

---

# 37. Real `.env` File Rule

Automation/AI must not read real environment files.

Allowed:

```text
.env.example
.env.admin.example
.env.deploy.example
config/env/**/.env.example
```

Forbidden unless the human explicitly overrides:

```text
.env
.env.production
.env.test
.env.local
deploy/.env.deploy
config/env/**/.env
```

Never print, summarize, copy, or commit real secrets.

---

# 38. Container App Secret Delivery

GitHub production secrets/variables are transformed at deployment time into:

```text
Container App secrets
Container App environment variables
```

Do not upload a permanent production `.env` file into each Container App.

---

# 39. VM Runtime Secret Delivery

The VM may reuse the existing deployment philosophy:

```text
GitHub production Environment
        │
        ▼
GitHub Actions
        │
        ▼
temporary generated runtime bundle
        │
        ▼
Azure Run Command
        │
        ▼
VM
        │
        ▼
/opt/researchtrack/runtime
```

Runtime files must use restrictive permissions.

Do not commit them.

Do not log their contents.

Avoid leaving unnecessary temporary bundles after deployment.

---

# 40. Manual One-Time Azure/GitHub Bootstrap

Only the trust/bootstrap step is manual.

Manually configure:

1. Azure deployment identity.
2. GitHub OIDC federated credential.
3. Required Azure RBAC.
4. GitHub Production Environment Azure identifiers.

Values include:

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
AZURE_LOCATION
```

No long-lived Azure client secret/password is used.

---

# 41. Bootstrap RBAC Scope Decision

Because Bicep is intended to create the production Resource Group automatically, the deployment identity needs permission to create/manage the required resources at a scope that permits Resource Group creation.

For a student subscription, the simplest model is a dedicated deployment identity with appropriate Contributor-level access at subscription scope.

If the subscription contains unrelated/sensitive resources, reduce scope using a manually created resource group or a narrower/custom RBAC model.

Do not casually grant broad Owner permissions.

---

# 42. Bicep Ownership

Bicep owns:

```text
resource group deployment/resources
VNet
subnets
NSG
public IP
VM NIC
VM
managed data disk
Container Apps environment
Container Apps
Private DNS required for Container Apps VNet ingress
```

Bicep is the authoritative desired-state definition for these resources.

Do not permanently modify them in the Azure Portal without updating Bicep.

---

# 43. Bicep Structure

Recommended:

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
├── vm/
│   ├── compose.yml
│   ├── nginx/
│   ├── mysql/
│   ├── kafka/
│   ├── prometheus/
│   └── grafana/
└── scripts/
    ├── configure-vm.sh
    ├── validate-infrastructure.sh
    └── validate-vm-stack.sh
```

Avoid one enormous Bicep file if modules make responsibilities clearer.

---

# 44. Bicep Parameterization

Put environment-specific non-secret configuration in `production.bicepparam`.

Examples:

```text
environmentName
azureLocation
vnetCidr
infraSubnetCidr
containerAppsSubnetCidr
vmSize
vmPrivateIp
dataDiskSize
publicIpSku
containerAppCpu
containerAppMemory
```

Never place production passwords/private keys in a committed `.bicepparam` file.

---

# 45. Infrastructure Reconciliation

Infrastructure deployment is declarative.

Correct model:

```text
actual Azure state
        │
        ▼
compare to Bicep desired state
        │
        ├── missing → create
        ├── different → update
        └── same → no effective change
```

Do not implement hundreds of shell checks such as:

```text
if resource does not exist
```

when Bicep can reconcile the state.

---

# 46. Azure What-If

Before applying Bicep changes:

```text
bicep build / validation
        ↓
Azure What-If
        ↓
deployment
```

What-If is a safety check.

Unexpected destructive/network changes must fail the pipeline or require manual review.

---

# 47. Infrastructure Workflow

Recommended:

```text
.github/workflows/azure-infrastructure.yml
```

Triggers:

- manual dispatch,
- changes to `deploy/azure/**`,
- optionally changes to the dedicated infrastructure workflow.

Flow:

```text
checkout
   ↓
validate Bicep
   ↓
Azure OIDC login
   ↓
What-If
   ↓
Bicep deployment
   ↓
VM configuration via Azure Run Command
   ↓
VM stack validation
   ↓
network validation
```

Do not redeploy infrastructure merely because Jira application code changed.

---

# 48. VM Configuration via Azure Run Command

Use Azure Run Command for deployment/configuration.

No public SSH is needed for routine CI/CD.

Flow:

```text
GitHub Actions
       │
       ▼
Azure API
       │
       ▼
Azure Run Command
       │
       ▼
VM
```

VM configuration scripts must be idempotent.

---

# 49. Application Production Workflow

Recommended:

```text
.github/workflows/backend-deploy-production.yml
```

Flow:

```text
CI passes
   ↓
detect affected services
   ↓
build immutable images
   ↓
push GHCR
   ↓
Azure OIDC login
   ↓
infrastructure preflight
   ↓
production config validation
   ↓
dbcheck
   ↓
migration
   ↓
deploy changed Container App revision
   ↓
readiness verification
   ↓
traffic activation
   ↓
full validation
```

---

# 50. Infrastructure Preflight

Application deployment should verify that required infrastructure already exists and is healthy.

Check:

```text
production Resource Group
VNet
Infrastructure VM
Container Apps environment
expected Container Apps
VM Run Command availability
private networking
```

If missing:

```text
FAIL:
Azure production infrastructure is not ready.
Run the Azure infrastructure workflow.
```

The application workflow should not create ad-hoc networking resources.

---

# 51. DB Migration Deployment Order

For each changed database-owning service:

```text
new image
   ↓
dbcheck
   ↓
migration
   ↓
new service revision
   ↓
readiness
   ↓
traffic
```

A failed migration must prevent the new service revision from becoming active.

---

# 52. Revision Rollback

Production deployment must keep the previous healthy Container App revision available until the new revision is healthy.

```text
Revision N
healthy
   │
   ├── remains available
   │
Revision N+1
   ↓
readiness fails
   ↓
disable N+1
   ↓
keep N serving
```

Do not destroy the last healthy revision before validation.

---

# 53. Production Validation

Every production deployment should validate:

## Azure services

- Container Apps expected revisions healthy
- VM healthy/running

## Application health

- Gateway
- Auth
- Project
- GitHub
- Jira
- Meeting
- Submission

## Public route

```text
Nginx → Gateway
```

## MySQL

- TCP/TLS connectivity
- service DB connectivity

## Kafka

- broker reachable
- produce test message
- consume test message

## Prometheus

- all seven targets present
- all required targets UP
- alert rules loaded
- no unexpected critical alerts

## Grafana

- datasource available
- expected dashboards provisioned

---

# 54. DNS Cutover

Do not switch public DNS early.

Recommended order:

```text
Azure infrastructure ready
        ↓
VM stack ready
        ↓
Container Apps ready
        ↓
private connectivity validated
        ↓
temporary/internal production validation
        ↓
DNS A records → VM public IP
        ↓
Let's Encrypt certificate
        ↓
external callbacks updated
        ↓
final end-to-end verification
```

---

# 55. GitHub/Jira Callback Cutover

After the Azure API domain is live over HTTPS, update:

- GitHub App callback URLs
- GitHub webhook URLs
- Jira OAuth callback URLs
- Jira webhook URLs
- any external allowlists/configuration using the old VPS endpoint

Do not update these before the new endpoint is healthy.

---

# 56. Legacy Production VPS

The Test VPS remains.

The old Production VPS deployment path should not silently remain as a hidden fallback.

During cutover, the old production host may be retained temporarily for rollback/data migration, but the workflow behavior must be explicit.

---

# 57. Rate-Limit / Forwarded Header Fix

Before final production cutover, the Gateway must correctly trust forwarded proxy headers.

Otherwise:

```text
User A
User B
User C
   │
   ▼
Nginx
   │
   ▼
Gateway sees one proxy IP
```

This causes IP-based rate limiting to treat all users as one client.

Implement and test trusted proxy forwarding before evaluation.

---

# 58. Logging

Nginx:

- access log
- error log
- rotation

Docker:

- bounded log rotation

Application:

- existing structured logs

Kafka/MySQL/Prometheus/Grafana:

- keep enough operational logs for diagnosis
- do not allow unlimited growth on the VM disk

Secrets must never appear in logs.

---

# 59. Disk Capacity Protection

Prometheus and Kafka can grow continuously.

Set explicit retention policies.

Examples:

```text
Prometheus retention:
7–15 days for the assignment deployment

Kafka retention:
3–7 days unless application requirements demand more
```

Use Docker log rotation.

Monitor disk usage.

Trigger warnings before the data disk becomes critically full.

---

# 60. Failure Domains and Accepted Limitations

This architecture intentionally has one infrastructure VM.

Therefore these components share one failure domain:

```text
Nginx
MySQL
Kafka
Prometheus
Grafana
```

If the VM fails, these services fail together.

This is accepted for:

- a short academic production period,
- lower cost,
- implementation simplicity.

It is not presented as a highly available enterprise infrastructure architecture.

Future production evolution may move:

- MySQL → Azure Database for MySQL
- Kafka → managed Apache Kafka
- Prometheus/Grafana → managed services
- Nginx → Azure-managed ingress

That is explicitly future work, not current scope.

---

# 61. File Storage

Submission Blob Storage is deferred.

Do not block core Azure production migration on it.

Future:

```text
Submission Service
       │
       ▼
Azure Blob Storage
```

---

# 62. Implementation Phases

## Phase 0 — Guardrails

- Add `CLAUDE.md`
- Add repository AI/env safety rules
- Freeze Test deployment behavior
- Record architecture decisions

## Phase 1 — Manual OIDC Bootstrap

Human performs:

- Azure deployment identity
- GitHub federated credential
- RBAC
- GitHub Production Azure identifiers

No infrastructure application resource creation is required manually.

## Phase 2 — Azure IaC Foundation

Implement/verify Bicep:

- production RG deployment
- VNet
- subnets
- NSG
- public IP
- VM NIC
- VM
- data disk
- Container Apps environment
- private DNS

## Phase 3 — VM Bootstrap

Automate:

- OS readiness
- Docker installation
- disk mount
- `/opt/researchtrack`
- `/data/researchtrack`
- VM Docker stack

## Phase 4 — VM Infrastructure Stack

Deploy:

- Nginx
- MySQL
- Kafka
- Prometheus
- Grafana

Verify each independently.

## Phase 5 — Container Apps Foundation

Create/deploy seven apps.

Start one replica each.

Configure ingress appropriate for internal environment + VNet access.

## Phase 6 — Private Networking

Prove:

```text
VM → Gateway
VM → every /metrics endpoint
Container Apps → MySQL
Container Apps → Kafka
```

Verify private DNS.

## Phase 7 — Application Production Deployment

Deploy service images from GHCR.

Run DB checks/migrations.

Verify health.

Verify rollback.

## Phase 8 — Monitoring

Update Prometheus targets.

Add stable labels.

Verify alerts/dashboards.

## Phase 9 — Kafka Application Integration

Implement actual GitHub/Jira event producers/consumers.

Infrastructure smoke test must already work.

## Phase 10 — Public Cutover

DNS
TLS
callbacks
webhooks
final end-to-end checks

---

# 63. Definition of Done

Production Azure migration is complete when:

- `develop` Test deployment is unchanged and healthy.
- `main` Production deployment targets Azure.
- Azure/GitHub authentication uses OIDC.
- Bicep creates/reconciles production infrastructure.
- VNet/subnets/NSGs are code-defined.
- Infrastructure VM has stable private and public IPs.
- Infrastructure VM has persistent data storage.
- Only ports 80/443 are public.
- No public SSH is required.
- MySQL is private.
- Kafka is private.
- Prometheus is private.
- Grafana native port is private.
- Grafana works through HTTPS Nginx route.
- Gateway is not internet-public directly.
- Gateway works through Nginx.
- All seven application services run in Container Apps.
- All seven services pass readiness/liveness checks.
- Container Apps reach MySQL privately.
- Container Apps reach Kafka privately.
- MySQL uses TLS.
- Prometheus scrapes all expected services.
- Grafana dashboards work.
- Failed revisions roll back safely.
- Production configuration comes from GitHub Environment secrets/variables.
- Real `.env` files are not committed/read as configuration documentation.
- Kafka smoke test succeeds.
- Forwarded-header/rate-limit behavior is fixed and tested.
- DNS/callback/webhook cutover succeeds.
