#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command mysql
rt_load_dev_env
rt_validate_db_environment

printf 'MySQL endpoint: %s:%s\n\n' "$MYSQL_HOST" "$MYSQL_PORT"
printf '%-16s %-34s %-22s\n' "SERVICE" "DATABASE" "STATUS"
printf '%-16s %-34s %-22s\n' "----------------" "----------------------------------" "----------------------"

failed=0
for service in $(rt_all_db_services); do
  prefix="$(rt_service_prefix "$service")"
  user="$(rt_env_value "${prefix}_DB_USER")"
  password="$(rt_env_value "${prefix}_DB_PASSWORD")"

  for mode in dev test; do
    db="$(rt_database_name "$service" "$mode")"
    status="$(rt_mysql_check "$user" "$password" "$db" || true)"
    printf '%-16s %-34s %-22s\n' "$service/$mode" "$db" "$status"
    [[ "$status" == "OK" ]] || failed=1
  done
done

exit "$failed"
