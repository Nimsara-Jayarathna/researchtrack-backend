#!/usr/bin/env bash
# Validates Azure Production env files against the canonical config/env
# contracts and the Azure topology (Container Apps + infrastructure VM).
#
# deploy/validate-env-files.sh remains the validator for the Test VPS/Compose
# deployment and is intentionally not relaxed to accept Azure values.
#
# Usage: validate-azure-env-files.sh <env-dir> [config-contract-root]
# Environment:
#   EXPECTED_MYSQL_HOST      required; the infrastructure VM private IP/hostname
#   EXPECTED_GRAFANA_HOSTNAME optional; GF_SERVER_ROOT_URL must be https://<it>
#   VALIDATE_SCOPE           all (default): every service file, used by the
#                            application workflow; infra: mysql.env and
#                            grafana.env only, used by the infrastructure workflow
#   FORBIDDEN_HOSTS          optional; comma-separated Test hostnames that must
#                            not appear in any Production value
set -euo pipefail

ENV_DIR="${1:-}"
CONTRACT_ROOT="${2:-config/env}"
EXPECTED_MYSQL_HOST="${EXPECTED_MYSQL_HOST:-}"
FORBIDDEN_HOSTS="${FORBIDDEN_HOSTS:-}"
EXPECTED_GRAFANA_HOSTNAME="${EXPECTED_GRAFANA_HOSTNAME:-}"
VALIDATE_SCOPE="${VALIDATE_SCOPE:-all}"
case "$VALIDATE_SCOPE" in all|infra) ;; *) echo "VALIDATE_SCOPE must be all or infra." >&2; exit 1 ;; esac

[[ -n "$ENV_DIR" && -d "$ENV_DIR" ]] || { echo "Usage: $0 <env-dir> [config-contract-root]" >&2; exit 1; }
[[ -d "$CONTRACT_ROOT" ]] || { echo "Configuration contract root does not exist: $CONTRACT_ROOT" >&2; exit 1; }
[[ -n "$EXPECTED_MYSQL_HOST" ]] || { echo "EXPECTED_MYSQL_HOST is required." >&2; exit 1; }

# Errors are tallied in a file because many checks run inside $(...) subshells.
error_log="$(mktemp)"
trap 'rm -f "$error_log"' EXIT
error() {
  echo "ERROR: $*" >&2
  echo x >> "$error_log"
}
error_count() {
  wc -l < "$error_log" | tr -d ' '
}

declare -A contracts=(
  [mysql.env]=mysql
  [shared-auth.env]=shared
  [gateway.env]=gateway
  [auth.env]=auth
  [project.env]=project
  [github.env]=github
  [jira.env]=jira
  [meeting.env]=meeting
  [submission.env]=submission
  [grafana.env]=grafana
)
if [[ "$VALIDATE_SCOPE" == infra ]]; then
  for file in "${!contracts[@]}"; do
    [[ "$file" == mysql.env || "$file" == grafana.env ]] || unset "contracts[$file]"
  done
fi

get_value() {
  local file="$1" key="$2" line value
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ "$line" == "$key="* ]] || continue
    value="${line#*=}"
    if [[ ${#value} -ge 2 && ( "$value" =~ ^\".*\"$ || "$value" =~ ^\'.*\'$ ) ]]; then
      value="${value:1:${#value}-2}"
    fi
    printf '%s' "$value"
    return 0
  done < "$file"
  return 1
}

is_placeholder() {
  case "$1" in
    CHANGE_ME*|__SET_ME__|__GENERATE__|YOUR_*|"<"*) return 0 ;;
    *) return 1 ;;
  esac
}

require_value() {
  local file="$1" key="$2" value
  value="$(get_value "$ENV_DIR/$file" "$key" || true)"
  if [[ -z "$value" ]]; then
    error "$file: required key '$key' is missing or empty."
  fi
  printf '%s' "$value"
}

# ---------------------------------------------------------------------------
# Contract shape, format and placeholders
# ---------------------------------------------------------------------------
for file in "${!contracts[@]}"; do
  runtime="$ENV_DIR/$file"
  contract="$CONTRACT_ROOT/${contracts[$file]}/.env.example"
  [[ -f "$runtime" ]] || { error "missing deployment environment file: $file"; continue; }
  [[ -f "$contract" ]] || { error "missing canonical contract: $contract"; continue; }

  declare -A seen=()
  line_number=0
  while IFS= read -r line || [[ -n "$line" ]]; do
    line_number=$((line_number + 1))
    line="${line%$'\r'}"
    [[ -z "$line" || "$line" =~ ^[[:space:]]*# ]] && continue
    if [[ "$line" != *=* ]]; then
      error "$file line $line_number: expected KEY=value."
      continue
    fi
    key="${line%%=*}"
    [[ "$key" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]] || error "$file line $line_number: invalid key '$key'."
    [[ -z "${seen[$key]:-}" ]] || error "$file: duplicate key '$key'."
    seen[$key]=1

    value="$(get_value "$runtime" "$key" || true)"
    # Blob storage is not implemented yet; its S3-shaped keys are allowed to
    # stay unset until the Azure storage contract replaces them.
    if is_placeholder "$value" && [[ ! ( "$file" == submission.env && "$key" == Storage__* ) ]]; then
      error "$file: '$key' still contains a placeholder."
    fi
  done < "$runtime"

  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ -z "$line" || "$line" =~ ^[[:space:]]*# || "$line" != *=* ]] && continue
    key="${line%%=*}"
    [[ -n "${seen[$key]:-}" ]] || error "$file is missing '$key' from canonical contract $contract."
  done < "$contract"
  unset seen
done

# Later checks assume every file parses and matches its contract.
if (( $(error_count) > 0 )); then
  echo "Azure Production env validation failed with $(error_count) error(s)." >&2
  exit 1
fi

# Service checks (application workflow). The infrastructure workflow only
# needs mysql.env and grafana.env.
if [[ "$VALIDATE_SCOPE" == all ]]; then
  # ---------------------------------------------------------------------------
  # Runtime environment
  # ---------------------------------------------------------------------------
  for service in gateway auth project github jira meeting submission; do
    file="$service.env"
    [[ "$(require_value "$file" ASPNETCORE_ENVIRONMENT)" == "Production" ]] || error "$file: ASPNETCORE_ENVIRONMENT must be 'Production'."
    [[ "$(require_value "$file" DOTNET_ENVIRONMENT)" == "Production" ]] || error "$file: DOTNET_ENVIRONMENT must be 'Production'."
    [[ "$(require_value "$file" ASPNETCORE_URLS)" == "http://+:8080" ]] || error "$file: ASPNETCORE_URLS must be 'http://+:8080' (Container Apps target port)."
  done

  # ---------------------------------------------------------------------------
  # JWT
  # ---------------------------------------------------------------------------
  require_value shared-auth.env Jwt__Issuer >/dev/null
  require_value shared-auth.env Jwt__Audience >/dev/null
  jwt_key="$(require_value shared-auth.env Jwt__SigningKey)"
  (( ${#jwt_key} >= 32 )) || error "shared-auth.env: Jwt__SigningKey must be at least 32 characters."

  # ---------------------------------------------------------------------------
  # Shared authentication composition (the same metadata the deployment uses):
  # every service using shared JWT validation must receive every shared-auth
  # contract key, and only shared-auth.env may define them. Names only.
  # ---------------------------------------------------------------------------
  service_metadata="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../build" && pwd)/service-impact.py"
  mapfile -t shared_auth_keys < <(sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' "$CONTRACT_ROOT/shared/.env.example")
  ((${#shared_auth_keys[@]} > 0)) || error "no keys found in $CONTRACT_ROOT/shared/.env.example."
  for service in gateway auth project github jira meeting submission; do
    meta="$(python3 "$service_metadata" meta --service "$service")" || { error "no deployment metadata for $service."; continue; }
    uses_shared_auth="$(sed -n 's/^shared_auth=//p' <<<"$meta")"
    read -ra composed <<<"$(sed -n 's/^env_files=//p' <<<"$meta")"
    for key in "${shared_auth_keys[@]}"; do
      if get_value "$ENV_DIR/$service.env" "$key" >/dev/null; then
        error "$service.env defines shared-auth key '$key'; it is owned by shared-auth.env only."
      fi
    done
    [[ "$uses_shared_auth" == true ]] || continue
    report=()
    for key in "${shared_auth_keys[@]}"; do
      state=missing
      for file in "${composed[@]}"; do
        value="$(get_value "$ENV_DIR/$file" "$key" || true)"
        [[ -n "$value" ]] && { state=present; break; }
      done
      report+=("$key: $state")
      [[ "$state" == present ]] || error "$service uses shared JWT authentication but its composed environment (${composed[*]}) lacks '$key'."
    done
    joined="$(printf '%s, ' "${report[@]}")"
    echo "shared auth for $service (${composed[*]}): ${joined%, }"
  done

  # ---------------------------------------------------------------------------
  # Service discovery: Container Apps names, never Compose names or localhost
  # ---------------------------------------------------------------------------
  check_service_url() {
    local file="$1" key="$2" service="$3" value host
    value="$(require_value "$file" "$key")"
    [[ -n "$value" ]] || return 0
    if [[ ! "$value" =~ ^https?://([^/:]+)(:[0-9]+)?/?$ ]]; then
      error "$file: $key must be an http(s) base URL without a path (got '$value')."
      return 0
    fi
    host="${BASH_REMATCH[1]}"
    if [[ "$host" != "rt-$service-prod" && "$host" != rt-$service-prod.* ]]; then
      error "$file: $key must address the Container App rt-$service-prod (got host '$host')."
    fi
  }

  for service in auth project github jira meeting submission; do
    check_service_url gateway.env "${service^^}_SERVICE_URL" "$service"
  done
  check_service_url project.env Services__Auth__BaseUrl auth
  check_service_url github.env Services__Project__BaseUrl project

  # ---------------------------------------------------------------------------
  # Databases: private VM MySQL over TLS, credentials matching mysql.env
  # ---------------------------------------------------------------------------
  connection_value() {
    local connection_string="$1" wanted part key value
    wanted="$(tr -d ' _' <<<"${2,,}")"
    IFS=';' read -ra parts <<< "$connection_string"
    for part in "${parts[@]}"; do
      [[ "$part" == *=* ]] || continue
      key="$(tr -d ' _' <<<"${part%%=*}")"
      value="${part#*=}"
      value="${value#"${value%%[![:space:]]*}"}"
      value="${value%"${value##*[![:space:]]}"}"
      if [[ "${key,,}" == "$wanted" ]]; then
        printf '%s' "$value"
        return 0
      fi
    done
    return 1
  }

  first_connection_value() {
    local connection_string="$1" key
    shift
    for key in "$@"; do
      connection_value "$connection_string" "$key" && return 0
    done
    return 1
  }

  for service in auth project github jira meeting submission; do
    file="$service.env"
    prefix="${service^^}"
    db_name="$(require_value mysql.env "${prefix}_DB_NAME")"
    db_user="$(require_value mysql.env "${prefix}_DB_USER")"
    db_password="$(require_value mysql.env "${prefix}_DB_PASSWORD")"
    cs="$(require_value "$file" ConnectionStrings__DefaultConnection)"
    [[ -n "$cs" ]] || continue

    host="$(first_connection_value "$cs" Server Host || true)"
    port="$(first_connection_value "$cs" Port || true)"
    database="$(first_connection_value "$cs" Database || true)"
    user="$(first_connection_value "$cs" User "User ID" Uid Username || true)"
    password="$(first_connection_value "$cs" Password Pwd || true)"
    ssl_mode="$(first_connection_value "$cs" SslMode || true)"

    case "$host" in
      mysql|localhost|127.*|"") error "$file: connection Server '$host' is a Compose/local host; Azure uses the infrastructure VM." ;;
    esac
    [[ "$host" == "$EXPECTED_MYSQL_HOST" ]] || error "$file: connection Server must be '$EXPECTED_MYSQL_HOST' (infrastructure VM)."
    [[ "$port" == "3306" ]] || error "$file: connection Port must be '3306'."
    [[ "$database" == "$db_name" ]] || error "$file: connection Database does not match mysql.env ${prefix}_DB_NAME."
    [[ "$user" == "$db_user" ]] || error "$file: connection User does not match mysql.env ${prefix}_DB_USER."
    [[ "$password" == "$db_password" ]] || error "$file: connection Password does not match mysql.env ${prefix}_DB_PASSWORD."
    # Traffic crosses the VNet and the VM MySQL enforces require_secure_transport.
    case "${ssl_mode,,}" in
      required|verifyca|verifyfull) ;;
      *) error "$file: connection SslMode must be Required, VerifyCA or VerifyFull for Azure (got '${ssl_mode:-unset}')." ;;
    esac
  done

  # ---------------------------------------------------------------------------
  # Public URLs: HTTPS, not local, not Test
  # ---------------------------------------------------------------------------
  check_public_url() {
    local file="$1" key="$2" value
    value="$(require_value "$file" "$key")"
    [[ -n "$value" ]] || return 0
    [[ "$value" == https://* ]] || error "$file: $key must use https in Production (got '$value')."
    [[ "$value" != *localhost* && "$value" != *127.0.0.1* ]] || error "$file: $key must not point at localhost."
  }

  check_public_url gateway.env FRONTEND_ORIGIN
  check_public_url auth.env PasswordReset__FrontendBaseUrl
  check_public_url github.env GitHub__SetupCallbackUrl
  check_public_url github.env GitHub__FrontendReturnOrigin
  check_public_url jira.env Jira__RedirectUri

  [[ "$(require_value auth.env Cookie__Secure)" == "true" ]] || error "auth.env: Cookie__Secure must be 'true' in Production."
  require_value auth.env Brevo__ApiKey >/dev/null
  require_value auth.env Jwt__AccessTokenMinutes >/dev/null
  require_value auth.env Jwt__RefreshTokenDays >/dev/null

  github_sync_interval="$(require_value github.env GitHub__SyncIntervalMinutes)"
  if [[ ! "$github_sync_interval" =~ ^[0-9]+$ ]] || (( github_sync_interval < 1 || github_sync_interval > 1440 )); then
    error "github.env: GitHub__SyncIntervalMinutes must be between 1 and 1440."
  fi
fi

require_value mysql.env MYSQL_ROOT_PASSWORD >/dev/null

if [[ -n "$FORBIDDEN_HOSTS" ]]; then
  IFS=',' read -ra forbidden <<< "$FORBIDDEN_HOSTS"
  for host in "${forbidden[@]}"; do
    host="${host//[[:space:]]/}"
    [[ -n "$host" ]] || continue
    for file in "${!contracts[@]}"; do
      if grep -qiF -- "$host" "$ENV_DIR/$file"; then
        error "$file references Test host '$host'."
      fi
    done
  done
fi

require_value grafana.env GF_SECURITY_ADMIN_PASSWORD >/dev/null
# Grafana is public only through Nginx HTTPS; its generated links must match.
grafana_root_url="$(require_value grafana.env GF_SERVER_ROOT_URL)"
if [[ -n "$EXPECTED_GRAFANA_HOSTNAME" ]]; then
  [[ "${grafana_root_url%/}" == "https://$EXPECTED_GRAFANA_HOSTNAME" ]] \
    || error "grafana.env: GF_SERVER_ROOT_URL must be 'https://$EXPECTED_GRAFANA_HOSTNAME'."
else
  [[ "$grafana_root_url" == https://* ]] || error "grafana.env: GF_SERVER_ROOT_URL must use https."
fi

if (( $(error_count) > 0 )); then
  echo "Azure Production env validation failed with $(error_count) error(s)." >&2
  exit 1
fi
echo "Azure Production env files match config/env contracts and the Azure topology."
