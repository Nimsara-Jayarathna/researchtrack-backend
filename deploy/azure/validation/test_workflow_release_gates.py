#!/usr/bin/env python3
"""Offline graph/security checks and real workflow shell bodies with stub CLIs."""
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
WORKFLOWS = ROOT / '.github/workflows'
spec = importlib.util.spec_from_file_location('policy', ROOT / 'deploy/azure/scripts/release-policy.py')
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)


def job_blocks(source):
    parts = re.split(r'^  ([\w-]+):\n', source.split('\njobs:\n', 1)[1], flags=re.M)
    return dict(zip(parts[1::2], parts[2::2]))


def dependencies(block):
    match = re.search(r'^    needs: (.+)$', block, re.M)
    return [item.strip() for item in match[1].strip('[]').split(',')] if match else []


def step(source, name):
    return source.split('      - name: ' + name + '\n', 1)[1].split('      - ', 1)[0]


def body(block):
    return '\n'.join(line[10:] for line in block.split('        run: |\n', 1)[1].splitlines()) + '\n'


class InfrastructureGate(unittest.TestCase):
    def setUp(self):
        self.source = (WORKFLOWS / 'azure-infrastructure.yml').read_text()
        self.jobs = job_blocks(self.source)

    def test_unprivileged_required_ci_precedes_bicep_and_production(self):
        gate = self.jobs['eligibility']
        self.assertIn('uses: ./.github/workflows/backend-release-validation.yml', gate)
        self.assertIn("github.event_name == 'push' || github.event_name == 'workflow_dispatch'", gate)
        self.assertIn("github.ref == 'refs/heads/" + policy.BRANCH + "'", gate)
        for privileged in ['secrets:', 'environment:', 'id-token:', 'packages:']:
            self.assertNotIn(privileged, gate)
        self.assertEqual(['eligibility'], dependencies(self.jobs['validate']))
        self.assertEqual(['eligibility', 'validate'], dependencies(self.jobs['deploy']))
        for block in [self.jobs['validate'], self.jobs['deploy']]:
            self.assertNotIn('always()', block.split('    steps:', 1)[0])
            self.assertNotIn('continue-on-error:', block)
        self.assertIn('environment: production', self.jobs['deploy'])
        self.assertIn('id-token: write', self.jobs['deploy'])

    def test_shared_policy_blocks_failed_missing_skipped_stale_and_untrusted_source(self):
        context = dict(CI_RESULT='success', SOURCE_SHA='a' * 40, SOURCE_REF='refs/heads/' + policy.BRANCH,
                       SOURCE_REPOSITORY=policy.REPOSITORY, SOURCE_EVENT='workflow_dispatch')
        remote = dict(name=policy.BRANCH, commit=dict(sha='a' * 40))
        self.assertEqual('a' * 40, policy.verify(context, remote))
        overrides = [dict(CI_RESULT=result) for result in ['failure', 'cancelled', 'skipped', '', 'neutral', None]]
        overrides += [dict(SOURCE_REF='refs/heads/main'), dict(SOURCE_EVENT='pull_request'),
                      dict(SOURCE_EVENT='pull_request_target'), dict(SOURCE_SHA='b' * 40)]
        for override in overrides:
            with self.subTest(override=override), self.assertRaises(ValueError):
                policy.verify(dict(context, **override), remote)

    def test_exact_sha_is_rechecked_before_any_credentials(self):
        deploy = self.jobs['deploy']
        self.assertEqual(2, self.source.count('ref: ${{ github.sha }}'))
        self.assertEqual(2, self.source.count('persist-credentials: false'))
        check = step(self.source, 'Recheck exact validated infrastructure source before production access')
        for expression in ['CI_RESULT: ${{ needs.eligibility.result }}', 'SOURCE_SHA: ${{ github.sha }}',
                           'SOURCE_EVENT: ${{ github.event_name }}', 'SOURCE_REF: ${{ github.ref }}',
                           'SOURCE_REPOSITORY: ${{ github.repository }}']:
            self.assertIn(expression, check)
        self.assertIn('run: python3 deploy/azure/scripts/release-policy.py', check)
        self.assertLess(deploy.index('Recheck exact validated infrastructure source'), deploy.index('secrets.AZURE_CLIENT_ID'))
        self.assertLess(deploy.index('Recheck exact validated infrastructure source'), deploy.index('Materialize and validate VM runtime'))

    def test_bicep_failure_stops_shell_and_deployment_has_no_bypass(self):
        with tempfile.TemporaryDirectory(prefix='rt-bicep-gate-test-') as directory:
            work = Path(directory)
            az = work / 'az'
            az.write_text('#!/bin/sh\necho called >> "$CALLS"\nexit 1\n')
            az.chmod(0o755)
            result = subprocess.run(['bash', '-e', '-c', body(step(self.source, 'Build Bicep'))],
                                    env=dict(os.environ, PATH=directory + ':' + os.environ['PATH'], CALLS=str(work / 'calls')),
                                    capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertEqual(['called'], (work / 'calls').read_text().splitlines())
        self.assertIn('validate', dependencies(self.jobs['deploy']))

    def test_monitoring_manual_inputs_whatif_and_lock_preserved(self):
        for value in ['monitoring_only:', 'allow_destructive:', 'group: researchtrack-azure-production',
                      'cancel-in-progress: false', 'queue: max', 'run: ./deploy/azure/scripts/whatif-gate.sh']:
            self.assertIn(value, self.source)
        self.assertIn('if: ${{ !inputs.monitoring_only }}', step(self.source, 'What-If'))
        self.assertIn('operation=(show)', step(self.source, 'Read existing infrastructure outputs or deploy Bicep'))

    def test_real_whatif_deletion_gate_and_explicit_manual_override(self):
        with tempfile.TemporaryDirectory(prefix='rt-whatif-test-') as directory:
            work = Path(directory)
            az = work / 'az'
            az.write_text('#!/bin/sh\necho "$*" >> "$CALLS"\ncat "$PLAN"\nexit "$CLI_EXIT"\n')
            az.chmod(0o755)
            for delete, allow, cli_exit, expected in [(False, 'false', '0', 0), (True, 'false', '0', 1),
                                                       (True, 'true', '0', 0), (False, 'false', '5', 5)]:
                with self.subTest(delete=delete, allow=allow, cli_exit=cli_exit):
                    (work / 'plan.json').write_text(json.dumps({'changes': [{'changeType': 'Delete' if delete else 'NoChange', 'resourceId': '/synthetic/resource'}]}))
                    (work / 'calls').write_text('')
                    result = subprocess.run(['bash', str(ROOT / 'deploy/azure/scripts/whatif-gate.sh')],
                        env=dict(os.environ, PATH=directory + ':' + os.environ['PATH'], PLAN=str(work / 'plan.json'),
                                 CALLS=str(work / 'calls'), CLI_EXIT=cli_exit, ALLOW_DESTRUCTIVE=allow,
                                 AZURE_LOCATION='synthetic-region', GITHUB_STEP_SUMMARY=str(work / 'summary')),
                        capture_output=True, text=True)
                    self.assertEqual(expected, result.returncode, result.stderr)
                    calls = (work / 'calls').read_text().splitlines()
                    self.assertEqual(1, len(calls))
                    self.assertTrue(calls[0].startswith('deployment sub what-if '))

    def test_reusable_graph_is_acyclic_and_within_supported_depth(self):
        def visit(path, ancestors):
            self.assertNotIn(path, ancestors)
            self.assertLessEqual(len(ancestors) + 1, 10)
            source = path.read_text()
            for child in re.findall(r'uses: \./\.github/workflows/([\w-]+\.yml)', source):
                visit(WORKFLOWS / child, ancestors + [path])
        visit(WORKFLOWS / 'azure-infrastructure.yml', [])
        visit(WORKFLOWS / 'backend-deploy-production.yml', [])


class PublicReadiness(unittest.TestCase):
    def run_check(self, enabled, failure=False, status='200'):
        source = (WORKFLOWS / 'backend-deploy-azure-production.yml').read_text()
        block = step(source, 'Verify public API endpoint')
        self.assertIn("if: vars.PRODUCTION_VERIFY_PUBLIC_ENDPOINT == 'true'", block)
        with tempfile.TemporaryDirectory(prefix='rt-public-ready-test-') as directory:
            work = Path(directory)
            for name, script in [('curl', '#!/bin/sh\nprintf "%s\\n" "$*" >> "$CALLS"\nprintf "%s" "$STATUS"\nexit "$FAIL"\n'),
                                 ('sleep', '#!/bin/sh\nexit 0\n')]:
                path = work / name
                path.write_text(script)
                path.chmod(0o755)
            # GitHub's optional condition is exact-string true; execute the
            # unchanged workflow body when enabled, with no network requests.
            result = subprocess.run(['bash', '-e', '-c', body(block) if enabled == 'true' else ':'],
                                    env=dict(os.environ, PATH=directory + ':' + os.environ['PATH'],
                                             API_HOSTNAME='api.example.test', CALLS=str(work / 'calls'), FAIL='1' if failure else '0', STATUS=status),
                                    capture_output=True, text=True)
            calls = (work / 'calls').read_text().splitlines() if (work / 'calls').exists() else []
            for call in calls:
                self.assertEqual('-fsS --max-time 10 --output /dev/null --write-out %{http_code} https://api.example.test/health/ready', call)
            return result, calls

    def test_enabled_uses_verified_https_readonly_get_without_credentials(self):
        result, calls = self.run_check('true')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(1, len(calls))

    def test_disabled_unset_or_other_values_do_not_request(self):
        for enabled in ['false', '', 'True']:
            result, calls = self.run_check(enabled)
            self.assertEqual(0, result.returncode)
            self.assertEqual([], calls)

    def test_failures_are_bounded_and_fail_the_step(self):
        result, calls = self.run_check('true', failure=True)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(10, len(calls))
        self.assertIn('did not return HTTP 200', result.stderr)

    def test_redirect_or_other_success_code_cannot_impersonate_readiness(self):
        for status in ['301', '302', '204', '503']:
            with self.subTest(status=status):
                result, calls = self.run_check('true', status=status)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual(10, len(calls))


class RestorationSafeguards(unittest.TestCase):
    def exercise(self, workflow, name, fail='', owned=True):
        block = step((WORKFLOWS / workflow).read_text(), name)
        self.assertIn('if: always()', block)
        with tempfile.TemporaryDirectory(prefix='rt-restoration-test-') as directory:
            work = Path(directory)
            for path in ['deploy/azure/scripts/runtime-power.sh', 'scripts/performance/gateway-exemption.sh']:
                target = work / path
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text('#!/bin/sh\necho "$1" >> "$CALLS"\n[ "$1" != "$FAIL" ]\n')
                target.chmod(0o755)
            (work / 'snapshot.json').write_text('{}')
            if owned:
                (work / 'active').touch()
            (work / 'bin').mkdir()
            (work / 'bin/sleep').write_text('#!/bin/sh\nexit 0\n')
            (work / 'bin/sleep').chmod(0o755)
            snapshot, active = str(work / 'snapshot.json'), str(work / 'active')
            env = dict(os.environ, PATH=str(work / 'bin') + ':' + os.environ['PATH'], CALLS=str(work / 'calls'), FAIL=fail,
                       RUNTIME_STATE_FILE=snapshot, RUNTIME_ACTIVE_FILE=active, STATE_FILE=snapshot, ACTIVE_FILE=active,
                       K6_GATEWAY_STATE_FILE=snapshot, K6_GATEWAY_ACTIVE_FILE=active, K6_RATE_LIMIT_BYPASS_ENABLED='true',
                       GITHUB_RUN_ID='123', GITHUB_RUN_ATTEMPT='1')
            result = subprocess.run(['bash', '-e', '-c', body(block)], cwd=work, env=env, capture_output=True, text=True)
            calls = (work / 'calls').read_text().splitlines()
            return result, calls, (work / 'active').exists()

    def test_application_restores_and_verifies_owned_runtime(self):
        result, calls, active = self.exercise('backend-deploy-azure-production.yml', 'Restore exact pre-deployment runtime state')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(['restore', 'verify', 'status'], calls)
        self.assertFalse(active)

    def test_application_restoration_failure_is_bounded_fatal_and_keeps_evidence(self):
        for fail in ['restore', 'verify']:
            result, calls, active = self.exercise('backend-deploy-azure-production.yml', 'Restore exact pre-deployment runtime state', fail)
            self.assertNotEqual(0, result.returncode)
            self.assertEqual(3, calls.count('restore'))
            self.assertTrue(active)
            self.assertIn('CRITICAL', result.stderr)

    def test_unowned_drift_cannot_be_overwritten(self):
        result, calls, _ = self.exercise('backend-deploy-azure-production.yml', 'Restore exact pre-deployment runtime state', 'verify', owned=False)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(['verify'], calls)
        self.assertIn('refusing to overwrite', result.stderr)

    def test_smoke_runtime_restoration_failure_remains_fatal(self):
        result, calls, active = self.exercise('azure-performance-k6-reusable.yml', 'Final restoration safeguard', 'restore')
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(3, calls.count('restore'))
        self.assertTrue(active)
        self.assertIn('CRITICAL', result.stderr)

    def test_smoke_gateway_restoration_failure_remains_fatal(self):
        result, calls, active = self.exercise('azure-performance-k6-reusable.yml', 'Final Gateway configuration restoration safeguard', 'restore')
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(3, calls.count('restore'))
        self.assertTrue(active)
        self.assertIn('CRITICAL', result.stderr)


if __name__ == '__main__':
    unittest.main()
