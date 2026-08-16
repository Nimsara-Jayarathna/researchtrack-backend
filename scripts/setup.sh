#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root

rt_require_command dotnet
rt_require_command git

printf 'Checking .NET SDK...\n'
dotnet --version
if ! dotnet --list-sdks | grep -Eq '^10\.0\.(3[0-9]{2}|[4-9][0-9]{2})'; then
  echo "ResearchTrack expects .NET 10 SDK 10.0.300 or a compatible later .NET 10 feature band." >&2
  echo "global.json defines the repository SDK policy." >&2
  exit 1
fi

if [[ ! -f .env.local ]]; then
  cp .env.example .env.local
  chmod 600 .env.local 2>/dev/null || true
  echo "Created .env.local from .env.example (gitignored)."
  echo "IMPORTANT: replace every CHANGE_ME database password before running DB-dependent commands."
else
  echo ".env.local already exists; leaving it unchanged."
  chmod 600 .env.local 2>/dev/null || true
fi

# Validate file syntax even if credentials have not been filled yet.
rt_load_env_file .env.local
rt_warn_if_insecure_permissions .env.local

dotnet tool restore
dotnet restore ResearchTrack.sln

if command -v mysql >/dev/null 2>&1; then
  echo "MySQL client detected."
else
  echo "NOTE: MySQL CLI client was not found. Install a MySQL 8+ compatible client before using DB scripts." >&2
fi

if command -v curl >/dev/null 2>&1; then
  echo "curl detected."
else
  echo "NOTE: curl was not found. Install curl before using health.sh." >&2
fi

echo
echo "Setup complete."
echo "Next:"
echo "  1. Edit .env.local and replace all CHANGE_ME values."
echo "  2. Ensure the configured MySQL endpoint is reachable."
echo "  3. ./scripts/db-status.sh"
echo "  4. ./scripts/migrate.sh all"
echo "  5. ./scripts/dev.sh core"
echo "  6. ./scripts/health.sh core"
echo
echo "Database administrators only: copy .env.admin.example to .env.admin.local and run ./scripts/db-init.sh when provisioning is required."
