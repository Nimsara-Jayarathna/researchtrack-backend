#!/usr/bin/env python3
"""Render the deployment input for a ResearchTrack Container App or its migration job.

- app: a parameters file for deploy/azure/modules/container-app.bicep
- job: a complete ARM body for Microsoft.App/jobs (applied with an ARM PUT)

Values come from the same validated env files (config/env/*/.env.example
contracts) the Test VPS receives. Secret-like keys become Container App secrets
referenced by name; everything else becomes a plain environment variable.
Prints the content hash of the desired spec.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys

SECRET_KEY_PATTERN = re.compile(
    r"(^ConnectionStrings__|Password$|Secret$|SecretKey$|SigningKey$|ApiKey$|AccessKey$|PrivateKey(Base64)?$)"
)
REVISION_ENV = "RESEARCHTRACK_DEPLOYMENT_REVISION"
REGISTRY_SECRET = "registry-password"


def parse_env_file(path: str) -> list[tuple[str, str]]:
    entries = []
    with open(path, encoding="utf-8") as handle:
        for raw in handle:
            line = raw.rstrip("\r\n")
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            key, _, value = line.partition("=")
            if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
                value = value[1:-1]
            entries.append((key, value))
    return entries


def secret_name(key: str) -> str:
    name = re.sub(r"[^a-z0-9-]", "-", key.lower().replace("__", "-"))
    return re.sub(r"-{2,}", "-", f"env-{name}").strip("-")[:253]


def build_env(env_files: list[str], extra_env: list[str]) -> tuple[list[dict], list[dict]]:
    env: list[dict] = []
    secrets: list[dict] = []
    seen: set[str] = set()
    secret_names: set[str] = set()

    entries = [entry for path in env_files for entry in parse_env_file(path)]
    for item in extra_env:
        key, _, value = item.partition("=")
        entries.append((key, value))

    for key, value in entries:
        if key in seen:
            sys.exit(f"Environment key '{key}' is defined more than once across env files.")
        seen.add(key)
        if SECRET_KEY_PATTERN.search(key):
            name = secret_name(key)
            if name in secret_names:
                sys.exit(f"Secret name collision for '{key}' ({name}).")
            secret_names.add(name)
            secrets.append({"name": name, "value": value})
            env.append({"name": key, "secretRef": name})
        else:
            env.append({"name": key, "value": value})
    return env, secrets


def require_keys(name: str, env_files: list[str], required: list[str]) -> None:
    """Fail before anything is rendered; report key names only, never values."""
    defined = {key for path in env_files for key, value in parse_env_file(path) if value.strip()}
    missing = [key for key in required if key not in defined]
    if missing:
        sources = ", ".join(os.path.basename(path) for path in env_files)
        sys.exit(f"{name}: required configuration missing or empty in composed env files ({sources}): "
                 + ", ".join(missing))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--kind", choices=["app", "job"], default="app")
    parser.add_argument("--service", required=True)
    parser.add_argument("--name", required=True)
    parser.add_argument("--location", required=True)
    parser.add_argument("--environment-id", required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--env-file", action="append", default=[], required=True)
    parser.add_argument("--cpu", default="0.5")
    parser.add_argument("--memory", default="1Gi")
    parser.add_argument("--min-replicas", type=int, default=1)
    parser.add_argument("--max-replicas", type=int, default=1)
    parser.add_argument("--registry-server", default="ghcr.io")
    parser.add_argument("--registry-username", default="")
    parser.add_argument("--job-command", default="/app/dbcheck && /app/migrate")
    # Key names the composed environment must define with a non-empty value
    # (e.g. the shared-auth contract for services using shared JWT validation).
    parser.add_argument("--require", action="append", default=[])
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    env, secrets = build_env(args.env_file, [])
    require_keys(args.name, args.env_file, args.require)

    registry_secret = ""
    registry_password = os.environ.get("REGISTRY_PASSWORD", "")
    if args.registry_username and registry_password:
        secrets.append({"name": REGISTRY_SECRET, "value": registry_password})
        registry_secret = REGISTRY_SECRET

    # Everything that defines the running revision. A content hash of it is
    # added to the container env: it forces a new revision whenever image, env,
    # secrets or resources change (secret value changes alone do not restart
    # revisions in Container Apps) and lets the workflow skip unchanged apps.
    desired = {
        "kind": args.kind,
        "service": args.service,
        "image": args.image,
        "env": env,
        "secrets": secrets,
        "cpu": args.cpu,
        "memory": args.memory,
        "replicas": [args.min_replicas, args.max_replicas],
        "registry": [args.registry_server, args.registry_username, registry_secret],
        "command": args.job_command if args.kind == "job" else None,
    }
    digest = hashlib.sha256(json.dumps(desired, sort_keys=True).encode()).hexdigest()[:16]
    env = env + [{"name": REVISION_ENV, "value": digest}]

    if args.kind == "app":
        # Parameters for deploy/azure/modules/container-app.bicep. `secrets` is a
        # @secure() parameter there, so values never enter deployment history.
        document = {
            "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
            "contentVersion": "1.0.0.0",
            "parameters": {
                "name": {"value": args.name},
                "location": {"value": args.location},
                "environmentId": {"value": args.environment_id},
                "service": {"value": args.service},
                "image": {"value": args.image},
                "env": {"value": env},
                "secrets": {"value": {"items": secrets}},
                "cpu": {"value": args.cpu},
                "memory": {"value": args.memory},
                "minReplicas": {"value": args.min_replicas},
                "maxReplicas": {"value": args.max_replicas},
                "registryServer": {"value": args.registry_server},
                "registryUsername": {"value": args.registry_username},
                "registryPasswordSecretName": {"value": registry_secret},
            },
        }
    else:
        registries = []
        if registry_secret:
            registries.append({
                "server": args.registry_server,
                "username": args.registry_username,
                "passwordSecretRef": registry_secret,
            })
        # Complete ARM body for Microsoft.App/jobs, applied with a PUT (aca-job.sh).
        document = {
            "location": args.location,
            "properties": {
                "environmentId": args.environment_id,
                "workloadProfileName": "Consumption",
                "configuration": {
                    "triggerType": "Manual",
                    "replicaTimeout": 1800,
                    "replicaRetryLimit": 0,
                    "manualTriggerConfig": {"parallelism": 1, "replicaCompletionCount": 1},
                    "secrets": secrets,
                    "registries": registries,
                },
                "template": {
                    "containers": [{
                        "name": args.service,
                        "image": args.image,
                        "command": ["/bin/sh", "-c"],
                        "args": [args.job_command],
                        "env": env,
                        "resources": {"cpu": float(args.cpu), "memory": args.memory},
                    }]
                },
            },
        }

    fd = os.open(args.output, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)
    print(digest)


if __name__ == "__main__":
    main()
