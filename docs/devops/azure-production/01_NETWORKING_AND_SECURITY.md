# Networking and Security Plan

## VNet

```text
vnet-researchtrack-prod
10.20.0.0/16
```

Subnets:

```text
snet-researchtrack-infra-prod
10.20.10.0/24

snet-researchtrack-containerapps-prod
10.20.20.0/23
```

The Container Apps subnet is dedicated to the Container Apps environment.

## VM addresses

Recommended stable VM private IP:

```text
10.20.10.4
```

One Standard Static Public IPv4 is attached to the VM.

## Public traffic

Only:

```text
80/tcp
443/tcp
```

Public DNS:

```text
api.researchtrack.blipzo.xyz
grafana.researchtrack.blipzo.xyz
```

Both resolve to the VM public IP.

## Private traffic

```text
Container Apps subnet → VM:3306 MySQL
Container Apps subnet → VM:9092 Kafka
VM → Container Apps HTTPS
```

No public access:

```text
22
3000
3306
9090
9092
```

## Internal Container Apps environment

The environment is internal-only.

Apps that the VM must reach use ingress that is reachable at the VNet boundary.

This does not make them public because the outer Container Apps environment has no public endpoint.

## Private DNS

The VM must resolve Container App FQDNs.

Create a Private DNS zone for the environment default domain.

Create a wildcard A record pointing to the Container Apps environment static IP.

Link the Private DNS zone to the production VNet.

Bicep owns this configuration.

## Nginx

API:

```text
Internet
→ VM :443
→ Nginx
→ private Gateway Container App
```

Grafana:

```text
Internet
→ VM :443
→ Nginx
→ grafana:3000
```

Nginx injects:

```text
Host
X-Real-IP
X-Forwarded-For
X-Forwarded-Proto
```

Gateway must trust forwarded headers only from the expected proxy/network.

## TLS

Public traffic:
- Let's Encrypt on Nginx.

MySQL:
- require encrypted connections.
- initial minimum `SslMode=Required`.
- stronger CA verification may be introduced later.

Kafka:
- target TLS/private secured listener.
- never expose plaintext Kafka publicly.

Same-host Docker:
- Grafana → Prometheus may remain HTTP.
- Nginx → Grafana may remain HTTP.

## Security principle

VNet isolation controls reachability.
TLS protects transport.
Use both where traffic crosses VM/Container App boundaries.
