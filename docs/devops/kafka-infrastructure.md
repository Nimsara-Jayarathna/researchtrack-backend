# ResearchTrack Kafka Infrastructure

## Overview

ResearchTrack Sprint 3 uses Apache Kafka as the base infrastructure for
asynchronous application events.

The test environment deploys one Kafka 3.9.1 broker using Docker Compose. The
broker uses KRaft mode, which removes the requirement for a separate ZooKeeper
service.

This deployment provides infrastructure only. Application-specific producers,
consumers, event handlers, and application topic contracts are implemented by
the development team.

## Deployment Design

| Setting | Value |
|---|---|
| Kafka image | `apache/kafka:3.9.1` |
| Deployment method | Docker Compose |
| Kafka mode | Single-node KRaft broker/controller |
| Compose service | `kafka` |
| Internal endpoint | `kafka:9092` |
| Controller endpoint | `kafka:9093` |
| Docker network | Existing `backend` network |
| Public port | Not published |
| Persistent storage | `kafka-data` Docker volume |
| Restart policy | `unless-stopped` |
| Topic auto-creation | Disabled |
| Default partitions | 1 |
| Test retention | 24 hours |

A single broker is appropriate for the Sprint 3 test environment. It does not
provide high availability and is temporarily unavailable during a restart.

## Network and Listener Configuration

Kafka listens internally using:

```text
PLAINTEXT://:9092