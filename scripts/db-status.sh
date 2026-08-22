#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command mysql

printf '%-16s %-34s %-22s\n' "SERVICE" "DATABASE" "STATUS"
printf '%-16s %-34s %-22s\n' "----------------" "----------------------------------" "----------------------"
failed=0
for service in $(rt_all_db_services); do
  rt_load_dev_env "$service"
  rt_validate_db_environment
  for mode in dev test; do
    db="$(rt_database_name "$mode")"
    status="$(rt_mysql_check "$Database__Username" "$Database__Password" "$db" || true)"
    printf '%-16s %-34s %-22s\n' "$service/$mode" "$db" "$status"
    [[ "$status" == "OK" ]] || failed=1
  done
done
exit "$failed"
