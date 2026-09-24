#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

protected_services=(auth project github jira meeting submission)

die() {
  echo "Shared-auth wiring validation failed: $*" >&2
  exit 1
}

compose_service_block() {
  local service="$1"
  awk -v service="$service" '
    $0 == "  " service ":" { found=1; print; next }
    found && $0 ~ /^  [A-Za-z0-9_-]+:$/ { exit }
    found { print }
  ' deploy/compose.yml
}

for service in "${protected_services[@]}"; do
  block="$(compose_service_block "$service")"
  [[ -n "$block" ]] || die "service '$service' is missing from deploy/compose.yml."
  grep -Fq -- '- ./env/shared-auth.env' <<<"$block" \
    || die "service '$service' does not load ./env/shared-auth.env in deploy/compose.yml."

done

# A shared-auth change must force/reliably trigger recreation for every protected
# service. Keep deploy-stack.sh's environment dependency map in sync with Compose.
for service in "${protected_services[@]}"; do
  grep -Eq "^[[:space:]]*${service}\)[[:space:]]+printf '%s\\\\n' shared-auth\\.env ${service}\\.env ;;" \
    deploy/scripts/deploy-stack.sh \
    || die "deploy-stack.sh does not track shared-auth.env for '$service'."
done

# Local development must load the same shared contract for the same service set.
shared_auth_case_line="$(grep -E '^[[:space:]]*auth\|project\|github\|jira\|meeting\|submission\)' scripts/lib/env.sh || true)"
[[ -n "$shared_auth_case_line" ]] \
  || die "scripts/lib/env.sh protected-service list is not auth|project|github|jira|meeting|submission."

# Every protected API must register the common JWT bearer implementation.
declare -A program_files=(
  [auth]='src/Services/ResearchTrack.AuthService/Program.cs'
  [project]='src/Services/ResearchTrack.ProjectService/Program.cs'
  [github]='src/Services/ResearchTrack.GitHubService/Program.cs'
  [jira]='src/Services/ResearchTrack.JiraService/Program.cs'
  [meeting]='src/Services/ResearchTrack.MeetingService/Program.cs'
  [submission]='src/Services/ResearchTrack.SubmissionService/Program.cs'
)

for service in "${protected_services[@]}"; do
  program="${program_files[$service]}"
  grep -Fq 'AddResearchTrackJwtAuthentication(builder.Configuration)' "$program" \
    || die "$program does not register AddResearchTrackJwtAuthentication."
done

# The protected set is exactly the services registering shared JWT validation,
# so a newly protected service cannot be left out of the lists above.
mapfile -t jwt_programs < <(grep -rl --include=Program.cs 'AddResearchTrackJwtAuthentication(builder.Configuration)' src | sort)
mapfile -t expected_programs < <(printf '%s\n' "${program_files[@]}" | sort)
[[ "${jwt_programs[*]}" == "${expected_programs[*]}" ]] \
  || die "services registering AddResearchTrackJwtAuthentication (${jwt_programs[*]}) differ from the protected list."

# Azure Production composes the shared-auth contract from the same metadata the
# deployment uses (deploy/build/service-impact.py); it must mark exactly the
# protected services, and nothing else, as shared-auth consumers.
for service in gateway "${protected_services[@]}"; do
  expected=false
  [[ -n "${program_files[$service]:-}" ]] && expected=true
  actual="$(python3 deploy/build/service-impact.py meta --service "$service" | sed -n 's/^shared_auth=//p')"
  [[ "$actual" == "$expected" ]] \
    || die "deploy/build/service-impact.py marks '$service' shared_auth=${actual:-unset}, expected $expected (Azure would not compose shared-auth.env correctly)."
done

echo "Shared JWT wiring is consistent across all protected business API services (Test, local and Azure Production)."
