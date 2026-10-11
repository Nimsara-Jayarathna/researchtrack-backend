#!/usr/bin/env bash
# Only the runner's labelled, loopback-bound disposable broker may be controlled.
set -euo pipefail
[[ $# == 1 && "$1" =~ ^(stop|start|snapshot)$ ]] || exit 2
[[ "${RT_KAFKA_TEST_ENABLE:-}" == 1 && "${RT_KAFKA_TEST_ENDPOINT:-}" == localhost:19092 ]] || exit 2
[[ "${RT_KAFKA_TEST_PROJECT:-}" =~ ^rt-kafka-fixture-[a-f0-9]{12}$ ]] || exit 2
fixture_tmp="${TMPDIR:-/tmp}"
fixture_tmp="$(cd "$fixture_tmp" && pwd -P)"
[[ "${RT_KAFKA_TEST_WORK:-}" == "$fixture_tmp/researchtrack-kafka-fixture."* ]] || exit 2
[[ "${RT_KAFKA_TEST_COMPOSE_FILE:-}" == */tests/Kafka/compose.yml ]] || exit 2
compose=(docker compose --env-file /dev/null --project-name "$RT_KAFKA_TEST_PROJECT" -f "$RT_KAFKA_TEST_COMPOSE_FILE")
id="$("${compose[@]}" ps -a -q kafka)"
[[ -n "$id" && "$id" == "${RT_KAFKA_TEST_BROKER_ID:-}" ]] || exit 2
# Require project + service + explicit fixture marker + exact data source and port.
docker inspect "$id" | python3 -c '
import json,os,sys
c=json.load(sys.stdin)[0]; labels=c["Config"]["Labels"]
assert labels["com.docker.compose.project"]==os.environ["RT_KAFKA_TEST_PROJECT"]
assert labels["com.docker.compose.service"]=="kafka"
assert labels["researchtrack.fixture"]=="isolated-kafka"
assert c["HostConfig"]["PortBindings"]=={"9092/tcp":[{"HostIp":"127.0.0.1","HostPort":"19092"}]}
assert any(m["Source"]==os.environ["RT_KAFKA_TEST_WORK"]+"/kafka-data" and m["Destination"]=="/fixture-data" for m in c["Mounts"])
'
case "$1" in
  stop) "${compose[@]}" stop kafka ;;
  start)
    "${compose[@]}" start kafka
    for ((attempt=0;attempt<90;attempt++)); do
      [[ "$(docker inspect --format '{{.State.Health.Status}}' "$id")" == healthy ]] && exit 0
      sleep 2
    done
    echo 'FAIL: disposable broker did not become healthy' >&2; exit 1 ;;
  snapshot)
    python3 - <<'PY'
import hashlib,json,os,pathlib,subprocess
work=pathlib.Path(os.environ['RT_KAFKA_TEST_WORK'])
compose=['docker','compose','--env-file','/dev/null','--project-name',os.environ['RT_KAFKA_TEST_PROJECT'],'-f',os.environ['RT_KAFKA_TEST_COMPOSE_FILE']]
containers={}
for name in ['kafka','mysql','prometheus','metric-snapshots','kafka-exporter']:
 cid=subprocess.check_output(compose+['ps','-a','-q',name],text=True).strip()
 containers[name]=subprocess.check_output(['docker','inspect','--format','{{.Id}} {{.State.StartedAt}}',cid],text=True).strip()
metadata=work/'kafka-data/meta.properties'
print(json.dumps({'containers':containers,'metadata_sha256':hashlib.sha256(metadata.read_bytes()).hexdigest()}))
PY
    ;;
esac
