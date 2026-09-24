#!/usr/bin/env bash
# Runs Azure What-If for the Production infrastructure and fails on any resource
# deletion unless ALLOW_DESTRUCTIVE=true (a deliberate workflow_dispatch input).
# Modifications are listed in the job summary for review.
#
# Required environment: AZURE_LOCATION, AZURE_VM_ADMIN_SSH_PUBLIC_KEY
set -euo pipefail

azure_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
summary="${GITHUB_STEP_SUMMARY:-/dev/null}"
out="$(mktemp)"
trap 'rm -f "$out"' EXIT

az deployment sub what-if \
  --location "${AZURE_LOCATION:?AZURE_LOCATION is required}" \
  --name researchtrack-prod-infrastructure \
  --parameters "$azure_dir/parameters/production.bicepparam" \
  --no-pretty-print \
  --output json > "$out"

jq -r '.changes[] | "\(.changeType)\t\(.resourceId)"' "$out" | sort | sed 's/^/  /'

{
  echo "### Azure What-If"
  echo
  echo "| Change | Count |"
  echo "|---|---|"
  jq -r '.changes | group_by(.changeType)[] | "| \(.[0].changeType) | \(length) |"' "$out"
  echo
  jq -r '.changes[] | select(.changeType == "Modify" or .changeType == "Delete")
    | "- **\(.changeType)** `\(.resourceId | split("/providers/")[-1])`"' "$out"
} >> "$summary"

deletes="$(jq '[.changes[] | select(.changeType == "Delete")] | length' "$out")"
if (( deletes > 0 )) && [[ "${ALLOW_DESTRUCTIVE:-false}" != "true" ]]; then
  echo "What-If reports $deletes resource deletion(s). Review them and re-run with allow_destructive=true if intended." >&2
  exit 1
fi
echo "What-If gate passed ($deletes deletion(s), allow_destructive=${ALLOW_DESTRUCTIVE:-false})."
