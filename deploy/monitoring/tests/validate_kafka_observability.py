#!/usr/bin/env python3
"""Offline monitoring checks. Uses only synthetic configuration; never reads real env files."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[3]
LEGACY_PANELS_HASH = "bc1dba3d29d236a7e0d0552f4072c9ad10f008861436ac145a57d976f0b1df75"
LEGACY_RULES_HASH = "40ed077298465a0567e0dba338ca1552993b603fda9bbd8d4a2e8056f8a38ffa"
LEGACY_VM_COMPOSE_HASH = "7715d23df5df3d6378ae7adf503df24f09371c43fb241722a73cd062d0dd42a9"
IMAGE = "prom/prometheus:v3.15.0"

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--promtool")
    parser.add_argument("--docker", action="store_true")
    args = parser.parse_args()
    if not args.promtool and not args.docker:
        parser.error("provide --promtool PATH or --docker")
    checks = 0
    def check(condition, message):
        nonlocal checks
        if not condition:
            raise AssertionError(message)
        checks += 1
    dashboards = ROOT / "deploy/monitoring/grafana/provisioning/dashboards/json"
    queries = set()
    uids = set()
    for path in sorted(dashboards.glob("*.json")):
        dashboard = json.loads(path.read_text())
        check(dashboard["uid"] not in uids, "unique dashboard UID")
        uids.add(dashboard["uid"])
        def panels(items):
            for panel in items:
                if panel.get("panels"):
                    yield from panels(panel["panels"])
                yield panel
        for panel in panels(dashboard["panels"]):
            if panel["type"] == "row":
                continue
            check(panel.get("datasource", {}).get("uid") == "prometheus", str(path) + " datasource")
            for target in panel.get("targets", []):
                if target.get("expr"):
                    queries.add(target["expr"].replace('$environment', 'production'))
        if dashboard["uid"] == "researchtrack-overview":
            original = [p for p in dashboard["panels"] if p["id"] <= 10]
            check(hashlib.sha256(json.dumps(original, sort_keys=True).encode()).hexdigest() == LEGACY_PANELS_HASH, "all original overview panels preserved")
        if dashboard["uid"] == "researchtrack-kafka-observability":
            check(len([p for p in dashboard["panels"] if p["type"] != "row"]) == 18, "18 Kafka panels")
    check(uids == {"researchtrack-overview", "github-sync-operations", "jira-integration-operations", "researchtrack-kafka-observability"}, "four provisioned dashboards")
    provisioning = (dashboards.parent / "dashboards.yml").read_text()
    check('/var/lib/grafana/dashboards' in provisioning or '/etc/grafana/provisioning/dashboards/json' in provisioning, "file dashboard provisioning path")
    rules = (ROOT / "deploy/monitoring/rules/researchtrack-alerts.yml").read_text()
    original = rules.split('  - name: researchtrack-kafka')[0].rstrip()
    # Ignore only the new explanatory comment placed between groups.
    original = original.split('# Kafka observability')[0].rstrip()
    check(hashlib.sha256(original.encode()).hexdigest() == LEGACY_RULES_HASH, "existing alert groups preserved")
    families = set(re.findall(r'Create(?:Counter|Gauge|Histogram)\(\s*"([a-z_]+)"', (ROOT / "src/BuildingBlocks/Kafka/KafkaMetrics.cs").read_text()))
    exporter_families = {'kafka_brokers', 'kafka_topic_partitions', 'kafka_consumergroup_members', 'kafka_consumergroup_lag', 'kafka_consumergroup_current_offset', 'kafka_topic_partition_current_offset'}
    for query in queries:
        for name in re.findall(r'\bkafka_[a-z_]+', query):
            check(name in exporter_families, "v1.10.0 exporter family exists: " + name)
        for name in re.findall(r'\bresearchtrack_kafka_[a-z_]+', query):
            base = re.sub(r'_(bucket|sum|count)$', '', name)
            check(name == "researchtrack_kafka_broker_healthy" or name in families or base in families, "dashboard metric exists: " + name)
    vm_source = (ROOT / 'deploy/azure/vm/compose.yml').read_text()
    original_vm = re.sub(r'(?ms)^  kafka-exporter:\n.*?(?=^  prometheus:\n)', '', vm_source)
    check(hashlib.sha256(original_vm.encode()).hexdigest() == LEGACY_VM_COMPOSE_HASH, "all existing VM services, storage and network definitions preserved")
    # Compose rendering does not contact Docker daemon or resolve any real env_file.
    env = dict(os.environ, INFRA_PRIVATE_IP="10.20.10.4")
    compose = json.loads(subprocess.check_output(['docker', 'compose', '--env-file', '/dev/null', '-f', str(ROOT / 'deploy/azure/vm/compose.yml'), 'config', '--no-env-resolution', '--no-path-resolution', '--format', 'json'], env=env))
    exporter = compose['services']['kafka-exporter']
    check(not exporter.get('ports'), "exporter has no host port")
    check(not exporter.get('volumes'), "exporter mounts no TLS keys or socket")
    check(exporter['read_only'] and exporter['cap_drop'] == ['ALL'], "exporter privileges bounded")
    check(int(exporter['mem_limit']) <= 100663296 and float(exporter['cpus']) <= .1, "exporter resource budget")
    check(exporter['image'].startswith('danielqsj/kafka-exporter:v1.10.0@sha256:'), "pinned exporter")
    check('--kafka.server=kafka:29092' in exporter['command'] and '--no-kafka.allow-auto-topic-creation' in exporter['command'], "private read-only Kafka inspection")
    fixture_env = dict(env, RT_KAFKA_TEST_UID='1000', RT_KAFKA_TEST_GID='1000', RT_KAFKA_TEST_WORK='/tmp/synthetic-kafka-fixture', RT_KAFKA_TEST_PASSWORD='SYNTHETIC_ONLY', RT_KAFKA_TEST_CLUSTER_ID='AAAAAAAAAAAAAAAAAAAAAA')
    subprocess.run(['docker', 'compose', '--env-file', '/dev/null', '-f', str(ROOT / 'tests/Kafka/compose.yml'), 'config', '--quiet'], env=fixture_env, check=True)
    checks += 1
    with tempfile.TemporaryDirectory(prefix='rt-kafka-monitoring-check-') as directory:
        work = Path(directory)
        shutil.copytree(ROOT / 'deploy/monitoring/rules', work / 'rules')
        shutil.copytree(ROOT / 'deploy/monitoring/tests', work / 'tests')
        target_root = '/work' if args.docker else str(work)
        def promtool(*command):
            prefix = ['docker', 'run', '--rm', '--network', 'none', '--read-only', '-v', f'{work}:/work:ro', '-w', '/work', '--entrypoint', '/bin/promtool', IMAGE] if args.docker else [args.promtool]
            subprocess.run(prefix + list(command), cwd=work, check=True, stdout=subprocess.DEVNULL)
        for name, source in [('azure', 'deploy/azure/vm/prometheus/prometheus.yml.template'), ('test', 'deploy/monitoring/prometheus.yml'), ('isolated', 'tests/Kafka/prometheus.yml')]:
            config = (ROOT / source).read_text().replace('__ACA_ENV_DOMAIN__', 'synthetic.internal.example.test').replace('__ACA_TLS_INSECURE__', 'false').replace('/etc/prometheus/rules/', target_root + '/rules/')
            (work / (name + '.yml')).write_text(config)
            promtool('check', 'config', target_root + '/' + name + '.yml')
            checks += 1
        promtool('check', 'rules', target_root + '/rules/researchtrack-alerts.yml')
        promtool('test', 'rules', target_root + '/tests/kafka-alerts.test.yml')
        checks += 2
        for query in queries:
            promtool('--experimental', 'promql', 'format', query)
        checks += len(queries)
    scenarios = len(re.findall(r'^- name:', (ROOT / 'deploy/monitoring/tests/kafka-alerts.test.yml').read_text(), re.MULTILINE))
    print(f'PASS: {checks} monitoring configuration assertions/checks; {scenarios} Prometheus scenarios; {len(queries)} dashboard expressions parsed.')

if __name__ == '__main__':
    main()
