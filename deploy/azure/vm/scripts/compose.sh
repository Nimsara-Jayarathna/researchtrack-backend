#!/usr/bin/env bash
# docker compose for the infrastructure stack with the runtime interpolation file.
set -euo pipefail
exec docker compose \
  --project-directory /opt/researchtrack \
  --env-file /opt/researchtrack/runtime/infra.env \
  -f /opt/researchtrack/compose.yml \
  "$@"
