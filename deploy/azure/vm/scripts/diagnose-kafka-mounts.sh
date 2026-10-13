#!/usr/bin/env bash
# Read-only. No environment contents, credentials, messages or mutations.
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
[[ $# == 0 ]] || { echo 'Usage: diagnose-kafka-mounts.sh' >&2; exit 2; }
id="$("$script_dir/compose.sh" ps -q kafka)"
[[ -n "$id" ]] || { echo 'Kafka container absent' >&2; exit 1; }
docker inspect "$id" | jq '.[0] | {Id, Image, Mounts: [.Mounts[] | {Type, Name, Source, Destination, RW}], mount_count: (.Mounts | length)}'
image="$(docker inspect --format '{{.Image}}' "$id")"
echo 'Image-declared volumes:'
docker image inspect "$image" --format '{{json .Config.Volumes}}' | jq .
# Inspect only the data filesystem identity, never modify/repair its mount.
findmnt --target /data/researchtrack/kafka/data --output TARGET,SOURCE,FSTYPE,OPTIONS
