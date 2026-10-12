#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# SonarQube Cloud uses an entirely separate runner, never changes native
# CSharpier/Roslyn behavior or invokes the other testing paradigms.
if [[ "${1:-}" == sonar ]]; then
  shift
  exec bash "$ROOT/scripts/code-quality/sonar-cloud.sh" scan "$@"
fi
exec bash "$ROOT/scripts/code-quality/run.sh" "$@"
