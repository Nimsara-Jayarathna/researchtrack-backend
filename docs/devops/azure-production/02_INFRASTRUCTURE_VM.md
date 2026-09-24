# Infrastructure VM Plan

## Purpose

The VM hosts infrastructure only:

```text
Nginx
MySQL
Kafka
Prometheus
Grafana
```

No ResearchTrack application microservice runs directly on this VM.

## Initial size

Target:

```text
2 vCPU
8 GiB RAM
Ubuntu LTS
non-Spot
64 GiB+ persistent data disk
```

Keep VM size parameterized in Bicep.

## Filesystem

```text
/opt/researchtrack/
├── compose.yml
├── runtime/
├── nginx/
├── mysql/
├── kafka/
├── prometheus/
└── grafana/

/data/researchtrack/
├── mysql/
├── kafka/
├── prometheus/
└── grafana/
```

`/opt` contains replaceable deployment configuration.

`/data` contains persistent state.

## Bootstrap responsibilities

The VM bootstrap script must:

- patch OS,
- install Docker,
- install Compose plugin,
- enable Docker,
- discover/mount persistent data disk,
- create directories,
- set ownership/permissions,
- configure log rotation,
- create deployment directory,
- deploy Compose stack.

It must be idempotent.

It must never format a disk that already contains production data.

## Docker Compose

Containers:

```text
nginx
mysql
kafka
prometheus
grafana
```

Dedicated network:

```text
researchtrack-infra
```

## Public bindings

Only Nginx:

```text
80
443
```

## Private bindings

MySQL:

```text
VM private IP :3306
```

Kafka:

```text
VM private IP :9092
```

Prometheus/Grafana should not require public bindings.

## MySQL

Version:

```text
MySQL 8.4
```

Persist:

```text
/data/researchtrack/mysql
```

Do not use root for application runtime.

Use health checks.

Require TLS.

Reuse existing service migrations.

## Kafka

Single KRaft broker/controller.

Persist:

```text
/data/researchtrack/kafka
```

Advertise the VM private address/private DNS.

Never advertise localhost or public VM IP to Container Apps.

Use a private secure listener.

## Prometheus

Persist:

```text
/data/researchtrack/prometheus
```

Suggested assignment retention:

```text
7–15 days
```

Scrape all seven Container Apps over private VNet.

Use stable labels.

## Grafana

Persist:

```text
/data/researchtrack/grafana
```

Datasource:

```text
http://prometheus:9090
```

Public access only through Nginx.

## Capacity controls

Kafka retention:

```text
3–7 days initially
```

Prometheus retention:

```text
7–15 days
```

Docker log rotation must be configured.

Disk usage must be monitored.

## Accepted limitation

This VM is one failure domain for ingress, DB, Kafka and monitoring.

That is accepted for the short academic deployment.
