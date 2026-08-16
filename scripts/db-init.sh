#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command mysql
rt_load_dev_env
rt_validate_db_environment
rt_load_admin_env

printf 'ResearchTrack database provisioning\n'
printf 'MySQL endpoint: %s:%s\n' "$MYSQL_HOST" "$MYSQL_PORT"
printf 'Administrator: %s\n\n' "$MYSQL_ADMIN_USER"

# Verify administrator access before making changes.
rt_mysql_exec "$MYSQL_ADMIN_USER" "$MYSQL_ADMIN_PASSWORD" "SELECT VERSION();" >/dev/null

echo "Administrator connection successful."
echo "Creating/updating ResearchTrack development/test databases and scoped service users..."

for service in $(rt_all_db_services); do
  prefix="$(rt_service_prefix "$service")"
  db="$(rt_env_value "${prefix}_DB_NAME")"
  test_db="$(rt_env_value "${prefix}_TEST_DB_NAME")"
  user="$(rt_env_value "${prefix}_DB_USER")"
  password="$(rt_env_value "${prefix}_DB_PASSWORD")"

  for identifier in "$db" "$test_db" "$user"; do
    if ! rt_validate_identifier "$identifier"; then
      echo "Unsafe MySQL identifier in .env.local: $identifier" >&2
      exit 1
    fi
  done

  escaped_password="$(rt_sql_escape "$password")"
  escaped_user="$(rt_sql_escape "$user")"

  # '%' is used for the MySQL account host so the service account can authenticate
  # through the configured database endpoint. Network exposure is controlled outside
  # this repository; database privileges remain scoped to the owning service DBs.
  sql="
CREATE DATABASE IF NOT EXISTS \`$db\` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE DATABASE IF NOT EXISTS \`$test_db\` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE USER IF NOT EXISTS '$escaped_user'@'%' IDENTIFIED BY '$escaped_password';
ALTER USER '$escaped_user'@'%' IDENTIFIED BY '$escaped_password';
GRANT ALL PRIVILEGES ON \`$db\`.* TO '$escaped_user'@'%';
GRANT ALL PRIVILEGES ON \`$test_db\`.* TO '$escaped_user'@'%';
"

  rt_mysql_exec "$MYSQL_ADMIN_USER" "$MYSQL_ADMIN_PASSWORD" "$sql" >/dev/null
  printf '  ready: %-12s dev=%-32s test=%-32s user=%s\n' "$service" "$db" "$test_db" "$user"
done

rt_mysql_exec "$MYSQL_ADMIN_USER" "$MYSQL_ADMIN_PASSWORD" "FLUSH PRIVILEGES;" >/dev/null

echo
echo "Database provisioning complete."
echo "Next: ./scripts/db-status.sh"
