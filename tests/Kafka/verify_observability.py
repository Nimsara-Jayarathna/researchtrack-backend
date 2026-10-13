#!/usr/bin/env python3
"""Live assertions against the disposable fixture, never Azure."""
import json
import os
from pathlib import Path
import sys
import time
from urllib.parse import urlencode
from urllib.request import urlopen

if os.environ.get('RT_KAFKA_TEST_ENABLE') != '1' or os.environ.get('RT_KAFKA_TEST_PROMETHEUS') != 'http://127.0.0.1:19090':
    raise SystemExit('Use scripts/test-kafka-integration.sh; only its loopback fixture is permitted.')
mode = sys.argv[1]
assert mode in ('healthy', 'unavailable')
base = 'http://127.0.0.1:19090'

def api(path):
    with urlopen(base + path, timeout=5) as response:
        result = json.load(response)
    assert result['status'] == 'success'
    return result['data']

def sample(query):
    result = api('/api/v1/query?' + urlencode({'query': query}))['result']
    return float(result[0]['value'][1]) if len(result) == 1 else None

deadline = time.monotonic() + 120
while time.monotonic() < deadline:
    try:
        if mode == 'healthy':
            healthy = sample('researchtrack_kafka_broker_healthy') == 1
            healthy &= sample('up{job="researchtrack-kafka"}') == 1
            healthy &= sample('kafka_brokers') == 1
            offset = sample('kafka_topic_partition_current_offset{topic="researchtrack.deployment-smoke",partition="0"}')
            healthy &= offset is not None and offset >= 0
            for service in ('github', 'jira'):
                healthy &= sample(f'sum(kafka_consumergroup_lag{{consumergroup="researchtrack-{service}-webhook-v1"}})') == 0
                healthy &= sample(f'researchtrack_kafka_enabled{{service="{service}"}}') == 1
            healthy &= sample('absent(ALERTS{alertname=~"ResearchTrackKafka(Broker|Exporter)Unavailable",alertstate="firing"})') == 1
        else:
            firing = sample('sum(ALERTS{alertname=~"ResearchTrackKafka(Broker|Exporter)Unavailable",alertstate="firing"})')
            healthy = firing is not None and firing >= 1
        if healthy:
            break
    except (OSError, AssertionError):
        pass
    time.sleep(2)
else:
    raise SystemExit('FAIL: isolated broker/exporter alert state did not converge: ' + mode)

evidence = {
    'scope': 'disposable isolated fixture only', 'mode': mode,
    'targets': api('/api/v1/targets'), 'alerts': api('/api/v1/alerts'),
    'broker_health': sample('researchtrack_kafka_broker_healthy'),
}
if mode == 'healthy':
    for service in ('github', 'jira'):
        path = Path(os.environ['RT_KAFKA_TEST_METRICS_DIRECTORY']) / (service + '-business.json')
        business = json.loads(path.read_text())
        assert business['inboxReceipts'] == 1
        if service == 'github':
            assert business['commits'] == 1 and business['syncRuns'] == 1
        else:
            assert business['syncJobs'] == 1 and business['syncRevision'] > 0
        evidence[service + '_business'] = business
directory = Path(os.environ['RT_KAFKA_TEST_EVIDENCE_DIRECTORY'])
directory.mkdir(parents=True, exist_ok=True)
(directory / (mode + '-' + str(time.time_ns()) + '.json')).write_text(json.dumps(evidence, indent=2) + '\n')
print('PASS: isolated exporter/broker scrape and alert state: ' + mode)
