#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command mysql
rt_load_admin_env

admin_host="$MYSQL_HOST"
admin_port="$MYSQL_PORT"
admin_user="$MYSQL_ADMIN_USER"
admin_password="$MYSQL_ADMIN_PASSWORD"

printf 'ResearchTrack database provisioning\n'
printf 'Administrator endpoint: %s:%s\n' "$admin_host" "$admin_port"
printf 'Administrator: %s\n\n' "$admin_user"

rt_mysql_exec "$admin_user" "$admin_password" "SELECT VERSION();" >/dev/null

echo "Administrator connection successful."
echo "Creating/updating service-owned development/test databases and scoped users..."

for service in $(rt_all_db_services); do
  rt_load_dev_env "$service"
  rt_validate_db_environment

  db="$Database__Name"
  test_db="$Database__TestName"
  user="$Database__Username"
  password="$Database__Password"

  for identifier in "$db" "$test_db" "$user"; do
    if ! rt_validate_identifier "$identifier"; then
      echo "Unsafe MySQL identifier in config/env/$service/.env.local: $identifier" >&2
      exit 1
    fi
  done

  escaped_password="$(rt_sql_escape "$password")"
  escaped_user="$(rt_sql_escape "$user")"
  sql="
CREATE DATABASE IF NOT EXISTS \`$db\` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE DATABASE IF NOT EXISTS \`$test_db\` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE USER IF NOT EXISTS '$escaped_user'@'%' IDENTIFIED BY '$escaped_password';
ALTER USER '$escaped_user'@'%' IDENTIFIED BY '$escaped_password';
GRANT ALL PRIVILEGES ON \`$db\`.* TO '$escaped_user'@'%';
GRANT ALL PRIVILEGES ON \`$test_db\`.* TO '$escaped_user'@'%';
"

  MYSQL_HOST="$admin_host" MYSQL_PORT="$admin_port" rt_mysql_exec "$admin_user" "$admin_password" "$sql" >/dev/null
  printf '  ready: %-12s dev=%-32s test=%-32s user=%s\n' "$service" "$db" "$test_db" "$user"
done

MYSQL_HOST="$admin_host" MYSQL_PORT="$admin_port" rt_mysql_exec "$admin_user" "$admin_password" "FLUSH PRIVILEGES;" >/dev/null

echo
echo "Database provisioning complete."
