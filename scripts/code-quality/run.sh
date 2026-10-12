#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
QUALITY_DIR="$ROOT/scripts/code-quality"
TOOL_MANIFEST="$QUALITY_DIR/.config/dotnet-tools.json"
REPORT_ROOT="$ROOT/artifacts/code-quality"
MODE="${1:-help}"
RUN="${2:-local}"
cd "$ROOT"

help_text() {
  cat <<'HELP'
ResearchTrack code quality — production C# only; .NET 10; no Python

  bash ./scripts/code-quality.sh setup              # restore pinned CSharpier 1.3.0
  bash ./scripts/code-quality.sh fix [run-id]        # safe IDE fixes, then CSharpier
  bash ./scripts/code-quality.sh check [run-id]      # verify formatting, style and build (gate)
  bash ./scripts/code-quality.sh analyze [run-id]    # complete Roslyn SARIF (no gate on existing warnings)
  bash ./scripts/code-quality.sh bootstrap [run-id]  # setup -> fix -> check
  bash ./scripts/code-quality.sh doctor             # check environment and repo configuration
  bash ./scripts/code-quality.sh self-test          # simulated orchestration tests; no SDK needed

A failing check exits nonzero and never rewrites source. Fix changes only eligible
production .cs files; EF migrations, generated files and tests are protected.
Machine-readable reports: artifacts/code-quality/<run-id>/status.tsv,
manifest.json, formatting logs and Roslyn SARIF. SonarQube has separate Actions.
HELP
}
if [[ "$MODE" == help || "$MODE" == --help || "$MODE" == -h ]]; then help_text; exit 0; fi
if [[ "$MODE" == self-test ]]; then exec bash "$QUALITY_DIR/self-test.sh"; fi
case "$MODE" in setup|fix|check|analyze|bootstrap|doctor) ;; *) echo "Unknown mode: $MODE" >&2; help_text; exit 2 ;; esac
if ! [[ "$RUN" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]{0,55}$ ]]; then echo "Invalid run-id: $RUN" >&2; exit 2; fi

require_sdk() {
  command -v dotnet >/dev/null || { echo 'ERROR: .NET SDK missing' >&2; return 2; }
  local version; version="$(dotnet --version)" || return 2
  if [[ "$version" != 10.* ]]; then echo "ERROR: .NET 10 required; found $version" >&2; return 2; fi
}
require_files() {
  local missing=0 file
  for file in \
    .csharpierrc .csharpierignore src/.editorconfig \
    src/Directory.Build.targets config/testing/code-quality/roslyn.globalconfig \
    scripts/code-quality/.config/dotnet-tools.json ResearchTrack.sln; do
    if [[ -f "$ROOT/$file" ]]; then printf '  OK: %s\n' "$file"; else printf '  MISSING: %s\n' "$file" >&2; missing=1; fi
  done
  (( missing == 0 ))
}
# Discover explicit eligible C# source files, never recursively pass an entire
# project directory (which could format XML/project files or migrations).
mapfile -d '' CS_FILES < <(find "$ROOT/src" -type f -iname '*.cs' \
  ! -ipath '*/Migrations/*' ! -ipath '*/bin/*' ! -ipath '*/obj/*' \
  ! -iname '*.g.cs' ! -iname '*.g.i.cs' ! -iname '*.generated.cs' \
  ! -iname '*.Designer.cs' ! -iname 'TemporaryGeneratedFile_*.cs' \
  ! -iname 'AssemblyInfo.cs' -print0 | sort -z)
mapfile -d '' PROJECTS < <(find "$ROOT/src" -type f -name '*.csproj' \
  ! -path '*/bin/*' ! -path '*/obj/*' -print0 | sort -z)
mapfile -d '' EXCLUSIONS < <(find "$ROOT/src" -type d -name Migrations -print0 | sort -z)
mapfile -d '' GENERATED < <(find "$ROOT/src" -type f -iname '*.cs' \
  \( -ipath '*/Migrations/*' -o -iname '*.g.cs' -o -iname '*.g.i.cs' -o -iname '*.generated.cs' -o -iname '*.Designer.cs' -o -iname 'TemporaryGeneratedFile_*.cs' -o -iname 'AssemblyInfo.cs' \) -print0 | sort -z)
if (( ${#CS_FILES[@]} == 0 || ${#PROJECTS[@]} != 8 )); then
  echo "ERROR: Expected eligible C# source files and eight production projects. Got ${#CS_FILES[@]} files and ${#PROJECTS[@]} projects." >&2; exit 2
fi

if [[ "$MODE" == doctor ]]; then
  echo 'ResearchTrack C# quality doctor'; require_sdk; require_files
  echo "C# eligible files: ${#CS_FILES[@]}; protected/generated files: ${#GENERATED[@]}; projects: ${#PROJECTS[@]}"
  echo 'CSharpier 1.3.0 will be installed by setup into the separate code-quality tool manifest.'
  exit 0
fi
if [[ "$MODE" == setup ]]; then
  require_sdk
  require_files
  echo "Restoring pinned CSharpier 1.3.0 from $TOOL_MANIFEST"
  dotnet tool restore --tool-manifest "$TOOL_MANIFEST"
  ( cd "$QUALITY_DIR" && dotnet csharpier --version )
  exit 0
fi

require_sdk
REPORT="$REPORT_ROOT/$RUN"
mkdir -p "$REPORT/raw" "$REPORT/roslyn"
LEDGER="$REPORT/status.tsv"
printf 'step\tstatus\texit_code\tlog\n' > "$LEDGER"
LAST_RC=0
TOTAL_FAILURES=0
run_step() {
  local name="$1"; shift
  local logfile="$REPORT/raw/$name.log"
  echo "> $name"
  set +e
  "$@" > "$logfile" 2>&1
  LAST_RC=$?
  set -e
  if (( LAST_RC == 0 )); then
    printf '%s\tpass\t0\t%s\n' "$name" "raw/$name.log" >> "$LEDGER"
    echo "  PASS"
  else
    printf '%s\tfail\t%s\t%s\n' "$name" "$LAST_RC" "raw/$name.log" >> "$LEDGER"
    echo "  FAIL ($LAST_RC): $logfile" >&2
    tail -n 6 "$logfile" >&2 || true
    TOTAL_FAILURES=$((TOTAL_FAILURES+1))
  fi
}
write_report() {
  local timestamp sdk branch commit step state rc log comma=""
  timestamp="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  sdk="$(dotnet --version 2>/dev/null || echo unavailable)"
  branch="$(git branch --show-current 2>/dev/null || echo unversioned)"
  commit="$(git rev-parse HEAD 2>/dev/null || echo unversioned)"
  local summary="$REPORT/code-quality-summary.md" json="$REPORT/manifest.json"
  {
    printf '# ResearchTrack Code Quality — %s\n\n' "$MODE"
    printf 'Date UTC: `%s`  \nSDK: `%s`  \nRun: `%s`  \n' "$timestamp" "$sdk" "$RUN"
    printf 'Branch: `%s`  \nCommit: `%s`  \n' "$branch" "$commit"
    printf 'Eligible production C# files: **%d**. Compiler projects: **%d**.\n\n' "${#CS_FILES[@]}" "${#PROJECTS[@]}"
    printf 'CSharpier pinned: **1.3.0**. Roslyn profile: opt-in; SonarQube uses a separate workflow.\n\n'
    printf '| Step | Result | Exit | Log |\n|---|---|---:|---|\n'
    tail -n +2 "$LEDGER" | while IFS=$'\t' read -r step state rc log; do
      printf '| %s | %s | %s | `%s` |\n' "$step" "$state" "$rc" "$log"
    done
    printf '\nFailures: **%d**. A completed tool step does not prove zero informational analyzer findings.\n' "$TOTAL_FAILURES"
    printf '\nRaw Roslyn SARIF: `roslyn/*.sarif`. All source edits (if any) must be reviewed with `git diff`.\n'
  } > "$summary"
  {
    printf '{"mode":"%s","run":"%s","utc":"%s","sdk":"%s","csharpier":"1.3.0","eligible_files":%d,"production_projects":%d,"failures":%d,"steps":[' \
      "$MODE" "$RUN" "$timestamp" "$sdk" "${#CS_FILES[@]}" "${#PROJECTS[@]}" "$TOTAL_FAILURES"
    tail -n +2 "$LEDGER" | while IFS=$'\t' read -r step state rc log; do
      printf '%s{"name":"%s","status":"%s","exit_code":%s,"log":"%s"}' "$comma" "$step" "$state" "$rc" "$log"
      comma=,
    done
    printf ']}\n'
  } > "$json"
  echo "Report: $summary"
}
finalize() {
  write_report
  if (( TOTAL_FAILURES > 0 )); then
    echo "ERROR: $TOTAL_FAILURES quality step(s) failed; check $REPORT/raw/" >&2
    return 1
  fi
  echo "PASS: $MODE on ${#CS_FILES[@]} production C# files"
}
# Avoid silently allowing `check` to run against a missing tool or a wrong
# installed version. When not installed, restore the *isolated* manifest.
run_csharpier() {
  local operation="$1"; shift
  ( cd "$QUALITY_DIR" && dotnet csharpier "$operation" "$@" )
}
ensure_csharpier() {
  if ! (cd "$QUALITY_DIR" && dotnet csharpier --version >/dev/null 2>&1); then
    run_step 'restore-csharpier' dotnet tool restore --tool-manifest "$TOOL_MANIFEST"
    if (( LAST_RC != 0 )); then return 1; fi
  fi
  local actual
  actual="$(cd "$QUALITY_DIR" && dotnet csharpier --version | tr -d '\r' | head -1)" || return 1
  if [[ "$actual" != *1.3.0* ]]; then echo "ERROR: CSharpier version mismatch: $actual" >&2; return 1; fi
}
# Run explicit batches to avoid Windows command-line length limits, and avoid
# accidentally formatting XML project files included by directory scanning.
csharpier_batches() {
  local operation="$1" start size=36 n=0 batch=()
  for (( start=0;start<${#CS_FILES[@]};start+=size )); do
    batch=("${CS_FILES[@]:start:size}")
    n=$((n+1))
    if [[ "$operation" == format ]]; then
      run_step "csharpier-format-$n" run_csharpier format "${batch[@]}" \
        --config-path "$ROOT/.csharpierrc" --ignore-path "$ROOT/.csharpierignore" --no-cache
    else
      run_step "csharpier-check-$n" run_csharpier check "${batch[@]}" \
        --config-path "$ROOT/.csharpierrc" --ignore-path "$ROOT/.csharpierignore"
    fi
  done
}
# `dotnet format style` owns only preapproved IDE0005/IDE0065 code actions.
# The .NET formatter DOES NOT own whitespace (to avoid IDE0055 conflict).
style_pass() {
  local verb="$1" project name project_dir exclusions=() file
  for project in "${PROJECTS[@]}"; do
    name="$(basename "${project%.csproj}")"
    project_dir="$(dirname "$project")"
    exclusions=()
    for file in "${EXCLUSIONS[@]}" "${GENERATED[@]}"; do
      # dotnet format resolves include/exclude against the repository's cwd;
      # absolute paths led to generated migration files being checked in CI.
      [[ "$file" == "$project_dir/"* ]] && exclusions+=("./${file#"$ROOT/"}")
    done
    local flags=(--no-restore --severity info --diagnostics IDE0005 IDE0065)
    flags+=(--include "./${project_dir#"$ROOT/"}")
    if [[ "$verb" == verify ]]; then flags+=(--verify-no-changes); fi
    if (( ${#exclusions[@]} )); then flags+=(--exclude "${exclusions[@]}"); fi
    mkdir -p "$REPORT/formatting/$name"
    run_step "style-$verb-$name" dotnet format style "$project" "${flags[@]}" \
      --report "$REPORT/formatting/$name"
  done
}
# No tests, mutations, performance or coverage processes. These eight builds
# compile production source only, and never run any tests.
compile_production() {
  local analysis="$1" project name args=()
  for project in "${PROJECTS[@]}"; do
    name="$(basename "${project%.csproj}")"
    args=(build "$project" -c Release --no-restore --nologo \
      -p:TreatWarningsAsErrors=false -p:CodeAnalysisTreatWarningsAsErrors=false)
    if [[ "$analysis" == yes ]]; then
      args+=(-t:Rebuild -p:ResearchTrackQualityAnalysis=true \
        "-p:ResearchTrackQualityRun=$RUN")
    fi
    run_step "build-$name" dotnet "${args[@]}"
  done
}
# Protected files are hashed before/after fix; the formatting pipeline must
# never silently edit tests, generated source, migrations or other tooling.
snapshot_protected() {
  local -a paths=()
  local file
  for file in "$ROOT/tests" "$ROOT/tools" "$ROOT/scripts" "$ROOT/config" "$ROOT/docs" "$ROOT/.github"; do
    [[ -d "$file" ]] && paths+=("$file")
  done
  for file in "${EXCLUSIONS[@]}" "${GENERATED[@]}"; do paths+=("$file"); done
  # Only files outside eligible C# are protected (including .csproj/XML).
  while IFS= read -r -d '' file; do
    case "$file" in *.cs) ;; *) paths+=("$file");; esac
  done < <(find "$ROOT/src" -type f ! -path '*/bin/*' ! -path '*/obj/*' -print0)
  if ((${#paths[@]})); then
    find "${paths[@]}" -type f ! -path '*/bin/*' ! -path '*/obj/*' -print0 | sort -z | xargs -0 -r sha256sum
  fi
}
if ! ensure_csharpier; then
  echo 'ERROR: CSharpier installation failed (run setup with NuGet access).' >&2
  TOTAL_FAILURES=$((TOTAL_FAILURES+1)); finalize; exit 1
fi

case "$MODE" in
  fix|bootstrap)
    before="$REPORT/protected-before.sha256"; after="$REPORT/protected-after.sha256"
    snapshot_protected > "$before"
    # Explicit restores before style fixes; no tests are run.
    for project in "${PROJECTS[@]}"; do
      run_step "restore-$(basename "${project%.csproj}")" dotnet restore "$project"
    done
    if (( TOTAL_FAILURES == 0 )); then style_pass fix; fi
    if (( TOTAL_FAILURES == 0 )); then csharpier_batches format; fi
    snapshot_protected > "$after"
    if ! cmp -s "$before" "$after"; then
      echo 'ERROR: Protected source/tool files changed during fix! Review source before committing.' >&2
      TOTAL_FAILURES=$((TOTAL_FAILURES+1))
    fi
    ;;
  check)
    # Preserve format findings while also inspecting style and compilation.
    csharpier_batches check
    restore_failed=0
    for project in "${PROJECTS[@]}"; do
      run_step "restore-$(basename "${project%.csproj}")" dotnet restore "$project"
      (( LAST_RC == 0 )) || restore_failed=1
    done
    if (( restore_failed == 0 )); then style_pass verify; compile_production no; fi
    ;;
  analyze)
    for project in "${PROJECTS[@]}"; do
      run_step "restore-$(basename "${project%.csproj}")" dotnet restore "$project"
    done
    if (( TOTAL_FAILURES == 0 )); then compile_production yes; fi
    ;;
  *) echo 'internal dispatch error' >&2; exit 2;;
esac
finalize
if [[ "$MODE" == bootstrap ]]; then
  exec bash "$ROOT/scripts/code-quality.sh" check "$RUN-check"
fi
