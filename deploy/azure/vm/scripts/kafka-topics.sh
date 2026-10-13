#!/usr/bin/env bash
# Non-destructive on-VM topic operations on the existing internal listener.
# create is the only write operation. No deletion or configuration alteration.
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
compose="$script_dir/compose.sh"
usage() { echo 'Usage: kafka-topics.sh list | exists NAME | describe NAME | config NAME | create NAME [PARTITIONS=3] [RETENTION_HOURS] | validate-manifest [FILE] | reconcile [--dry-run|--check|--apply] [FILE]' >&2; }
(($# > 0)) || { usage; exit 2; }
action="$1"; shift
if [[ "$action" == validate-manifest || "$action" == reconcile ]]; then
  mode=--dry-run
  if [[ "$action" == reconcile && "${1:-}" == --* ]]; then mode="$1"; shift; fi
  case "$mode" in --dry-run|--check|--apply) ;; *) usage; exit 2 ;; esac
  (($# <= 1)) || { usage; exit 2; }
  manifest="${1:-$script_dir/../kafka/topics.json}"
  if ! jq -e -f "$script_dir/kafka-topic-manifest.jq" "$manifest" >/dev/null 2>&1; then
    echo 'FAIL: invalid topic manifest (required fields, naming, duplicates, approval, partitions, replication or retention).' >&2
    exit 2
  fi
  if [[ "$action" == validate-manifest ]]; then echo 'PASS: topic manifest valid'; exit 0; fi
  # Inspect EVERY approved existing topic before permitting any creation.
  # The normal deployment/startup paths never invoke --apply.
  topics="$("$script_dir/kafka-topics.sh" list)" || { echo 'FAIL: broker topic query failed.' >&2; exit 1; }
  missing=(); drift=0
  while IFS=$'\t' read -r name partitions hours; do
    if ! grep -Fxq -- "$name" <<<"$topics"; then
      printf 'MISSING: %s (partitions=%s rf=1 retentionHours=%s)\n' "$name" "$partitions" "$hours"
      missing+=("$name" "$partitions" "$hours")
      continue
    fi
    description="$("$script_dir/kafka-topics.sh" describe "$name")" || { echo "FAIL: describe failed for $name" >&2; exit 1; }
    if ! awk -v p="$partitions" '
      /PartitionCount:/ { for(i=1;i<=NF;i++) { if($i=="PartitionCount:") pc=$(i+1); if($i=="ReplicationFactor:") rf=$(i+1) } }
      END { exit !(pc == p && rf == 1) }' <<<"$description"; then
      echo "DRIFT: $name partitions/replication; owner review required." >&2; drift=1
    fi
    # --all includes effective broker defaults, not only dynamic overrides.
    effective="$(timeout 60 "$compose" exec -T \
      -e KAFKA_LOG4J_OPTS=-Dlog4j.configuration=file:/opt/kafka/config/tools-log4j.properties \
      -e KAFKA_HEAP_OPTS=-Xmx256m kafka /opt/kafka/bin/kafka-configs.sh \
      --bootstrap-server localhost:29092 --describe --all --entity-type topics --entity-name "$name" </dev/null)" \
      || { echo "FAIL: config query failed for $name" >&2; exit 1; }
    if ! grep -Eq "^[[:space:]]*retention[.]ms=$((hours * 3600000))([[:space:]]|$)" <<<"$effective"; then
      echo "DRIFT: $name retention; owner review required." >&2; drift=1
    fi
    if ((drift == 0)); then echo "MATCH: $name"; fi
  done < <(jq -r '.topics[] | select(.approved) | [.name,.partitions,.retentionHours] | @tsv' "$manifest")
  ((drift == 0)) || { echo 'FAIL: drift found; no topics created or altered.' >&2; exit 1; }
  if [[ "$mode" == --check && ${#missing[@]} -gt 0 ]]; then exit 1; fi
  if [[ "$mode" == --apply ]]; then
    for ((i=0; i<${#missing[@]}; i+=3)); do
      "$script_dir/kafka-topics.sh" create "${missing[i]}" "${missing[i+1]}" "${missing[i+2]}"
    done
    # Detect drift/races after creation, without repairing existing topics.
    "$script_dir/kafka-topics.sh" reconcile --check "$manifest"
  else
    echo "PASS: $mode complete; unapproved topics excluded; no broker changes."
  fi
  exit 0
fi
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
