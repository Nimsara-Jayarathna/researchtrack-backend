#!/usr/bin/env bash
# Non-destructive on-VM topic operations on the existing internal listener.
# create is the only write operation. No deletion or configuration alteration.
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
compose="$script_dir/compose.sh"
usage() { echo 'Usage: kafka-topics.sh list | exists NAME | describe NAME | config NAME | create NAME [PARTITIONS=3] [RETENTION_HOURS]' >&2; }
(($# > 0)) || { usage; exit 2; }
action="$1"; shift
case "$action" in list) (($# == 0)) || { usage; exit 2; } ;;
  exists|describe|config) (($# == 1)) || { usage; exit 2; } ;;
  create) (($# >= 1 && $# <= 3)) || { usage; exit 2; } ;;
  *) usage; exit 2 ;;
esac
topic="${1:-}"
if [[ "$action" != list ]]; then
  [[ "$topic" =~ ^[A-Za-z0-9._-]+$ && ${#topic} -le 249 && "$topic" != . && "$topic" != .. ]] \
    || { echo 'Invalid topic name.' >&2; exit 2; }
fi
cli=(timeout 60 "$compose" exec -T
  -e KAFKA_LOG4J_OPTS=-Dlog4j.configuration=file:/opt/kafka/config/tools-log4j.properties
  -e KAFKA_HEAP_OPTS=-Xmx256m kafka)
bin=/opt/kafka/bin
admin=("${cli[@]}" "$bin/kafka-topics.sh" --bootstrap-server localhost:29092)
case "$action" in
  list) "${admin[@]}" --list </dev/null ;;
  exists)
    topics="$("${admin[@]}" --list </dev/null)"
    if grep -Fxq -- "$topic" <<<"$topics"; then echo "PASS: topic $topic exists"; else echo "NOT VERIFIED: topic $topic does not exist"; exit 1; fi ;;
  describe) "${admin[@]}" --describe --topic "$topic" </dev/null ;;
  config) "${cli[@]}" "$bin/kafka-configs.sh" --bootstrap-server localhost:29092 --describe --entity-type topics --entity-name "$topic" </dev/null ;;
  create)
    partitions="${2:-3}"; hours="${3:-}"
    [[ "$partitions" =~ ^[1-9][0-9]{0,3}$ ]] || { echo 'Partitions must be 1..9999.' >&2; exit 2; }
    config=()
    if [[ -n "$hours" ]]; then
      [[ "$hours" =~ ^[1-9][0-9]{0,4}$ ]] || { echo 'Retention hours must be 1..99999.' >&2; exit 2; }
      config=(--config "retention.ms=$((hours * 3600000))")
    fi
    "${admin[@]}" --create --if-not-exists --topic "$topic" --partitions "$partitions" --replication-factor 1 "${config[@]}" </dev/null
    description="$("${admin[@]}" --describe --topic "$topic" </dev/null)"
    if ! awk -v p="$partitions" '
      /PartitionCount:/ { for(i=1;i<=NF;i++) { if($i=="PartitionCount:") pc=$(i+1); if($i=="ReplicationFactor:") rf=$(i+1) } }
      END { exit !(pc == p && rf == 1) }' <<<"$description"; then
      echo 'FAIL: existing topic differs from requested partitions/replication; no existing topic was altered.' >&2; exit 1
    fi
    if [[ -n "$hours" ]] && ! grep -Eq "(^|[[:space:],])retention.ms=$((hours * 3600000))($|[[:space:],])" <<<"$description"; then
      echo 'FAIL: existing topic retention differs; review with the topic owner, no configuration was altered.' >&2; exit 1
    fi
    printf 'PASS: topic %s has %s partitions, replication factor 1\n' "$topic" "$partitions"
    printf '%s\n' "$description" ;;
esac
