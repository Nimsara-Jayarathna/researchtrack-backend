#!/usr/bin/env bash
# Isolated SonarQube Cloud .NET 10 scan. No test/coverage/mutation/k6 execution.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
MODE="${1:-help}"
RUN="${2:-local}"
VERSION='11.3.0'
TOOL_DIR="$ROOT/artifacts/code-quality/sonar-tools"
SCANNER="$TOOL_DIR/dotnet-sonarscanner"
# Windows installs a .exe tool shim; Git Bash can execute it directly.
[[ ! -f "${SCANNER}.exe" ]] || SCANNER="${SCANNER}.exe"

usage() {
  cat <<'USAGE'
ResearchTrack SonarQube Cloud — build-aware production .NET scan

  bash ./scripts/code-quality/sonar-cloud.sh doctor
  bash ./scripts/code-quality/sonar-cloud.sh setup
  bash ./scripts/code-quality/sonar-cloud.sh scan [run-id]
  bash ./scripts/code-quality/sonar-cloud.sh self-test

Required environment variables:
  SONAR_TOKEN, SONAR_PROJECT_KEY, SONAR_ORGANIZATION
Optional:
  SONAR_REGION=us (EU/sonarcloud.io is the default)
  SONAR_ENFORCE_GATE=true (default false during initial baselining)

Run on the main branch for the first real analysis. No other test workflows run.
USAGE
}
if [[ "$MODE" == help || "$MODE" == -h || "$MODE" == --help ]]; then usage; exit 0; fi
if [[ "$MODE" == self-test ]]; then exec bash "$ROOT/scripts/code-quality/sonar-self-test.sh"; fi
case "$MODE" in doctor|setup|scan) ;; *) usage >&2; exit 2 ;; esac
if ! [[ "$RUN" =~ ^[a-zA-Z0-9][a-zA-Z0-9._-]{0,55}$ ]]; then
  echo 'Invalid run identifier' >&2; exit 2
fi
if ! command -v dotnet >/dev/null; then echo 'ERROR: .NET SDK missing' >&2; exit 2; fi
SDK="$(dotnet --version)"
[[ "$SDK" == 10.* ]] || { echo "ERROR: .NET 10 required; found $SDK" >&2; exit 2; }

# The list is deliberate and stable. A future 9th project is an explicit scope change.
PROJECTS=(
  'src/BuildingBlocks/ResearchTrack.BuildingBlocks.Api/ResearchTrack.BuildingBlocks.Api.csproj'
  'src/Gateway/ResearchTrack.Gateway/ResearchTrack.Gateway.csproj'
  'src/Services/ResearchTrack.AuthService/ResearchTrack.AuthService.csproj'
  'src/Services/ResearchTrack.GitHubService/ResearchTrack.GitHubService.csproj'
  'src/Services/ResearchTrack.JiraService/ResearchTrack.JiraService.csproj'
  'src/Services/ResearchTrack.MeetingService/ResearchTrack.MeetingService.csproj'
  'src/Services/ResearchTrack.ProjectService/ResearchTrack.ProjectService.csproj'
  'src/Services/ResearchTrack.SubmissionService/ResearchTrack.SubmissionService.csproj'
)
for proj in "${PROJECTS[@]}"; do
  [[ -f "$ROOT/$proj" ]] || { echo "ERROR: missing production project $proj" >&2; exit 2; }
done
mapfile -d '' FOUND < <(find src -name '*.csproj' -type f -print0 | sort -z)
[[ "${#FOUND[@]}" == 8 ]] || { echo "ERROR: expected exactly 8 production projects, found ${#FOUND[@]}" >&2; exit 2; }

if [[ "$MODE" == doctor || "$MODE" == scan ]]; then
  for key in SONAR_TOKEN SONAR_PROJECT_KEY SONAR_ORGANIZATION; do
    [[ -n "${!key:-}" ]] || { echo "ERROR: required environment variable $key is missing" >&2; exit 2; }
  done
  case "${SONAR_REGION:-eu}" in ''|eu|us) ;; *) echo 'ERROR: SONAR_REGION must be eu or us' >&2; exit 2 ;; esac
  case "${SONAR_ENFORCE_GATE:-false}" in true|false|'') ;; *) echo 'ERROR: SONAR_ENFORCE_GATE must be true or false' >&2; exit 2 ;; esac
fi
if [[ "$MODE" == doctor ]]; then
  echo "PASS: .NET $SDK; eight production projects; SonarQube credentials available (values hidden)."
  echo "Region: ${SONAR_REGION:-eu}; gate enforcement: ${SONAR_ENFORCE_GATE:-false}"
  exit 0
fi

install_scanner() {
  if [[ -x "$SCANNER" ]]; then
    echo 'SonarScanner already present in ignored tool directory.'
  else
    mkdir -p "$TOOL_DIR"
    dotnet tool install --tool-path "$TOOL_DIR" dotnet-sonarscanner --version "$VERSION"
    [[ ! -f "${SCANNER}.exe" ]] || SCANNER="${SCANNER}.exe"
  fi
  "$SCANNER" --version
}
if [[ "$MODE" == setup ]]; then install_scanner; exit 0; fi

# Report files are purposefully ignored by Git and never carry secret tokens.
REPORT="$ROOT/artifacts/code-quality/sonar/$RUN"
mkdir -p "$REPORT"
install_scanner

# Pre-restore outside the SonarScanner hooks. Do not restore tests or tools.
# Only eight production projects are passed to `dotnet restore` / `dotnet build`.
for proj in "${PROJECTS[@]}"; do
  echo "Restoring $proj"
  dotnet restore "$proj" --nologo \
    -p:TreatWarningsAsErrors=false -p:CodeAnalysisTreatWarningsAsErrors=false
done

BEGIN=(
  begin "/k:${SONAR_PROJECT_KEY}" "/o:${SONAR_ORGANIZATION}"
  "/d:sonar.token=${SONAR_TOKEN}"
  '/d:sonar.dotnet.excludeTestProjects=true'
  '/d:sonar.scanner.scanAll=false'
  '/d:sonar.exclusions=**/Migrations/**,**/*.Designer.cs,**/*.g.cs,**/*.generated.cs,**/obj/**,**/bin/**'
  "/d:sonar.qualitygate.wait=${SONAR_ENFORCE_GATE:-false}"
  '/d:sonar.qualitygate.timeout=600'
  "/d:sonar.scanner.metadataFilePath=$REPORT/report-task.txt"
)
if [[ "${SONAR_REGION:-eu}" == us ]]; then BEGIN+=('/d:sonar.region=us'); fi

# Never echo the arguments: the scanner authentication token is one of them.
echo 'SonarScanner begin (credentials masked by runner)'
"$SCANNER" "${BEGIN[@]}"

# Shared BuildingBlocks is built first, then the other seven projects.
# BuildProjectReferences=false prevents repeated assembly analyses and keeps
# the build graph strictly limited to the eight listed production projects.
for proj in "${PROJECTS[@]}"; do
  echo "SonarScanner build: $proj"
  dotnet build "$proj" -c Release --no-restore --no-incremental --nologo \
    -p:BuildProjectReferences=false \
    -p:TreatWarningsAsErrors=false -p:CodeAnalysisTreatWarningsAsErrors=false
done

echo 'SonarScanner end: submitting findings to SonarQube Cloud'
"$SCANNER" end "/d:sonar.token=${SONAR_TOKEN}"
echo 'PASS: SonarScanner analysis submitted. Review metrics after cloud processing.'
