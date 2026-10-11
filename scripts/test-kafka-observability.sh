#!/usr/bin/env bash
# Offline checks only; Docker is needed for Compose parsing even with native promtool.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
command -v docker >/dev/null || { echo 'NOT EXECUTED: Docker CLI required for Compose parsing.' >&2; exit 2; }
if [[ -n "${PROMTOOL:-}" ]]; then
  exec python3 "$root/deploy/monitoring/tests/validate_kafka_observability.py" --promtool "$PROMTOOL"
elif command -v promtool >/dev/null; then
  exec python3 "$root/deploy/monitoring/tests/validate_kafka_observability.py" --promtool "$(command -v promtool)"
else
  docker info >/dev/null 2>&1 || { echo 'NOT EXECUTED: provide PROMTOOL or a running Docker daemon.' >&2; exit 2; }
  exec python3 "$root/deploy/monitoring/tests/validate_kafka_observability.py" --docker
fi
