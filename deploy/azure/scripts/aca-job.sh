# shellcheck shell=bash
# Container Apps Job helpers, sourced by network-probe.sh and
# deploy-container-apps.sh. Requires an authenticated `az` session.
#
# Jobs are applied with ONE mechanism: an ARM PUT of the complete job resource
# (create-or-replace, idempotent). `az containerapp job create --yaml` is not
# used: the CLI registers defaults for --parallelism/--replica-completion-count
# on create and warns that "additional flags" are ignored on every call, and
# `job update --yaml` is a partial update rather than a declarative replace.

ACA_JOB_API_VERSION="${ACA_JOB_API_VERSION:-2024-03-01}"

aca_job_url() {
  local resource_group="$1" name="$2" subscription
  subscription="$(az account show --query id -o tsv)"
  printf 'https://management.azure.com/subscriptions/%s/resourceGroups/%s/providers/Microsoft.App/jobs/%s?api-version=%s' \
    "$subscription" "$resource_group" "$name" "$ACA_JOB_API_VERSION"
}

# aca_job_put <resource-group> <job-name> <body.json>
# Creates or fully replaces the job from an ARM body ({location, properties}),
# then waits until the resource is provisioned.
aca_job_put() {
  local resource_group="$1" name="$2" body="$3" url state=""
  url="$(aca_job_url "$resource_group" "$name")"
  az rest --method put --url "$url" --body @"$body" --headers "Content-Type=application/json" -o none || return 1
  for _ in $(seq 1 60); do
    state="$(az rest --method get --url "$url" --query properties.provisioningState -o tsv 2>/dev/null || true)"
    case "$state" in
      Succeeded) return 0 ;;
      Failed|Canceled) echo "Job $name provisioning state: $state" >&2; return 1 ;;
    esac
    sleep 5
  done
  echo "Timed out waiting for job $name to provision (last state: ${state:-unknown})." >&2
  return 1
}

# aca_job_start <resource-group> <job-name>  -> prints the execution name
aca_job_start() {
  az containerapp job start -g "$1" -n "$2" --query name -o tsv
}

# aca_job_wait <resource-group> <job-name> <execution> <max-polls> -> prints the final status
aca_job_wait() {
  local resource_group="$1" name="$2" execution="$3" polls="${4:-60}" status=""
  for _ in $(seq 1 "$polls"); do
    status="$(az containerapp job execution show -g "$resource_group" -n "$name" \
      --job-execution-name "$execution" --query properties.status -o tsv 2>/dev/null || true)"
    case "$status" in
      Succeeded|Failed|Stopped|Degraded) printf '%s' "$status"; return 0 ;;
    esac
    sleep 10
  done
  printf '%s' "${status:-Timeout}"
}

# aca_job_logs <resource-group> <job-name> <execution> <container>
# Console output of one execution: live replica logs first, then Log Analytics
# (ingestion can lag a few minutes after a short execution ends).
aca_job_logs() {
  local resource_group="$1" name="$2" execution="$3" container="$4" logs workspace query
  logs="$(az containerapp job logs show -g "$resource_group" -n "$name" --execution "$execution" \
    --container "$container" --follow false --tail 300 2>/dev/null || true)"
  if [[ -n "${logs//[[:space:]]/}" ]]; then
    printf '%s\n' "$logs"
    return 0
  fi
  workspace="$(az containerapp env show --ids "$(az containerapp job show -g "$resource_group" -n "$name" \
    --query properties.environmentId -o tsv 2>/dev/null)" \
    --query properties.appLogsConfiguration.logAnalyticsConfiguration.customerId -o tsv 2>/dev/null || true)"
  if [[ -z "$workspace" ]]; then
    echo "(no live logs and no Log Analytics workspace configured)"
    return 0
  fi
  query="ContainerAppConsoleLogs_CL | where ContainerJobName_s == '$name' and ContainerGroupName_s startswith '$execution' | order by TimeGenerated asc | project Log_s"
  for _ in $(seq 1 18); do
    logs="$(az rest --method post --resource https://api.loganalytics.io \
      --url "https://api.loganalytics.io/v1/workspaces/$workspace/query" \
      --body "$(jq -n --arg q "$query" '{query: $q}')" \
      --query 'tables[0].rows[].[0]' -o tsv 2>/dev/null || true)"
    if [[ -n "${logs//[[:space:]]/}" ]]; then
      printf '%s\n' "$logs"
      return 0
    fi
    sleep 10
  done
  echo "(logs not yet available in Log Analytics workspace $workspace)"
}
