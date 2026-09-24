#!/usr/bin/env bash
# Deploys ResearchTrack services to Azure Container Apps.
#
# Each app is applied through deploy/azure/modules/container-app.bicep (Bicep
# owns the app definition). Every service targets the image published for its
# current source fingerprint (IMAGE_TAGS, from deploy/build/service-impact.py
# plan), pinned by digest and checked against its provenance labels; there is
# no fallback to the image an app already runs. Only services whose desired
# spec changed (image, env, secrets, resources) are touched, so a Jira-only
# change updates rt-jira-prod and nothing else. For each such DB-owning service,
# /app/dbcheck and /app/migrate run first as a Container Apps Job with the same
# image; a failure stops before any revision changes. A new revision that does
# not become healthy is deactivated and the previous healthy revision kept.
#
# Required environment:
#   RESOURCE_GROUP, ACA_ENVIRONMENT, IMAGE_PREFIX, GIT_SHA, ENV_DIR, WORK_DIR
#   IMAGE_TAGS                 JSON map service -> source-fingerprint image tag
# Optional:
#   REBUILT_SERVICES           space-separated services built in this run
#   REGISTRY_USERNAME/REGISTRY_PASSWORD  GHCR pull credential (private packages)
#   FORCE_REDEPLOY=true        migrate and roll out every service regardless of changes
#   PLAN_ONLY=true             print the plan and summary, change nothing
set -Eeuo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=aca-job.sh
. "$script_dir/aca-job.sh"
app_template="$script_dir/../modules/container-app.bicep"

for key in RESOURCE_GROUP ACA_ENVIRONMENT IMAGE_PREFIX GIT_SHA ENV_DIR WORK_DIR IMAGE_TAGS; do
  [[ -n "${!key:-}" ]] || { echo "$key is required." >&2; exit 1; }
done
jq -e 'type == "object"' <<<"$IMAGE_TAGS" >/dev/null 2>&1 \
  || { echo "IMAGE_TAGS must be the JSON service -> image tag map from the build job." >&2; exit 1; }

REBUILT_SERVICES="${REBUILT_SERVICES:-}"
# One replica per service until statelessness/background workers are reviewed
# (docs/devops/azure-production/DECISIONS.md).
MIN_REPLICAS=1
MAX_REPLICAS=1
FORCE_REDEPLOY="${FORCE_REDEPLOY:-false}"
PLAN_ONLY="${PLAN_ONLY:-false}"
summary="${GITHUB_STEP_SUMMARY:-/dev/null}"

# Backends first so the Gateway never routes to a service that is not deployed.
services=(auth project github jira meeting submission gateway)
db_services=" auth project github jira meeting submission "

mkdir -p "$WORK_DIR"
chmod 700 "$WORK_DIR"
# Rendered specs contain secret values; never leave them behind.
trap 'rm -rf "$WORK_DIR"' EXIT

env_id="$(az containerapp env show -g "$RESOURCE_GROUP" -n "$ACA_ENVIRONMENT" --query id -o tsv)"
location="$(az containerapp env show -g "$RESOURCE_GROUP" -n "$ACA_ENVIRONMENT" --query location -o tsv)"

service_env_files() {
  case "$1" in
    gateway) echo gateway.env ;;
    auth|project|github) echo "shared-auth.env $1.env" ;;
    jira|meeting|submission) echo "$1.env" ;;
  esac
}

# Mirrors the Compose resource ceilings, rounded to valid Consumption sizes.
service_resources() {
  case "$1" in
    gateway|auth|project) echo "0.5 1Gi" ;;
    *) echo "0.25 0.5Gi" ;;
  esac
}

resolve_image_digest() {
  local ref="$1" digest
  digest="$(docker buildx imagetools inspect "$ref" --format '{{json .Manifest}}' | jq -r .digest)"
  [[ "$digest" == sha256:* ]] || { echo "Cannot resolve digest for $ref" >&2; return 1; }
  echo "${ref%:*}@$digest"
}

# OCI labels of an image: a single-platform manifest, or an index (buildx adds
# a provenance attestation) where linux/amd64 is preferred over other platforms.
image_labels() {
  docker buildx imagetools inspect "$1" --format '{{json .Image}}' | jq -c '
    (if has("config") then .
     else (.["linux/amd64"] // ([to_entries[] | select(.key | startswith("unknown/") | not) | .value][0]) // {})
     end) | .config.Labels // {}'
}

render() {
  local kind="$1" service="$2" name="$3" image="$4" output="$5"
  local args=() file cpu memory
  read -r cpu memory < <(service_resources "$service")
  for file in $(service_env_files "$service"); do
    args+=(--env-file "$ENV_DIR/$file")
  done
  REGISTRY_PASSWORD="${REGISTRY_PASSWORD:-}" python3 "$script_dir/render-containerapp.py" \
    --kind "$kind" \
    --service "$service" \
    --name "$name" \
    --location "$location" \
    --environment-id "$env_id" \
    --image "$image" \
    --cpu "$cpu" --memory "$memory" \
    --min-replicas "$MIN_REPLICAS" --max-replicas "$MAX_REPLICAS" \
    --registry-server ghcr.io \
    --registry-username "${REGISTRY_USERNAME:-}" \
    --output "$output" \
    "${args[@]}"
}

# ---------------------------------------------------------------------------
# Plan
# ---------------------------------------------------------------------------
declare -A target_image=() previous_revision=() source_commit=() deploy_reason=()
planned=()

{
  echo "### Azure Container Apps deployment"
  echo
  echo "Source commit: \`$GIT_SHA\`"
  echo
  echo "| Service | Action | Reason | Image built from | Image | Previous revision | New revision |"
  echo "|---|---|---|---|---|---|---|"
} >> "$summary"

summary_row() {
  local service="$1" action="$2" reason="$3" new_revision="$4"
  echo "| $service | $action | $reason | ${source_commit[$service]:0:12} | ${target_image[$service]} | ${previous_revision[$service]:-none} | $new_revision |" >> "$summary"
}

echo "== Planning"
for service in "${services[@]}"; do
  app="rt-$service-prod"
  current="$(az containerapp show -g "$RESOURCE_GROUP" -n "$app" -o json 2>/dev/null || true)"

  if [[ -n "$current" ]]; then
    current_image="$(jq -r '.properties.template.containers[0].image // ""' <<<"$current")"
    current_hash="$(jq -r '[.properties.template.containers[0].env[]? | select(.name == "RESEARCHTRACK_DEPLOYMENT_REVISION") | .value][0] // ""' <<<"$current")"
    previous_revision[$service]="$(jq -r '.properties.latestReadyRevisionName // ""' <<<"$current")"
  else
    current_image=""
    current_hash=""
    previous_revision[$service]=""
  fi

  # Always the image for the current source, never "whatever is running now".
  tag="$(jq -r --arg s "$service" '.[$s] // ""' <<<"$IMAGE_TAGS")"
  [[ -n "$tag" ]] || { echo "No source image tag for $service; refusing to deploy an image of unknown provenance." >&2; exit 1; }
  ref="$IMAGE_PREFIX/researchtrack-$service:$tag"
  image="$(resolve_image_digest "$ref")" \
    || { echo "Image $ref for the current $service source is not published." >&2; exit 1; }
  labels="$(image_labels "$ref")"
  labelled_tag="$(jq -r '."io.researchtrack.source-tag" // ""' <<<"$labels")"
  labelled_service="$(jq -r '."io.researchtrack.service" // ""' <<<"$labels")"
  if [[ "$labelled_tag" != "$tag" || "$labelled_service" != "$service" ]]; then
    echo "Image $ref is labelled service='$labelled_service' source-tag='$labelled_tag'; expected '$service' '$tag'." >&2
    exit 1
  fi
  source_commit[$service]="$(jq -r '."org.opencontainers.image.revision" // "unknown"' <<<"$labels")"
  target_image[$service]="$image"

  if [[ " $REBUILT_SERVICES " == *" $service "* ]]; then
    origin="built in this run"
  else
    origin="reused image built from ${source_commit[$service]:0:12}"
  fi

  hash="$(render app "$service" "$app" "$image" "$WORK_DIR/$service.app.json")"
  if [[ "$hash" == "$current_hash" && "$FORCE_REDEPLOY" != "true" ]]; then
    printf '   %-11s unchanged  %s (source %s)\n' "$service" "$image" "${source_commit[$service]:0:12}"
    summary_row "$service" unchanged "running image already matches current source" -
    continue
  fi

  if [[ -z "$current" ]]; then
    deploy_reason[$service]="first deployment ($origin)"
  elif [[ "$FORCE_REDEPLOY" == "true" && "$hash" == "$current_hash" ]]; then
    deploy_reason[$service]="force_redeploy=true"
  elif [[ "$current_image" != "$image" ]]; then
    deploy_reason[$service]="running image is not the current source ($origin)"
  else
    deploy_reason[$service]="configuration changed (env, secrets or resources)"
  fi
  planned+=("$service")
  printf '   %-11s deploy     %s - %s\n' "$service" "$image" "${deploy_reason[$service]}"
done

if ((${#planned[@]} == 0)); then
  echo "Nothing to deploy: every Container App already matches the desired spec."
  exit 0
fi
if [[ "$PLAN_ONLY" == "true" ]]; then
  echo "PLAN_ONLY=true: stopping before migrations and rollout."
  exit 0
fi

# ---------------------------------------------------------------------------
# Migrations
# ---------------------------------------------------------------------------
run_migration() {
  local service="$1" job="rt-migrate-$1-prod" spec execution status
  spec="$WORK_DIR/$service.job.json"
  render job "$service" "$job" "${target_image[$service]}" "$spec" >/dev/null

  # One mechanism: full ARM PUT of the job (see aca-job.sh), never --yaml.
  aca_job_put "$RESOURCE_GROUP" "$job" "$spec" || { rm -f "$spec"; return 1; }
  rm -f "$spec"

  execution="$(aca_job_start "$RESOURCE_GROUP" "$job")"
  echo "   $service: $execution"
  status="$(aca_job_wait "$RESOURCE_GROUP" "$job" "$execution" 120)"
  if [[ "$status" == Succeeded ]]; then
    echo "   $service migrations applied"
    return 0
  fi
  echo "Migration job $job execution $execution finished with status $status." >&2
  aca_job_logs "$RESOURCE_GROUP" "$job" "$execution" "$service" | tail -n 60 >&2 || true
  return 1
}

echo "== Database checks and migrations"
for service in "${planned[@]}"; do
  [[ "$db_services" == *" $service "* ]] || continue
  run_migration "$service"
done

# ---------------------------------------------------------------------------
# Rollout
# ---------------------------------------------------------------------------
rollback() {
  local service="$1" app="$2" failed="$3" previous="${previous_revision[$1]}"
  echo "   rolling back $app" >&2
  if [[ -n "$failed" && "$failed" != "$previous" ]]; then
    az containerapp revision deactivate -g "$RESOURCE_GROUP" -n "$app" --revision "$failed" -o none || true
  fi
  if [[ -n "$previous" ]]; then
    az containerapp revision activate -g "$RESOURCE_GROUP" -n "$app" --revision "$previous" -o none || true
    echo "   $app kept on previous healthy revision $previous" >&2
  fi
  summary_row "$service" "**failed, rolled back**" "${deploy_reason[$service]}" "${failed:-none}"
}

wait_for_revision() {
  local service="$1" app="$2" image="$3" app_json="" revision="" revision_json=""
  local provisioning="" health="" running="" ready="" running_image=""
  for _ in $(seq 1 60); do
    app_json="$(az containerapp show -g "$RESOURCE_GROUP" -n "$app" -o json)"
    revision="$(jq -r '.properties.latestRevisionName // ""' <<<"$app_json")"
    ready="$(jq -r '.properties.latestReadyRevisionName // ""' <<<"$app_json")"
    [[ -n "$revision" ]] || { sleep 10; continue; }

    revision_json="$(az containerapp revision show -g "$RESOURCE_GROUP" -n "$app" --revision "$revision" -o json)"
    provisioning="$(jq -r '.properties.provisioningState // ""' <<<"$revision_json")"
    health="$(jq -r '.properties.healthState // ""' <<<"$revision_json")"
    running="$(jq -r '.properties.runningState // ""' <<<"$revision_json")"
    running_image="$(jq -r '.properties.template.containers[0].image // ""' <<<"$revision_json")"

    if [[ "$provisioning" == "Failed" || "$running" == "Failed" ]]; then
      echo "Revision $revision failed (provisioning=$provisioning running=$running)." >&2
      echo "$revision"
      return 1
    fi
    if [[ "$provisioning" == "Provisioned" && "$health" == "Healthy" && "$ready" == "$revision" ]]; then
      if [[ "$running_image" != "$image" ]]; then
        echo "Revision $revision runs $running_image, expected $image." >&2
        echo "$revision"
        return 1
      fi
      echo "$revision"
      return 0
    fi
    sleep 10
  done
  echo "Timed out waiting for $app revision $revision to become ready (health=$health running=$running)." >&2
  echo "$revision"
  return 1
}

echo "== Rolling out Container Apps"
for service in "${planned[@]}"; do
  app="rt-$service-prod"
  spec="$WORK_DIR/$service.app.json"

  # Create and update are the same declarative Bicep deployment. A failed
  # deployment still goes through revision checks and rollback below.
  if ! az deployment group create \
      -g "$RESOURCE_GROUP" \
      -n "$app-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}" \
      -f "$app_template" \
      -p @"$spec" \
      -o none; then
    echo "Bicep deployment of $app reported a failure." >&2
  fi
  rm -f "$spec"

  if new_revision="$(wait_for_revision "$service" "$app" "${target_image[$service]}")"; then
    echo "   $app ready on $new_revision"
    summary_row "$service" deployed "${deploy_reason[$service]}" "$new_revision"
  else
    az containerapp logs show -g "$RESOURCE_GROUP" -n "$app" --revision "$new_revision" --tail 100 2>/dev/null || true
    rollback "$service" "$app" "$new_revision"
    exit 1
  fi
done

echo "Container Apps deployment complete."
