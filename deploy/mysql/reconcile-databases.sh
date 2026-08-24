#!/usr/bin/env bash
set -euo pipefail

required=(
  MYSQL_ROOT_PASSWORD
  AUTH_DB_NAME AUTH_DB_USER AUTH_DB_PASSWORD
  PROJECT_DB_NAME PROJECT_DB_USER PROJECT_DB_PASSWORD
  GITHUB_DB_NAME GITHUB_DB_USER GITHUB_DB_PASSWORD
  JIRA_DB_NAME JIRA_DB_USER JIRA_DB_PASSWORD
  MEETING_DB_NAME MEETING_DB_USER MEETING_DB_PASSWORD
  SUBMISSION_DB_NAME SUBMISSION_DB_USER SUBMISSION_DB_PASSWORD
)

for key in "${required[@]}"; do
  if [[ -z "${!key:-}" ]]; then
    echo "Missing required MySQL deployment value: $key" >&2
    exit 1
  fi
done

validate_identifier() {
  local value="$1" label="$2"
  if [[ ! "$value" =~ ^[A-Za-z0-9_]+$ ]]; then
    echo "$label must contain only letters, numbers, and underscores." >&2
    exit 1
  fi
}

sql_string_escape() {
  printf '%s' "$1" | sed "s/'/''/g"
}

create_or_update_database_user() {
  local database="$1" user="$2" password="$3"
  local escaped_user escaped_password

  validate_identifier "$database" "Database name"
  validate_identifier "$user" "Database user"

  escaped_user="$(sql_string_escape "$user")"
  escaped_password="$(sql_string_escape "$password")"

  mysql --protocol=socket -uroot -p"$MYSQL_ROOT_PASSWORD" <<SQL
CREATE DATABASE IF NOT EXISTS \`$database\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '$escaped_user'@'%' IDENTIFIED BY '$escaped_password';
ALTER USER '$escaped_user'@'%' IDENTIFIED BY '$escaped_password';
GRANT ALL PRIVILEGES ON \`$database\`.* TO '$escaped_user'@'%';
FLUSH PRIVILEGES;
SQL
}

create_or_update_database_user "$AUTH_DB_NAME" "$AUTH_DB_USER" "$AUTH_DB_PASSWORD"
create_or_update_database_user "$PROJECT_DB_NAME" "$PROJECT_DB_USER" "$PROJECT_DB_PASSWORD"
create_or_update_database_user "$GITHUB_DB_NAME" "$GITHUB_DB_USER" "$GITHUB_DB_PASSWORD"
create_or_update_database_user "$JIRA_DB_NAME" "$JIRA_DB_USER" "$JIRA_DB_PASSWORD"
create_or_update_database_user "$MEETING_DB_NAME" "$MEETING_DB_USER" "$MEETING_DB_PASSWORD"
create_or_update_database_user "$SUBMISSION_DB_NAME" "$SUBMISSION_DB_USER" "$SUBMISSION_DB_PASSWORD"

echo "ResearchTrack databases and service database users are reconciled."
