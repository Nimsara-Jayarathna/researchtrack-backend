#!/usr/bin/env bash
set -euo pipefail

ENV_DIR="${1:-}"
CONTRACT_ROOT="${2:-config/env}"
DEPLOY_ENVIRONMENT="${3:-}"

if [[ -z "$ENV_DIR" || ! -d "$ENV_DIR" ]]; then
  echo "Usage: $0 <environment-file-directory> [config-contract-root] [test|production]" >&2
  exit 1
fi

if [[ ! -d "$CONTRACT_ROOT" ]]; then
  echo "Configuration contract root does not exist: $CONTRACT_ROOT" >&2
  exit 1
fi

files=(
  mysql.env shared-auth.env gateway.env auth.env project.env
  github.env jira.env meeting.env submission.env
)

declare -A contracts=(
  [mysql.env]="$CONTRACT_ROOT/mysql/.env.example"
  [shared-auth.env]="$CONTRACT_ROOT/shared/.env.example"
  [gateway.env]="$CONTRACT_ROOT/gateway/.env.example"
  [auth.env]="$CONTRACT_ROOT/auth/.env.example"
  [project.env]="$CONTRACT_ROOT/project/.env.example"
  [github.env]="$CONTRACT_ROOT/github/.env.example"
  [jira.env]="$CONTRACT_ROOT/jira/.env.example"
  [meeting.env]="$CONTRACT_ROOT/meeting/.env.example"
  [submission.env]="$CONTRACT_ROOT/submission/.env.example"
)

for file in "${files[@]}"; do
  [[ -f "$ENV_DIR/$file" ]] || { echo "Missing deployment environment file: $file" >&2; exit 1; }
  [[ -f "${contracts[$file]}" ]] || { echo "Missing canonical environment contract: ${contracts[$file]}" >&2; exit 1; }
done

validate_file_format() {
  local file="$1" line line_number=0 key
  declare -A seen=()

  while IFS= read -r line || [[ -n "$line" ]]; do
    line_number=$((line_number + 1))
    line="${line%$'\r'}"
    [[ -z "$line" || "$line" =~ ^[[:space:]]*# ]] && continue

    if [[ "$line" != *=* ]]; then
      echo "Invalid environment entry in $file at line $line_number; expected KEY=value." >&2
      exit 1
    fi

    key="${line%%=*}"
    if [[ ! "$key" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]]; then
      echo "Invalid environment key '$key' in $file at line $line_number." >&2
      exit 1
    fi

    if [[ -n "${seen[$key]:-}" ]]; then
      echo "Duplicate environment key '$key' in $file." >&2
      exit 1
    fi
    seen[$key]=1
  done < "$file"
}

get_value() {
  local file="$1" key="$2" line value=""
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ "$line" == "$key="* ]] || continue
    value="${line#*=}"
    if [[ "$value" =~ ^\".*\"$ && ${#value} -ge 2 ]]; then
      value="${value:1:${#value}-2}"
    elif [[ "$value" =~ ^\'.*\'$ && ${#value} -ge 2 ]]; then
      value="${value:1:${#value}-2}"
    fi
    printf '%s' "$value"
    return 0
  done < "$file"
  return 1
}

contract_keys() {
  local file="$1" line key
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ -z "$line" || "$line" =~ ^[[:space:]]*# ]] && continue
    [[ "$line" == *=* ]] || continue
    key="${line%%=*}"
    printf '%s\n' "$key"
  done < "$file"
}

validate_contract_shape() {
  local runtime="$1" contract="$2" key
  while IFS= read -r key; do
    if ! grep -qE "^${key}=" "$runtime"; then
      echo "$(basename "$runtime") is missing '$key' from canonical contract $contract." >&2
      exit 1
    fi
  done < <(contract_keys "$contract")
}

for file in "${files[@]}"; do
  validate_file_format "$ENV_DIR/$file"
  validate_file_format "${contracts[$file]}"
  validate_contract_shape "$ENV_DIR/$file" "${contracts[$file]}"
done

require_value() {
  local file="$1" key="$2" value
  value="$(get_value "$file" "$key" || true)"
  if [[ -z "$value" ]]; then
    echo "Required key '$key' is missing or empty in $(basename "$file")." >&2
    exit 1
  fi
  case "$value" in
    CHANGE_ME*|__SET_ME__|__GENERATE__|YOUR_*|"<"*)
      echo "Required key '$key' in $(basename "$file") still contains a placeholder." >&2
      exit 1
      ;;
  esac
  printf '%s' "$value"
}

connection_value() {
  local connection_string="$1" requested_key="$2" part key value normalized requested_normalized
  requested_normalized="${requested_key,,}"
  requested_normalized="${requested_normalized// /}"
  requested_normalized="${requested_normalized//_/}"

  IFS=';' read -ra parts <<< "$connection_string"
  for part in "${parts[@]}"; do
    [[ "$part" == *=* ]] || continue
    key="${part%%=*}"
    value="${part#*=}"
    key="${key#"${key%%[![:space:]]*}"}"
    key="${key%"${key##*[![:space:]]}"}"
    value="${value#"${value%%[![:space:]]*}"}"
    value="${value%"${value##*[![:space:]]}"}"
    normalized="${key,,}"
    normalized="${normalized// /}"
    normalized="${normalized//_/}"
    if [[ "$normalized" == "$requested_normalized" ]]; then
      printf '%s' "$value"
      return 0
    fi
  done

  return 1
}

require_connection_value() {
  local service="$1" connection_string="$2" key="$3" value
  value="$(connection_value "$connection_string" "$key" || true)"
  if [[ -z "$value" ]]; then
    echo "$service.env ConnectionStrings__DefaultConnection is missing '$key'." >&2
    exit 1
  fi
  case "$value" in
    CHANGE_ME*|__SET_ME__|__GENERATE__|YOUR_*|"<"*)
      echo "$service.env ConnectionStrings__DefaultConnection '$key' still contains a placeholder." >&2
      exit 1
      ;;
  esac
  printf '%s' "$value"
}

mysql="$ENV_DIR/mysql.env"
shared="$ENV_DIR/shared-auth.env"
gateway="$ENV_DIR/gateway.env"

require_value "$mysql" MYSQL_ROOT_PASSWORD >/dev/null
require_value "$shared" Jwt__Issuer >/dev/null
require_value "$shared" Jwt__Audience >/dev/null
jwt_key="$(require_value "$shared" Jwt__SigningKey)"
if (( ${#jwt_key} < 32 )); then
  echo "Jwt__SigningKey must be at least 32 characters for the current HS256 configuration." >&2
  exit 1
fi

# The gateway uses the same friendly contract locally and in Docker. Program.cs maps
# these values into the ASP.NET/YARP configuration hierarchy at startup.
require_value "$gateway" FRONTEND_ORIGIN >/dev/null

declare -A gateway_urls=(
  [AUTH_SERVICE_URL]='http://auth:8080'
  [PROJECT_SERVICE_URL]='http://project:8080'
  [GITHUB_SERVICE_URL]='http://github:8080'
  [JIRA_SERVICE_URL]='http://jira:8080'
  [MEETING_SERVICE_URL]='http://meeting:8080'
  [SUBMISSION_SERVICE_URL]='http://submission:8080'
)
for key in "${!gateway_urls[@]}"; do
  actual="$(require_value "$gateway" "$key")"
  expected="${gateway_urls[$key]}"
  [[ "$actual" == "$expected" ]] || {
    echo "gateway.env $key must be '$expected' for private Compose service discovery." >&2
    exit 1
  }
done

declare -A service_db_names=(
  [auth]=AUTH_DB_NAME
  [project]=PROJECT_DB_NAME
  [github]=GITHUB_DB_NAME
  [jira]=JIRA_DB_NAME
  [meeting]=MEETING_DB_NAME
  [submission]=SUBMISSION_DB_NAME
)
declare -A service_db_users=(
  [auth]=AUTH_DB_USER
  [project]=PROJECT_DB_USER
  [github]=GITHUB_DB_USER
  [jira]=JIRA_DB_USER
  [meeting]=MEETING_DB_USER
  [submission]=SUBMISSION_DB_USER
)
declare -A service_db_passwords=(
  [auth]=AUTH_DB_PASSWORD
  [project]=PROJECT_DB_PASSWORD
  [github]=GITHUB_DB_PASSWORD
  [jira]=JIRA_DB_PASSWORD
  [meeting]=MEETING_DB_PASSWORD
  [submission]=SUBMISSION_DB_PASSWORD
)

for service in auth project github jira meeting submission; do
  file="$ENV_DIR/$service.env"
  mysql_name="$(require_value "$mysql" "${service_db_names[$service]}")"
  mysql_user="$(require_value "$mysql" "${service_db_users[$service]}")"
  mysql_password="$(require_value "$mysql" "${service_db_passwords[$service]}")"

  connection_string="$(require_value "$file" ConnectionStrings__DefaultConnection)"
  service_host="$(require_connection_value "$service" "$connection_string" Server)"
  service_port="$(require_connection_value "$service" "$connection_string" Port)"
  service_name="$(require_connection_value "$service" "$connection_string" Database)"
  service_user="$(connection_value "$connection_string" User || true)"
  [[ -n "$service_user" ]] || service_user="$(connection_value "$connection_string" "User ID" || true)"
  [[ -n "$service_user" ]] || service_user="$(connection_value "$connection_string" Uid || true)"
  [[ -n "$service_user" ]] || service_user="$(connection_value "$connection_string" Username || true)"
  [[ -n "$service_user" ]] || { echo "$service.env ConnectionStrings__DefaultConnection is missing 'User'." >&2; exit 1; }
  service_password="$(connection_value "$connection_string" Password || true)"
  [[ -n "$service_password" ]] || service_password="$(connection_value "$connection_string" Pwd || true)"
  [[ -n "$service_password" ]] || { echo "$service.env ConnectionStrings__DefaultConnection is missing 'Password'." >&2; exit 1; }
  service_ssl_mode="$(require_connection_value "$service" "$connection_string" SslMode)"

  [[ "$service_host" == "mysql" ]] || { echo "$service.env ConnectionStrings__DefaultConnection Server must be 'mysql' for Docker deployment." >&2; exit 1; }
  [[ "$service_port" == "3306" ]] || { echo "$service.env ConnectionStrings__DefaultConnection Port must be '3306' for Docker deployment." >&2; exit 1; }
  [[ "$service_name" == "$mysql_name" ]] || { echo "$service.env ConnectionStrings__DefaultConnection Database does not match mysql.env." >&2; exit 1; }
  [[ "$service_user" == "$mysql_user" ]] || { echo "$service.env ConnectionStrings__DefaultConnection User does not match mysql.env." >&2; exit 1; }
  [[ "$service_password" == "$mysql_password" ]] || { echo "$service.env ConnectionStrings__DefaultConnection Password does not match mysql.env." >&2; exit 1; }
  [[ "$service_ssl_mode" == "Disabled" ]] || { echo "$service.env ConnectionStrings__DefaultConnection SslMode must be 'Disabled'." >&2; exit 1; }
done

require_value "$ENV_DIR/auth.env" Jwt__AccessTokenMinutes >/dev/null
require_value "$ENV_DIR/auth.env" Jwt__RefreshTokenDays >/dev/null
require_value "$ENV_DIR/auth.env" Brevo__ApiKey >/dev/null

auth_url="$(require_value "$ENV_DIR/project.env" Services__Auth__BaseUrl)"
[[ "$auth_url" == "http://auth:8080" ]] || {
  echo "project.env Services__Auth__BaseUrl must be 'http://auth:8080' inside Compose." >&2
  exit 1
}

case "$DEPLOY_ENVIRONMENT" in
  test) expected_runtime_environment="Test" ;;
  production) expected_runtime_environment="Production" ;;
  "") expected_runtime_environment="" ;;
  *) echo "Unknown deployment environment '$DEPLOY_ENVIRONMENT'; expected test or production." >&2; exit 1 ;;
esac

for service in gateway auth project github jira meeting submission; do
  env_file="$ENV_DIR/$service.env"
  env_name="$(require_value "$env_file" ASPNETCORE_ENVIRONMENT)"
  dotnet_env="$(require_value "$env_file" DOTNET_ENVIRONMENT)"

  if [[ -n "$expected_runtime_environment" ]]; then
    [[ "$env_name" == "$expected_runtime_environment" ]] || {
      echo "$service.env ASPNETCORE_ENVIRONMENT must be '$expected_runtime_environment' for $DEPLOY_ENVIRONMENT deployment." >&2
      exit 1
    }
    [[ "$dotnet_env" == "$expected_runtime_environment" ]] || {
      echo "$service.env DOTNET_ENVIRONMENT must be '$expected_runtime_environment' for $DEPLOY_ENVIRONMENT deployment." >&2
      exit 1
    }
  fi

  urls="$(require_value "$env_file" ASPNETCORE_URLS)"
  [[ "$urls" == "http://+:8080" ]] || {
    echo "$service.env ASPNETCORE_URLS must be 'http://+:8080' for the container image." >&2
    exit 1
  }
done

echo "Deployment environment files match config/env contracts and passed deployment validation."
