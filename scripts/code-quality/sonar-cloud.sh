#!/usr/bin/env bash
# Isolated SonarQube Cloud .NET 10 scan + existing unit-test coverage import.
# No mutation, load, or unrelated CI workflows are modified.
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
ResearchTrack SonarQube Cloud — build-aware production .NET scan with unit coverage

  bash ./scripts/code-quality/sonar-cloud.sh doctor
  bash ./scripts/code-quality/sonar-cloud.sh setup
  bash ./scripts/code-quality/sonar-cloud.sh scan [run-id]
  bash ./scripts/code-quality/sonar-cloud.sh self-test

Required environment variables:
  SONAR_TOKEN, SONAR_PROJECT_KEY, SONAR_ORGANIZATION
Optional:
  SONAR_REGION=us (EU/sonarcloud.io is the default)
  SONAR_ENFORCE_GATE=true (default false during initial baselining)

Run from main. The scanner itself collects existing unit test coverage; it never runs mutation/load workflows.
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
# Existing xUnit v3 projects already reference coverlet.collector; no changes
# to unit test projects, other students' test code, or their CI jobs required.
TEST_PROJECTS=(
  'tests/ResearchTrack.AuthService.Tests/ResearchTrack.AuthService.Tests.csproj'
  'tests/ResearchTrack.Gateway.Tests/ResearchTrack.Gateway.Tests.csproj'
  'tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj'
  'tests/ResearchTrack.JiraService.Tests/ResearchTrack.JiraService.Tests.csproj'
  'tests/ResearchTrack.MeetingService.Tests/ResearchTrack.MeetingService.Tests.csproj'
  'tests/ResearchTrack.ProjectService.Tests/ResearchTrack.ProjectService.Tests.csproj'
  'tests/ResearchTrack.SubmissionService.Tests/ResearchTrack.SubmissionService.Tests.csproj'
)
for proj in "${TEST_PROJECTS[@]}"; do
  [[ -f "$ROOT/$proj" ]] || { echo "ERROR: missing unit test project $proj" >&2; exit 2; }
  grep -q 'PackageReference Include="coverlet.collector"' "$ROOT/$proj" || {
    echo "ERROR: $proj is missing coverlet.collector" >&2; exit 2;
  }
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
  echo "PASS: .NET $SDK; eight production projects; seven existing coverlet-enabled unit suites; SonarQube credentials available (values hidden)."
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
  # SonarScanner for .NET 11.x is a begin/end tool. Unlike typical CLIs,
  # `dotnet-sonarscanner --version` exits 1, even after a successful install.
  # Verify its package/version with the .NET tool registry instead.
  local installed_tools
  installed_tools="$(dotnet tool list --tool-path "$TOOL_DIR")"
  if ! printf '%s\n' "$installed_tools" | awk -v expected="$VERSION" \
    'tolower($1) == "dotnet-sonarscanner" && $2 == expected { found=1 } END { exit !found }'; then
    echo "ERROR: Expected dotnet-sonarscanner $VERSION in $TOOL_DIR." >&2
    printf '%s\n' "$installed_tools" >&2
    return 1
  fi
  [[ -x "$SCANNER" ]] || { echo "ERROR: SonarScanner executable not found: $SCANNER" >&2; return 1; }
  echo "PASS: SonarScanner for .NET $VERSION is installed."
}
if [[ "$MODE" == setup ]]; then install_scanner; exit 0; fi

# Report files are purposefully ignored by Git and never carry secret tokens.
REPORT="$ROOT/artifacts/code-quality/sonar/$RUN"
mkdir -p "$REPORT"
install_scanner

# Pre-restore outside the SonarScanner hooks to avoid scanner-instrumented restores.
# Restore production and existing unit test dependencies; no mutation suites.
for proj in "${PROJECTS[@]}" "${TEST_PROJECTS[@]}"; do
  echo "Restoring $proj"
  dotnet restore "$proj" --nologo \
    -p:TreatWarningsAsErrors=false -p:CodeAnalysisTreatWarningsAsErrors=false
done

# The reports are emitted later by coverlet (format=opencover), while the
# scanner is active, and SonarQube imports only actual reported execution.
COVERAGE_DIR="$REPORT/coverage"
# A repeated local run ID must not accept stale reports from an earlier run.
rm -rf "$COVERAGE_DIR"
rm -f "$REPORT/report-task.txt"
mkdir -p "$COVERAGE_DIR"
BEGIN=(
  begin "/k:${SONAR_PROJECT_KEY}" "/o:${SONAR_ORGANIZATION}"
  "/d:sonar.token=${SONAR_TOKEN}"
  '/d:sonar.dotnet.excludeTestProjects=true'
  '/d:sonar.scanner.scanAll=false'
  "/d:sonar.cs.opencover.reportsPaths=artifacts/code-quality/sonar/$RUN/coverage/**/coverage.opencover.xml"
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

# Unit execution happens between scanner begin/end. SonarQube reads OpenCover
# files at `end`. Fail closed if any suite or its report is missing.
COVERAGE_MANIFEST="$REPORT/coverage-files.txt"
: > "$COVERAGE_MANIFEST"
for proj in "${TEST_PROJECTS[@]}"; do
  name="$(basename "$proj" .csproj)"
  destination="$COVERAGE_DIR/$name"
  mkdir -p "$destination"
  echo "Coverage test suite: $name (existing unit/mocking tests only)"
  dotnet test "$proj" -c Release --no-restore --nologo \
    -p:TreatWarningsAsErrors=false -p:CodeAnalysisTreatWarningsAsErrors=false \
    --filter 'Category!=DatabaseIntegration' \
    --collect:'XPlat Code Coverage;Format=opencover' \
    --results-directory "$destination"
  mapfile -d '' REPORTS < <(find "$destination" -type f -name 'coverage.opencover.xml' -print0)
  if (( ${#REPORTS[@]} != 1 )) || [[ ! -s "${REPORTS[0]:-}" ]]; then
    echo "ERROR: Expected one non-empty OpenCover report for $name, found ${#REPORTS[@]}." >&2
    exit 1
  fi
  if ! grep -q '<CoverageSession' "${REPORTS[0]}"; then
    echo "ERROR: Invalid OpenCover document from $name (missing CoverageSession)." >&2
    exit 1
  fi
  printf '%s\n' "${REPORTS[0]#"$ROOT/"}" >> "$COVERAGE_MANIFEST"
done
if [[ "$(wc -l < "$COVERAGE_MANIFEST")" != "${#TEST_PROJECTS[@]}" ]]; then
  echo 'ERROR: coverage manifest does not include all seven suites' >&2; exit 1
fi
{
  echo '# SonarQube unit coverage collection evidence'
  echo
  printf 'Existing test suites executed: **%d**\n\n' "${#TEST_PROJECTS[@]}"
  echo 'Format: **OpenCover XML** collected with `coverlet.collector`.'
  echo 'Filter: `Category!=DatabaseIntegration` (same as existing group unit workflow).'
  echo 'Reports are imported by `sonar.cs.opencover.reportsPaths` at SonarScanner end.'
  echo 'This file records collector output, not an estimated coverage percentage.'
  echo
  echo 'Reports:'
  while IFS= read -r file; do printf -- '- `%s`\n' "$file"; done < "$COVERAGE_MANIFEST"
} > "$REPORT/coverage-collection-summary.md"
echo "PASS: ${#TEST_PROJECTS[@]} genuine OpenCover reports generated."
echo 'SonarScanner end: submitting analysis and importing coverage to SonarQube Cloud'
"$SCANNER" end "/d:sonar.token=${SONAR_TOKEN}"
echo 'PASS: SonarScanner analysis submitted. Review actual coverage and gate after cloud processing.'
