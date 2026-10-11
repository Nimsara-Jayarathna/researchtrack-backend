#!/usr/bin/env python3
"""Adversarial offline tests of the real disposable-broker control boundary."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('broker-control.sh')

class BrokerControlSafety(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='researchtrack-kafka-fixture.')
        self.work = Path(self.tmp.name).resolve()
        self.bin = self.work / 'bin'
        self.bin.mkdir()
        self.log = self.work / 'calls'
        self.inspection = self.work / 'inspect.json'
        self.env = dict(os.environ, TMPDIR=str(self.work.parent), PATH=str(self.bin)+':'+os.environ['PATH'],
                        RT_KAFKA_TEST_ENABLE='1', RT_KAFKA_TEST_ENDPOINT='localhost:19092',
                        RT_KAFKA_TEST_PROJECT='rt-kafka-fixture-0123456789ab',
                        RT_KAFKA_TEST_WORK=str(self.work), RT_KAFKA_TEST_COMPOSE_FILE='/fixture/tests/Kafka/compose.yml',
                        RT_KAFKA_TEST_BROKER_ID='fixture-id', CALLS=str(self.log), INSPECTION=str(self.inspection))
        self.container = {'Config': {'Labels': {'com.docker.compose.project':self.env['RT_KAFKA_TEST_PROJECT'],
                            'com.docker.compose.service':'kafka','researchtrack.fixture':'isolated-kafka'}},
                          'HostConfig': {'PortBindings': {'9092/tcp':[{'HostIp':'127.0.0.1','HostPort':'19092'}]}},
                          'Mounts':[{'Source':str(self.work)+'/kafka-data','Destination':'/fixture-data'}]}
        docker = self.bin / 'docker'
        docker.write_text('''#!/usr/bin/env python3
import json,os,sys
args=sys.argv[1:]
with open(os.environ['CALLS'],'a') as f: f.write(json.dumps(args)+'\\n')
if args[0]=='inspect':
    if '--format' in args: print('healthy')
    else: print(open(os.environ['INSPECTION']).read())
elif 'ps' in args: print('fixture-id')
''')
        docker.chmod(0o755)
    def tearDown(self):
        self.tmp.cleanup()
    def run_control(self, action='stop', **changes):
        self.inspection.write_text(json.dumps([self.container]))
        result = subprocess.run(['bash',str(SCRIPT),action],env=dict(self.env,**changes),capture_output=True,text=True)
        calls = [json.loads(line) for line in self.log.read_text().splitlines()] if self.log.exists() else []
        return result,calls
    def assert_no_mutation(self, result, calls):
        self.assertNotEqual(0,result.returncode)
        self.assertFalse(any('stop' in call or 'start' in call for call in calls))
    def test_valid_fixture_stop_is_scoped_to_kafka(self):
        result,calls=self.run_control()
        self.assertEqual(0,result.returncode,result.stderr)
        mutation=[call for call in calls if 'stop' in call]
        self.assertEqual(1,len(mutation))
        self.assertEqual(['stop','kafka'],mutation[0][-2:])
    def test_valid_start_checks_same_container_health(self):
        result,calls=self.run_control('start')
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertTrue(any(call[-2:]==['start','kafka'] for call in calls))
    def test_production_endpoint_or_project_is_rejected(self):
        for values in [{'RT_KAFKA_TEST_ENDPOINT':'10.20.10.4:9092'},{'RT_KAFKA_TEST_PROJECT':'researchtrack-production'}]:
            with self.subTest(values=values):self.assert_no_mutation(*self.run_control(**values))
    def test_wrong_container_identity_is_rejected(self):
        self.assert_no_mutation(*self.run_control(RT_KAFKA_TEST_BROKER_ID='other-container'))
    def test_missing_fixture_label_is_rejected(self):
        self.container['Config']['Labels']['researchtrack.fixture']='production'
        self.assert_no_mutation(*self.run_control())
    def test_wrong_mount_or_public_port_is_rejected(self):
        for field in ['mount','port']:
            with self.subTest(field=field):
                if field=='mount':self.container['Mounts'][0]['Source']='/data/researchtrack/kafka/data'
                else:self.container['HostConfig']['PortBindings']['9092/tcp'][0]['HostIp']='0.0.0.0'
                self.assert_no_mutation(*self.run_control())
                self.container['Mounts'][0]['Source']=str(self.work)+'/kafka-data'
    def test_unsupported_operation_is_rejected(self):
        for action in ['down','restart','delete','reset-offsets']:
            with self.subTest(action=action):self.assert_no_mutation(*self.run_control(action))

if __name__ == '__main__':
    unittest.main()
