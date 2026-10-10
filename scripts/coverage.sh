#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

CONFIGURATION="${CONFIGURATION:-Release}"
RAW_DIR="artifacts/test-results/unit/raw"
RAW_REPORT_DIR="artifacts/coverage/all-source"
BUSINESS_REPORT_DIR="artifacts/coverage/business"
SUMMARY_FILE="artifacts/coverage/coverage-summary.md"
METRICS_FILE="artifacts/coverage/coverage-metrics.env"
NO_BUILD=false

if [[ "${1:-}" == "--no-build" ]]; then
  NO_BUILD=true
fi

rm -rf artifacts/test-results/unit artifacts/coverage
mkdir -p "$RAW_DIR" "$RAW_REPORT_DIR" "$BUSINESS_REPORT_DIR"

dotnet tool restore

if [[ "$NO_BUILD" != true ]]; then
  dotnet build ResearchTrack.sln -c "$CONFIGURATION" --no-restore
fi

dotnet test ResearchTrack.sln \
  -c "$CONFIGURATION" \
  --no-build \
  --filter "Category!=DatabaseIntegration" \
  --collect:"XPlat Code Coverage" \
  --results-directory "$RAW_DIR"

mapfile -t COVERAGE_FILES < <(find "$RAW_DIR" -type f -name 'coverage.cobertura.xml' -print | sort)
if [[ ${#COVERAGE_FILES[@]} -eq 0 ]]; then
  echo "No Cobertura coverage files were produced." >&2
  exit 1
fi

REPORTS="$(IFS=';'; echo "${COVERAGE_FILES[*]}")"
COMMON_FILTERS=(
  '-assemblyfilters:+ResearchTrack.*;-*.Tests;-ResearchTrack.Testing;-ResearchTrack.DevSeeder;-ResearchTrack.DbCheck'
  '-filefilters:-*Migrations*;-*Program.cs;-*obj*;-*Generated*'
)

dotnet tool run reportgenerator -- \
  "-reports:${REPORTS}" \
  "-targetdir:${RAW_REPORT_DIR}" \
  '-reporttypes:Html;Cobertura;MarkdownSummaryGithub;JsonSummary;TextSummary;CsvSummary' \
  "${COMMON_FILTERS[@]}"

# Business report intentionally measures code that unit tests are expected to own:
# domain/business Features plus the small Gateway policy layer. Controllers, DTOs,
# persistence plumbing and external HTTP/storage adapters are excluded from this gate.
dotnet tool run reportgenerator -- \
  "-reports:${REPORTS}" \
  "-targetdir:${BUSINESS_REPORT_DIR}" \
  '-reporttypes:Html;Cobertura;MarkdownSummaryGithub;JsonSummary;TextSummary;CsvSummary' \
  "${COMMON_FILTERS[@]}" \
  '-classfilters:+ResearchTrack.*.Features.*;+ResearchTrack.Gateway.*;-*.Controllers.*;-*.Contracts.*;-*.Persistence.*;-*.Infrastructure.*;-*.Extensions.*'

python - "$RAW_REPORT_DIR/Cobertura.xml" "$BUSINESS_REPORT_DIR/Cobertura.xml" "$SUMMARY_FILE" "$METRICS_FILE" <<'PY'
from __future__ import annotations
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

raw_path, business_path, summary_path, metrics_path = map(Path, sys.argv[1:])

def metrics(path: Path):
    root = ET.parse(path).getroot()
    line_rate = float(root.attrib.get("line-rate", "0")) * 100
    branch_rate = float(root.attrib.get("branch-rate", "0")) * 100
    lines_valid = int(float(root.attrib.get("lines-valid", "0")))
    lines_covered = int(float(root.attrib.get("lines-covered", "0")))
    branches_valid = int(float(root.attrib.get("branches-valid", "0")))
    branches_covered = int(float(root.attrib.get("branches-covered", "0")))
    return line_rate, branch_rate, lines_covered, lines_valid, branches_covered, branches_valid

raw = metrics(raw_path)
business = metrics(business_path)

summary = f"""# ResearchTrack Unit + Mocking Coverage\n\n| Scope | Line coverage | Branch coverage | Lines | Branches |\n|---|---:|---:|---:|---:|\n| All production source (filtered generated/bootstrap code) | **{raw[0]:.1f}%** | **{raw[1]:.1f}%** | {raw[2]}/{raw[3]} | {raw[4]}/{raw[5]} |\n| Business logic quality-gate scope | **{business[0]:.1f}%** | **{business[1]:.1f}%** | {business[2]}/{business[3]} | {business[4]}/{business[5]} |\n\nThe business scope is intentionally limited to `Features` classes and the Gateway policy layer. Controllers, DTO-only contracts, EF persistence plumbing, migrations, generated/bootstrap code, and external infrastructure adapters are not used to inflate or depress the unit-test quality gate.\n\n## Evidence\n\n- Unit/mock tests run with `Category!=DatabaseIntegration`.\n- Raw Cobertura files: `artifacts/test-results/unit/raw/`.\n- Full-source HTML: `artifacts/coverage/all-source/index.html`.\n- Business HTML: `artifacts/coverage/business/index.html`.\n- Database integration tests remain a separate suite.\n"""
summary_path.write_text(summary, encoding="utf-8")
metrics_path.write_text(
    f"RAW_LINE={raw[0]:.2f}\nRAW_BRANCH={raw[1]:.2f}\n"
    f"BUSINESS_LINE={business[0]:.2f}\nBUSINESS_BRANCH={business[1]:.2f}\n",
    encoding="utf-8")

print(summary)

min_line = None
min_branch = None
import os
if os.getenv("COVERAGE_MIN_BUSINESS_LINE"):
    min_line = float(os.environ["COVERAGE_MIN_BUSINESS_LINE"])
if os.getenv("COVERAGE_MIN_BUSINESS_BRANCH"):
    min_branch = float(os.environ["COVERAGE_MIN_BUSINESS_BRANCH"])

failures = []
if min_line is not None and business[0] + 1e-9 < min_line:
    failures.append(f"business line coverage {business[0]:.2f}% < {min_line:.2f}%")
if min_branch is not None and business[1] + 1e-9 < min_branch:
    failures.append(f"business branch coverage {business[1]:.2f}% < {min_branch:.2f}%")
if failures:
    raise SystemExit("Coverage quality gate failed: " + "; ".join(failures))
PY

echo "Coverage evidence written to artifacts/coverage/."
