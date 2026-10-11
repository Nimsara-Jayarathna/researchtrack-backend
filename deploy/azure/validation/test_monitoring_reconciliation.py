#!/usr/bin/env python3
"""Exercise real monitoring scripts against a disposable filesystem and stub Docker."""
import base64
import io
import os
import json
import hashlib
import re
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]

class MonitoringReconciliation(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='rt-monitoring-reconcile-test-')
        self.work = Path(self.tmp.name)
        self.opt = self.work / 'opt'
        for directory in ['runtime', 'prometheus/rules', 'scripts']:
            (self.opt / directory).mkdir(parents=True, exist_ok=True)
        (self.opt / 'runtime/infra.env').write_text('ACA_ENV_DOMAIN=fixture.internal.example.test\nACA_TLS_VERIFY=on\n')
        (self.opt / 'compose.yml').write_text('services: {}\n')
        (self.opt / 'prometheus/prometheus.yml.template').write_text((ROOT / 'deploy/azure/vm/prometheus/prometheus.yml.template').read_text())
        self.script = self.work / 'reconcile.sh'
        self.script.write_text((ROOT / 'deploy/azure/vm/scripts/reconcile-monitoring.sh').read_text().replace('/opt/researchtrack', str(self.opt)))
        self.log = self.work / 'calls'
        self.bin = self.work / 'bin'
        self.bin.mkdir()
        self.executable(self.opt / 'scripts/compose.sh', '''#!/usr/bin/env bash
printf 'compose %s\\n' "$*" >> "$CALLS"
if [[ "$1" == ps ]]; then printf '%s-id\\n' "$3"; exit 0; fi
if [[ "$1" == config && "${FAIL_CONFIG:-0}" == 1 ]]; then exit 1; fi
''')
        self.executable(self.bin / 'docker', '''#!/usr/bin/env bash
printf 'docker %s\\n' "$*" >> "$CALLS"
if [[ "$1" == inspect ]]; then
  if [[ "$3" == '{{.Image}}' ]]; then printf 'sha256:%064d\\n' 1
  else
    state=unchanged
    marker="$CALLS.kafka-inspected"
    if [[ "$4" == kafka-id && "${MUTATE:-0}" == 1 ]]; then
      [[ -f "$marker" ]] && state=changed
      touch "$marker"
    fi
    printf '%s %s\\n' "$4" "$state"
  fi
elif [[ "$1" == run && "${FAIL_PROMTOOL:-0}" == 1 ]]; then exit 1
fi
''')
        self.env = dict(os.environ, PATH=str(self.bin) + ':' + os.environ['PATH'], CALLS=str(self.log))
    def tearDown(self):
        self.tmp.cleanup()
    def executable(self, path, text):
        path.write_text(text)
        path.chmod(0o755)
    def run_script(self, **env):
        return subprocess.run(['bash', str(self.script)], env=dict(self.env, **env), capture_output=True, text=True)
    def test_only_monitoring_containers_change(self):
        result = self.run_script()
        self.assertEqual(0, result.returncode, result.stderr)
        calls = self.log.read_text()
        self.assertIn('up -d --no-deps kafka-exporter', calls)
        self.assertIn('up -d --no-deps --force-recreate --pull never prometheus grafana', calls)
        self.assertIn('check config /etc/prometheus/prometheus.yml', calls)
        for line in calls.splitlines():
            if line.startswith('compose ') and any(x in line.split() for x in ['up', 'pull', 'stop', 'down', 'restart']):
                self.assertFalse(any(x in line.split() for x in ['kafka', 'mysql', 'nginx']), line)
        self.assertNotIn('prune', calls)
        self.assertIn('insecure_skip_verify: false', (self.opt / 'prometheus/prometheus.yml').read_text())
        self.assertFalse(list(self.opt.glob('.monitoring-*')))
    def test_invalid_compose_prevents_changes(self):
        self.assertNotEqual(0, self.run_script(FAIL_CONFIG='1').returncode)
        self.assertNotIn('up -d', self.log.read_text())
    def test_invalid_rules_prevent_changes(self):
        self.assertNotEqual(0, self.run_script(FAIL_PROMTOOL='1').returncode)
        self.assertNotIn('up -d', self.log.read_text())
        self.assertFalse((self.opt / 'prometheus/prometheus.yml').exists())
    def test_changed_broker_identity_is_rejected(self):
        result = self.run_script(MUTATE='1')
        self.assertNotEqual(0, result.returncode)
        self.assertIn('identity/start time changed', result.stderr)
    def test_invalid_hostname_prevents_docker_calls(self):
        (self.opt / 'runtime/infra.env').write_text('ACA_ENV_DOMAIN=invalid/hostname\nACA_TLS_VERIFY=on\n')
        self.assertNotEqual(0, self.run_script().returncode)
        self.assertFalse(self.log.exists())
    def test_monitoring_bundle_needs_no_runtime_credentials(self):
        out = self.work / 'bundle'
        env = dict(os.environ, INFRA_PRIVATE_IP='10.20.10.4', API_HOSTNAME='api.example.test', GRAFANA_HOSTNAME='grafana.example.test', ACA_ENV_DOMAIN='fixture.internal.example.test', RT_RECONCILE_MODE='monitoring')
        result = subprocess.run(['bash', str(ROOT / 'deploy/azure/scripts/build-vm-bundle.sh'), '/nonexistent-synthetic-env', str(out)], env=env, capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse((out / 'runtime.b64').exists())
        script = (out / 'reconcile-vm.sh').read_text()
        self.assertNotIn('RT_RUNTIME_B64', script)
        self.assertNotIn('"$opt/scripts/reconcile-stack.sh"', script)
        encoded = script.splitlines()[0].split('=', 1)[1]
        with tarfile.open(fileobj=io.BytesIO(base64.b64decode(encoded))) as bundle:
            names = bundle.getnames()
            for name in ['./scripts/reconcile-monitoring.sh', './prometheus/rules/researchtrack-alerts.yml', './grafana/provisioning/dashboards/json/kafka-observability.json']:
                self.assertIn(name, names)
                self.assertEqual(0o755 if '/scripts/' in name else 0o644, bundle.getmember(name).mode)
    def test_workflow_preserves_automatic_triggers_and_scopes_manual_mode(self):
        workflow = (ROOT / '.github/workflows/azure-infrastructure.yml').read_text()
        push = workflow.split('  push:', 1)[1].split('  workflow_dispatch:', 1)[0]
        self.assertEqual("f4b67a82f63a5ab2de9cc6c145d134c8b5ca27f794f17082add0af2f95209f49", hashlib.sha256(push.encode()).hexdigest())
        for name in ['Materialize and validate VM runtime configuration', 'Configure VM (OS, Docker, data disk)', 'What-If', 'Validate private networking (VM side)', 'Validate private networking (Container Apps side)']:
            self.assertIn('      - name: ' + name + '\n        if: ${{ !inputs.monitoring_only }}', workflow)
        self.assertIn('"MONITORING_ONLY=$MONITORING_ONLY"', workflow)
        self.assertIn('inputs.monitoring_only || false', workflow)
        self.assertIn('Require an already-running VM', workflow)
    def test_workflow_reads_outputs_without_deploying_bicep_in_monitoring_mode(self):
        self.check_workflow_output_step('true', 'show')
    def test_workflow_preserves_normal_bicep_deployment(self):
        self.check_workflow_output_step('false', 'create')
    def check_workflow_output_step(self, mode, operation):
        workflow = (ROOT / '.github/workflows/azure-infrastructure.yml').read_text()
        step = workflow.split('      - name: Read existing infrastructure outputs or deploy Bicep', 1)[1].split('      - name:', 1)[0]
        body = step.split('        run: |\n', 1)[1]
        script = self.work / 'outputs.sh'
        script.write_text(''.join(line[10:] + '\n' for line in body.splitlines()))
        names = ['resourceGroupName', 'vmName', 'vmPrivateIp', 'containerAppsEnvironmentName', 'containerAppsDefaultDomain', 'containerAppsStaticIp', 'publicIpAddress']
        data = json.dumps({key: {'value': 'synthetic-example'} for key in names})
        self.executable(self.bin / 'az', '#!/usr/bin/env bash\nprintf \'az %s\\n\' "$*" >> "$CALLS"\nif [[ "$1" == deployment ]]; then cat <<\'JSON\'\n' + data + '\nJSON\nfi\n')
        env = dict(self.env, MONITORING_ONLY=mode, AZURE_LOCATION='synthetic-region', GITHUB_ENV=str(self.work / 'github.env'), GITHUB_STEP_SUMMARY=str(self.work / 'summary'), API_HOSTNAME='api.example.test', GRAFANA_HOSTNAME='grafana.example.test')
        result = subprocess.run(['bash', str(script)], env=env, capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stderr)
        calls = self.log.read_text()
        self.assertIn('az deployment sub ' + operation, calls)
        if mode == 'true':
            self.assertNotIn('deployment sub create', calls)
            self.assertNotIn('--parameters', calls)
        else:
            self.assertIn('--parameters deploy/azure/parameters/production.bicepparam', calls)

    def test_invalid_bundle_mode_is_rejected(self):
        result = subprocess.run(['bash', str(ROOT / 'deploy/azure/scripts/build-vm-bundle.sh'), '/nonexistent', str(self.work / 'bundle')], env=dict(os.environ, RT_RECONCILE_MODE='invalid'), capture_output=True, text=True)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('must be stack or monitoring', result.stderr)

if __name__ == '__main__':
    unittest.main()
