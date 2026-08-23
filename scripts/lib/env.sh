#!/usr/bin/env bash
set -euo pipefail

RESEARCHTRACK_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

rt_cd_root() {
  cd "$RESEARCHTRACK_ROOT"
}

rt_require_command() {
  local command_name="$1"
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Required command not found: $command_name" >&2
    exit 1
  fi
}

rt_env_file() {
  local service="${1,,}"
  printf '%s/config/env/%s/.env.local\n' "$RESEARCHTRACK_ROOT" "$service"
}

rt_admin_env_file() {
  printf '%s/config/env/admin/.env.local\n' "$RESEARCHTRACK_ROOT"
}

rt_shared_env_file() {
  printf '%s/config/env/shared/.env.local\n' "$RESEARCHTRACK_ROOT"
}

rt_service_uses_shared_auth() {
  case "${1,,}" in
    auth|project) return 0 ;;
    *) return 1 ;;
  esac
}

rt_warn_if_insecure_permissions() {
  local file="$1"
  [[ -f "$file" ]] || return 0

  # Git Bash/MSYS/Cygwin on Windows does not provide reliable POSIX mode
  # semantics for files on NTFS. Avoid warning about an artificial 644 mode.
  case "$(uname -s 2>/dev/null || true)" in
    MINGW*|MSYS*|CYGWIN*) return 0 ;;
  esac

  local mode=""
  if stat -c '%a' "$file" >/dev/null 2>&1; then
    mode="$(stat -c '%a' "$file")"
  elif stat -f '%Lp' "$file" >/dev/null 2>&1; then
    mode="$(stat -f '%Lp' "$file")"
  fi

  if [[ -n "$mode" && "$mode" != "600" ]]; then
    echo "WARNING: $file permissions are $mode; recommended permissions are 600." >&2
  fi
}

rt_load_env_file() {
  local env_file="$1"
  local line_number=0 line key value

  [[ -f "$env_file" ]] || {
    echo "Environment file not found: $env_file" >&2
    return 1
  }

  while IFS= read -r line || [[ -n "$line" ]]; do
    line_number=$((line_number + 1))
    line="${line%$'\r'}"

    [[ -z "$line" || "$line" =~ ^[[:space:]]*# ]] && continue

    if [[ "$line" != *=* ]]; then
      echo "Invalid environment entry in $env_file at line $line_number. Expected KEY=value." >&2
      return 1
    fi

    key="${line%%=*}"
    value="${line#*=}"

    if [[ ! "$key" =~ ^[A-Za-z_][A-Za-z0-9_]*$ ]]; then
      echo "Invalid environment variable name '$key' in $env_file at line $line_number." >&2
      return 1
    fi

    if [[ "$value" =~ ^\".*\"$ && ${#value} -ge 2 ]]; then
      value="${value:1:${#value}-2}"
    elif [[ "$value" =~ ^\'.*\'$ && ${#value} -ge 2 ]]; then
      value="${value:1:${#value}-2}"
    fi

    export "$key=$value"
  done < "$env_file"
}

rt_load_dev_env() {
  local service="${1:-}"
  if [[ -z "$service" ]]; then
    echo "rt_load_dev_env requires a service name." >&2
    return 1
  fi

  local file
  file="$(rt_env_file "$service")"
  if [[ ! -f "$file" ]]; then
    echo "Missing $file. Run ./scripts/setup.sh first, then configure the service file." >&2
    exit 1
  fi

  rt_warn_if_insecure_permissions "$file"
  rt_load_env_file "$file"

  # Shared JWT values load after service-local settings so the platform
  # issuer/audience/signing key cannot drift across protected services.
  if rt_service_uses_shared_auth "$service"; then
    local shared_file
    shared_file="$(rt_shared_env_file)"
    if [[ ! -f "$shared_file" ]]; then
      echo "Missing $shared_file. Run ./scripts/setup.sh, then configure shared JWT values." >&2
      exit 1
    fi
    rt_warn_if_insecure_permissions "$shared_file"
    rt_load_env_file "$shared_file"
    rt_validate_shared_auth_environment
  fi

  export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
  export DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Development}"
  if [[ "${service,,}" != "gateway" ]]; then
    export MYSQL_HOST="${Database__Host:-}"
    export MYSQL_PORT="${Database__Port:-}"
  fi
}

rt_load_admin_env() {
  local file
  file="$(rt_admin_env_file)"

  if [[ ! -f "$file" ]]; then
    cat >&2 <<'MSG'
Missing config/env/admin/.env.local.

Database provisioning requires administrator credentials.
Copy config/env/admin/.env.example to .env.local only on the administrator machine/account.
Normal developers do not need this file.
MSG
    exit 1
  fi

  rt_warn_if_insecure_permissions "$file"
  rt_load_env_file "$file"
  rt_require_env MYSQL_HOST MYSQL_PORT MYSQL_ADMIN_USER MYSQL_ADMIN_PASSWORD
  rt_reject_placeholder MYSQL_ADMIN_USER
  rt_reject_placeholder MYSQL_ADMIN_PASSWORD
}

rt_require_env() {
  local key
  for key in "$@"; do
    if [[ -z "${!key:-}" ]]; then
      echo "Required environment variable is missing: $key" >&2
      return 1
    fi
  done
}

rt_reject_placeholder() {
  local key="$1"
  local value="${!key:-}"
  case "$value" in
    CHANGE_ME|__SET_ME__|__GENERATE__|YOUR_*|"<"*)
      echo "Environment variable $key still contains a placeholder value." >&2
      return 1
      ;;
  esac
}

rt_validate_shared_auth_environment() {
  rt_require_env Jwt__Issuer Jwt__Audience Jwt__SigningKey
  rt_reject_placeholder Jwt__Issuer
  rt_reject_placeholder Jwt__Audience
  rt_reject_placeholder Jwt__SigningKey
}

rt_validate_db_environment() {
  rt_require_env \
    Database__Host \
    Database__Port \
    Database__Name \
    Database__TestName \
    Database__Username \
    Database__Password \
    Database__SslMode \
    Database__AllowPublicKeyRetrieval

  rt_reject_placeholder Database__Name
  rt_reject_placeholder Database__TestName
  rt_reject_placeholder Database__Username
  rt_reject_placeholder Database__Password
  rt_reject_placeholder Database__SslMode
  rt_reject_placeholder Database__AllowPublicKeyRetrieval

  if [[ ! "$Database__Port" =~ ^[0-9]+$ || "$Database__Port" -lt 1 || "$Database__Port" -gt 65535 ]]; then
    echo "Database__Port must be a valid TCP port number (1-65535)." >&2
    return 1
  fi
  if [[ "$Database__AllowPublicKeyRetrieval" != "true" && "$Database__AllowPublicKeyRetrieval" != "false" ]]; then
    echo "Database__AllowPublicKeyRetrieval must be true or false." >&2
    return 1
  fi
}

rt_database_name() {
  local mode="${2:-${1:-dev}}"
  case "$mode" in
    dev) rt_require_env Database__Name; printf '%s\n' "$Database__Name" ;;
    test) rt_require_env Database__TestName; printf '%s\n' "$Database__TestName" ;;
    *) echo "Unknown database mode '$mode'. Expected dev or test." >&2; return 1 ;;
  esac
}

rt_db_connection() {
  local mode="${2:-${1:-dev}}" db
  rt_validate_db_environment
  db="$(rt_database_name "$mode")"
  printf 'Server=%s;Port=%s;Database=%s;User=%s;Password=%s;SslMode=%s;AllowPublicKeyRetrieval=%s;\n' \
    "$Database__Host" "$Database__Port" "$db" "$Database__Username" "$Database__Password" \
    "$Database__SslMode" "$Database__AllowPublicKeyRetrieval"
}

rt_db_connection_for_service() (
  set -euo pipefail
  local service="${1,,}" mode="${2:-dev}"
  rt_load_dev_env "$service"
  rt_validate_db_environment
  rt_db_connection "$mode"
)

rt_service_project() {
  case "${1,,}" in
    gateway) echo "src/Gateway/ResearchTrack.Gateway/ResearchTrack.Gateway.csproj" ;;
    auth) echo "src/Services/ResearchTrack.AuthService/ResearchTrack.AuthService.csproj" ;;
    project) echo "src/Services/ResearchTrack.ProjectService/ResearchTrack.ProjectService.csproj" ;;
    github) echo "src/Services/ResearchTrack.GitHubService/ResearchTrack.GitHubService.csproj" ;;
    jira) echo "src/Services/ResearchTrack.JiraService/ResearchTrack.JiraService.csproj" ;;
    meeting) echo "src/Services/ResearchTrack.MeetingService/ResearchTrack.MeetingService.csproj" ;;
    submission) echo "src/Services/ResearchTrack.SubmissionService/ResearchTrack.SubmissionService.csproj" ;;
    *) echo "Unknown service: $1" >&2; return 1 ;;
  esac
}

rt_service_context() {
  case "${1,,}" in
    auth) echo "AuthDbContext" ;;
    project) echo "ProjectDbContext" ;;
    github) echo "GitHubDbContext" ;;
    jira) echo "JiraDbContext" ;;
    meeting) echo "MeetingDbContext" ;;
    submission) echo "SubmissionDbContext" ;;
    *) echo "Service has no EF Core context: $1" >&2; return 1 ;;
  esac
}

rt_service_port() {
  case "${1,,}" in
    gateway) echo "5000" ;;
    auth) echo "5101" ;;
    project) echo "5102" ;;
    github) echo "5103" ;;
    jira) echo "5104" ;;
    meeting) echo "5105" ;;
    submission) echo "5106" ;;
    *) echo "Unknown service: $1" >&2; return 1 ;;
  esac
}

rt_service_prefix() {
  case "${1,,}" in
    auth) echo "AUTH" ;;
    project) echo "PROJECT" ;;
    github) echo "GITHUB" ;;
    jira) echo "JIRA" ;;
    meeting) echo "MEETING" ;;
    submission) echo "SUBMISSION" ;;
    *) echo "Service has no database: $1" >&2; return 1 ;;
  esac
}

rt_all_db_services() {
  echo "auth project github jira meeting submission"
}

rt_gateway_env() {
  rt_require_env FRONTEND_ORIGIN AUTH_SERVICE_URL PROJECT_SERVICE_URL GITHUB_SERVICE_URL JIRA_SERVICE_URL MEETING_SERVICE_URL SUBMISSION_SERVICE_URL
  for key in FRONTEND_ORIGIN AUTH_SERVICE_URL PROJECT_SERVICE_URL GITHUB_SERVICE_URL JIRA_SERVICE_URL MEETING_SERVICE_URL SUBMISSION_SERVICE_URL; do
    rt_reject_placeholder "$key"
  done

  export Frontend__AllowedOrigins__0="$FRONTEND_ORIGIN"
  export ReverseProxy__Clusters__auth__Destinations__primary__Address="$AUTH_SERVICE_URL"
  export ReverseProxy__Clusters__project__Destinations__primary__Address="$PROJECT_SERVICE_URL"
  export ReverseProxy__Clusters__github__Destinations__primary__Address="$GITHUB_SERVICE_URL"
  export ReverseProxy__Clusters__jira__Destinations__primary__Address="$JIRA_SERVICE_URL"
  export ReverseProxy__Clusters__meeting__Destinations__primary__Address="$MEETING_SERVICE_URL"
  export ReverseProxy__Clusters__submission__Destinations__primary__Address="$SUBMISSION_SERVICE_URL"
}

rt_mysql_defaults_file() {
  local user="$1" password="$2" host="${3:-${MYSQL_HOST:-}}" port="${4:-${MYSQL_PORT:-}}"
  local file

  [[ -n "$host" && -n "$port" ]] || {
    echo "MYSQL_HOST and MYSQL_PORT are required before connecting to MySQL." >&2
    return 1
  }

  file="$(mktemp "${TMPDIR:-/tmp}/researchtrack-mysql.XXXXXX")"
  chmod 600 "$file"
  local escaped_host escaped_user escaped_password
  escaped_host="$(rt_mysql_option_escape "$host")"
  escaped_user="$(rt_mysql_option_escape "$user")"
  escaped_password="$(rt_mysql_option_escape "$password")"

  cat > "$file" <<EOF2
[client]
host=$escaped_host
port=$port
user=$escaped_user
password=$escaped_password
protocol=tcp
EOF2
  printf '%s\n' "$file"
}

rt_mysql_option_escape() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  printf '"%s"' "$value"
}

rt_mysql_exec() (
  set -euo pipefail
  local user="$1" password="$2" sql="$3" db="${4:-}"
  local defaults

  defaults="$(rt_mysql_defaults_file "$user" "$password")"
  trap 'rm -f "$defaults"' EXIT HUP INT TERM

  if [[ -n "$db" ]]; then
    mysql --defaults-extra-file="$defaults" --batch --skip-column-names "$db" -e "$sql"
  else
    mysql --defaults-extra-file="$defaults" --batch --skip-column-names -e "$sql"
  fi
)

rt_mysql_check() {
  local user="$1" password="$2" db="$3"
  local output status

  set +e
  output="$(rt_mysql_exec "$user" "$password" "SELECT 1;" "$db" 2>&1)"
  status=$?
  set -e

  if [[ "$status" -eq 0 ]]; then
    printf 'OK\n'
    return 0
  fi

  case "$output" in
    *"ERROR 1045"*) printf 'AUTH FAILED\n' ;;
    *"ERROR 1049"*) printf 'DATABASE MISSING\n' ;;
    *"ERROR 2002"*|*"ERROR 2003"*) printf 'CONNECTION FAILED\n' ;;
    *) printf 'FAILED\n' ;;
  esac
  return 1
}

rt_sql_escape() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\'/\'\'}"
  printf '%s' "$value"
}

rt_validate_identifier() {
  [[ "$1" =~ ^[A-Za-z0-9_]+$ ]]
}
