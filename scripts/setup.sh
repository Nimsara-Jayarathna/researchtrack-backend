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
  exit 1
fi

for service in gateway auth project github jira meeting submission; do
  example="config/env/$service/.env.example"
  local_file="config/env/$service/.env.local"
  if [[ ! -f "$local_file" ]]; then
    cp "$example" "$local_file"
    chmod 600 "$local_file" 2>/dev/null || true
    echo "Created $local_file"
  else
    echo "$local_file already exists; leaving it unchanged."
  fi
  rt_load_env_file "$local_file"
  rt_warn_if_insecure_permissions "$local_file"
done

dotnet tool restore
dotnet restore ResearchTrack.sln

echo
echo "Setup complete. Replace every CHANGE_ME value in the service .env.local files you intend to run."
echo "Database administrators only: copy config/env/admin/.env.example to config/env/admin/.env.local before ./scripts/db-init.sh."
