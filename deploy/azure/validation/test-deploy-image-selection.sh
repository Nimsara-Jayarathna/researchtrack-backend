#!/usr/bin/env bash
# Regression tests for image selection in deploy-container-apps.sh.
#
# Runs the real script with PLAN_ONLY=true against stub `az` (Container Apps
# state) and `docker` (GHCR). Env files hold synthetic values only. Proves that
# every service targets the digest of the image for its current source, that an
# app already running an older image is redeployed even when its service was
# not rebuilt in this run, and that missing or mislabelled images fail.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
deployer="$repo_root/deploy/azure/scripts/deploy-container-apps.sh"
renderer="$repo_root/deploy/azure/scripts/render-containerapp.py"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

services=(gateway auth project github jira meeting submission)
prefix=ghcr.io/example
env_id=/subscriptions/0/resourceGroups/rg/providers/Microsoft.App/managedEnvironments/cae
location=westeurope

mkdir -p "$work/bin" "$work/env" "$work/apps" "$work/images"
printf 'Jwt__Issuer=https://issuer.example\nJwt__Audience=researchtrack\nJwt__SigningKey=%s\n' \
  "$(printf 'k%.0s' {1..48})" > "$work/env/shared-auth.env"
for service in "${services[@]}"; do
  printf 'Service__Name=%s\n' "$service" > "$work/env/$service.env"
done

cat > "$work/bin/az" <<'STUB'
#!/usr/bin/env bash
# containerapp env show --query id|location ; containerapp show -n <app>
args="$*"
case "$args" in
  "containerapp env show"*"--query id"*) echo "$STUB_ENV_ID" ;;
  "containerapp env show"*"--query location"*) echo "$STUB_LOCATION" ;;
  "containerapp show"*)
    while [[ $# -gt 0 && "$1" != "-n" ]]; do shift; done
    [[ -f "$STUB_DIR/apps/$2.json" ]] || { echo "ResourceNotFound" >&2; exit 3; }
    cat "$STUB_DIR/apps/$2.json"
    ;;
  *) echo "unexpected az call: $args" >&2; exit 99 ;;
esac
STUB

cat > "$work/bin/docker" <<'STUB'
#!/usr/bin/env bash
# buildx imagetools inspect <ref> --format '{{json .Manifest}}'|'{{json .Image}}'
ref="$4" format="$6"
file="$STUB_DIR/images/${ref//[\/:]/_}"
[[ -f "$file" ]] || { echo "ERROR: $ref: not found" >&2; exit 1; }
case "$format" in
  *Manifest*) jq -c '{digest: .digest}' "$file" ;;
  *Image*) jq -c '.image' "$file" ;;
esac
STUB
chmod +x "$work/bin/az" "$work/bin/docker"

tag_for() { echo "src-$(printf '%s' "$1" | shasum | cut -c1-40)"; }
digest_for() { echo "sha256:$(printf 'new-%s' "$1" | shasum -a 256 | cut -c1-64)"; }

# Publish the image for each service's current source. $1 = image JSON shape:
# "single" (plain manifest) or "index" (index with provenance attestation).
publish() {
  local shape="$1" service tag labels
  for service in "${services[@]}"; do
    tag="$(tag_for "$service")"
    labels="$(jq -nc --arg s "$service" --arg t "$tag" '{
      "org.opencontainers.image.revision": "feedfacefeedfacefeedfacefeedfacefeedface",
      "org.opencontainers.image.source": "https://github.com/example/researchtrack-backend",
      "io.researchtrack.service": $s, "io.researchtrack.source-tag": $t}')"
    if [[ "$shape" == single ]]; then
      image="$(jq -nc --argjson l "$labels" '{config: {Labels: $l}}')"
    else
      image="$(jq -nc --argjson l "$labels" '{"linux/amd64": {config: {Labels: $l}}, "unknown/unknown": {config: {}}}')"
    fi
    jq -nc --arg d "$(digest_for "$service")" --argjson i "$image" '{digest: $d, image: $i}' \
      > "$work/images/${prefix//\//_}_researchtrack-${service}_${tag}"
  done
}

image_tags() {
  local service json='{}'
  for service in "${services[@]}"; do
    json="$(jq -c --arg s "$service" --arg t "$(tag_for "$service")" '. + {($s): $t}' <<<"$json")"
  done
  echo "$json"
}

# Container App state as `az containerapp show` returns it. $2 = image;
# $3 = spec hash (RESEARCHTRACK_DEPLOYMENT_REVISION).
set_app() {
  jq -n --arg image "$2" --arg hash "$3" --arg rev "rt-$1-prod--old" '{properties: {
    latestReadyRevisionName: $rev, latestRevisionName: $rev,
    template: {containers: [{image: $image, env: [{name: "RESEARCHTRACK_DEPLOYMENT_REVISION", value: $hash}]}]}}}' \
    > "$work/apps/rt-$1-prod.json"
}

spec_hash() {
  local service="$1" image="$2" cpu memory args=()
  case "$service" in gateway|auth|project) cpu=0.5 memory=1Gi ;; *) cpu=0.25 memory=0.5Gi ;; esac
  # Same composition as the deployer: the central service metadata.
  for file in $(python3 "$repo_root/deploy/build/service-impact.py" meta --service "$service" | sed -n 's/^env_files=//p'); do
    args+=(--env-file "$work/env/$file")
  done
  python3 "$renderer" --kind app --service "$service" --name "rt-$service-prod" --location "$location" \
    --environment-id "$env_id" --image "$image" --cpu "$cpu" --memory "$memory" \
    --min-replicas 1 --max-replicas 1 --registry-server ghcr.io --registry-username "" \
    --output "$work/spec.json" "${args[@]}"
}

run_deployer() {
  : > "$work/summary.md"
  PATH="$work/bin:$PATH" STUB_DIR="$work" STUB_ENV_ID="$env_id" STUB_LOCATION="$location" \
    RESOURCE_GROUP=rg ACA_ENVIRONMENT=cae IMAGE_PREFIX="$prefix" GIT_SHA=feedface \
    ENV_DIR="$work/env" WORK_DIR="$work/specs" GITHUB_STEP_SUMMARY="$work/summary.md" \
    PLAN_ONLY=true "$@" "$deployer" > "$work/out" 2>&1
}

fail() { echo "FAIL: $*" >&2; echo "--- output:" >&2; cat "$work/out" >&2; exit 1; }
pass() { echo "ok - $*"; }

reset_state() { rm -f "$work/apps/"* "$work/images/"*; }

# 1. The incident: apps run old digests, only gateway was rebuilt in this run.
#    Every service must still move to the image for its current source.
reset_state
publish index
for service in "${services[@]}"; do
  set_app "$service" "$prefix/researchtrack-$service@sha256:$(printf '%064d' 0)" oldhash
done
run_deployer env IMAGE_TAGS="$(image_tags)" REBUILT_SERVICES=gateway || fail "incident plan exited non-zero"
for service in "${services[@]}"; do
  grep -Eq "^   $service +deploy +$prefix/researchtrack-$service@$(digest_for "$service") - running image is not the current source" "$work/out" \
    || fail "$service not redeployed to its current-source digest"
done
grep -q 'jira .*reused image built from feedfacefeed' "$work/out" || fail "jira reason does not name the source commit"
grep -q 'gateway .*built in this run' "$work/out" || fail "gateway reason does not say it was built"
grep -q 'PLAN_ONLY=true: stopping before migrations' "$work/out" || fail "plan-only did not stop"
pass "apps on stale images are redeployed even when not rebuilt in this run"

# 2. Apps already on the current-source digest with an identical spec are left alone.
reset_state
publish single
for service in "${services[@]}"; do
  image="$prefix/researchtrack-$service@$(digest_for "$service")"
  set_app "$service" "$image" "$(spec_hash "$service" "$image")"
done
run_deployer env IMAGE_TAGS="$(image_tags)" || fail "unchanged plan exited non-zero"
grep -q 'Nothing to deploy' "$work/out" || fail "expected nothing to deploy"
for service in "${services[@]}"; do
  grep -q "^| $service | unchanged | running image already matches current source | feedfacefeed |" "$work/summary.md" \
    || fail "summary row for unchanged $service missing"
done
pass "current apps are unchanged and every service appears in the summary"

# 3. force_redeploy rolls out unchanged apps with an explicit reason.
run_deployer env IMAGE_TAGS="$(image_tags)" FORCE_REDEPLOY=true || fail "force plan exited non-zero"
[[ "$(grep -c 'deploy .* - force_redeploy=true' "$work/out")" == 7 ]] || fail "force_redeploy did not plan all seven"
pass "force_redeploy plans all seven"

# 4. First deployment of an app.
rm -f "$work/apps/rt-jira-prod.json"
run_deployer env IMAGE_TAGS="$(image_tags)" || fail "first-deploy plan exited non-zero"
grep -q '^   jira .*first deployment' "$work/out" || fail "jira first deployment not planned"
pass "first deployment uses the current-source image"

# 5. The image for a service's current source is not published: fail, never fall back.
rm -f "$work/images/"*researchtrack-jira_*
if run_deployer env IMAGE_TAGS="$(image_tags)"; then fail "missing image did not fail"; fi
grep -q 'for the current jira source is not published' "$work/out" || fail "missing image message"
pass "missing current-source image fails instead of reusing a running image"

# 6. Provenance labels must match the service and source tag.
reset_state
publish single
file="$(ls "$work/images/"*researchtrack-github_*)"
jq '.image.config.Labels["io.researchtrack.source-tag"] = "src-0000000000000000000000000000000000000000"' "$file" > "$file.tmp" && mv "$file.tmp" "$file"
if run_deployer env IMAGE_TAGS="$(image_tags)"; then fail "mislabelled image did not fail"; fi
grep -q "Image .*researchtrack-github.* is labelled" "$work/out" || fail "label mismatch message"
pass "image whose provenance labels do not match is rejected"

# 7. Missing or incomplete IMAGE_TAGS is refused.
reset_state
publish single
if run_deployer env IMAGE_TAGS=""; then fail "empty IMAGE_TAGS accepted"; fi
if run_deployer env IMAGE_TAGS='{"gateway":"src-x"}'; then fail "partial IMAGE_TAGS accepted"; fi
grep -q 'refusing to deploy an image of unknown provenance' "$work/out" || fail "partial IMAGE_TAGS message"
pass "missing image tags are refused"

# 8. Plan logs each service's composed env files and required shared keys (names only).
reset_state
publish single
run_deployer env IMAGE_TAGS="$(image_tags)" || fail "composition plan exited non-zero"
grep -q '^   jira        env shared-auth.env jira.env (requires Jwt__Issuer,Jwt__Audience,Jwt__SigningKey)$' "$work/out" \
  || fail "jira composition not logged"
grep -q '^   gateway     env gateway.env$' "$work/out" || fail "gateway composition not logged"
grep -q "$(printf 'k%.0s' {1..48})" "$work/out" && fail "signing key value printed"
pass "plan logs composed env files and required shared-auth keys"

# 9. Shared key missing: planning fails before any migration or revision.
cp "$work/env/shared-auth.env" "$work/shared-auth.env.bak"
sed -i.tmp '/^Jwt__SigningKey=/d' "$work/env/shared-auth.env" && rm -f "$work/env/shared-auth.env.tmp"
if run_deployer env IMAGE_TAGS="$(image_tags)"; then fail "missing shared key accepted"; fi
grep -q 'rt-auth-prod: required configuration missing or empty in composed env files (shared-auth.env, auth.env): Jwt__SigningKey' "$work/out" \
  || fail "missing shared key message"
grep -q 'PLAN_ONLY=true: stopping' "$work/out" && fail "planning continued after missing shared key"
cp "$work/shared-auth.env.bak" "$work/env/shared-auth.env"
pass "missing shared-auth key fails during planning, before any Azure change"

# 10. Rollback is idempotent: an already-active previous revision is success.
rollback_fns="$(sed -n '/^revision_active() {/,/^}/p; /^set_revision_active() {/,/^}/p; /^rollback() {/,/^}/p' "$deployer")"
cat > "$work/bin/az" <<'STUB'
#!/usr/bin/env bash
# revision show --query properties.active | revision activate|deactivate
echo "$*" >> "$STUB_DIR/az.log"
revision="" ; prev=""
for arg in "$@"; do [[ "$prev" == --revision ]] && revision="$arg"; prev="$arg"; done
state_file="$STUB_DIR/revisions/$revision"
case "$2 $3" in
  "revision show") cat "$state_file" ;;
  "revision activate")
    [[ "$(cat "$state_file")" == true ]] && { echo "ERROR: (RevisionAlreadyInRequestedState) Revision $revision is already active!" >&2; exit 1; }
    echo true > "$state_file" ;;
  "revision deactivate")
    [[ "$(cat "$state_file")" == false ]] && { echo "ERROR: (RevisionAlreadyInRequestedState) Revision $revision is already inactive!" >&2; exit 1; }
    echo false > "$state_file" ;;
  *) echo "unexpected az call: $*" >&2; exit 99 ;;
esac
STUB
chmod +x "$work/bin/az"
mkdir -p "$work/revisions"
run_rollback() {
  : > "$work/az.log"
  PATH="$work/bin:$PATH" STUB_DIR="$work" RESOURCE_GROUP=rg bash -c '
    set -Eeuo pipefail
    summary=/dev/null
    declare -A previous_revision=([jira]=rt-jira-prod--prev) target_image=([jira]=img) deploy_reason=([jira]=r) source_commit=([jira]=c)
    summary_row() { :; }
    '"$rollback_fns"'
    rollback jira rt-jira-prod rt-jira-prod--new' > "$work/out" 2>&1
}
echo true > "$work/revisions/rt-jira-prod--prev"; echo true > "$work/revisions/rt-jira-prod--new"
run_rollback || fail "rollback with previous already active failed"
grep -q 'revision activate' "$work/az.log" && fail "activate called for an already-active revision"
grep -q 'RevisionAlreadyInRequestedState' "$work/out" && fail "rollback surfaced RevisionAlreadyInRequestedState"
grep -q 'rt-jira-prod--prev already activated' "$work/out" || fail "already-active not reported"
grep -q 'kept on previous healthy revision rt-jira-prod--prev' "$work/out" || fail "rollback outcome not reported"
[[ "$(cat "$work/revisions/rt-jira-prod--new")" == false ]] || fail "failed revision not deactivated"
pass "rollback treats an already-active previous revision as success"

echo false > "$work/revisions/rt-jira-prod--prev"; echo false > "$work/revisions/rt-jira-prod--new"
run_rollback || fail "rollback with inactive previous failed"
[[ "$(cat "$work/revisions/rt-jira-prod--prev")" == true ]] || fail "previous revision not re-activated"
grep -q 'revision deactivate' "$work/az.log" && fail "deactivate called for an already-inactive revision"
pass "rollback re-activates an inactive previous revision"

echo "All deploy image-selection tests passed."
