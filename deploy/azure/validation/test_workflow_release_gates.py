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
        overrides += [dict(SOURCE_REF='refs/heads/integration/main-kafka-production-20261011'), dict(SOURCE_EVENT='pull_request'),
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


class ProductionOperationGate(unittest.TestCase):
    def test_untrusted_context_cannot_enter_operation_environment(self):
        # Evaluate the actual guards over their small boolean expression subset.
        from types import SimpleNamespace
        for filename, job in [('azure-runtime-power.yml','runtime'), ('azure-runtime-session.yml','session'),
                              ('azure-performance-k6-reusable.yml','performance')]:
            block = job_blocks((WORKFLOWS / filename).read_text())[job]
            guard = re.search(r'^    if: \$\{\{ (.+) \}\}$', block, re.M)[1]
            expression = guard.replace('&&', 'and').replace('||', 'or')
            cases = [(policy.REPOSITORY, 'workflow_dispatch', 'refs/heads/main', True),
                     (policy.REPOSITORY, 'workflow_dispatch', 'refs/heads/integration/main-kafka-production-20261011', False),
                     ('fork/researchtrack-backend', 'workflow_dispatch', 'refs/heads/' + policy.BRANCH, False),
                     (policy.REPOSITORY, 'pull_request', 'refs/pull/1/merge', False),
                     (policy.REPOSITORY, 'pull_request_target', 'refs/heads/' + policy.BRANCH, False),
                     (policy.REPOSITORY, 'workflow_run', 'refs/heads/' + policy.BRANCH, False),
                     (policy.REPOSITORY, 'workflow_dispatch', 'refs/heads/develop', False),
                     (policy.REPOSITORY, 'workflow_dispatch', 'refs/tags/main', False),
                     (policy.REPOSITORY, 'push', 'refs/heads/integration/main-kafka-production-20261011', False),
                     (policy.REPOSITORY, 'push', 'refs/heads/' + policy.BRANCH,
                      filename == 'azure-performance-k6-reusable.yml')]
            for repository, event, ref, allowed in cases:
                with self.subTest(filename=filename, repository=repository, event=event, ref=ref):
                    github = SimpleNamespace(repository=repository, event_name=event, ref=ref)
                    self.assertEqual(allowed, eval(expression, {'__builtins__': {}}, {'github': github}))

    def test_operation_checkouts_and_source_gate_precede_credentials(self):
        for filename in ['azure-runtime-power.yml', 'azure-runtime-session.yml', 'azure-performance-k6-reusable.yml']:
            with self.subTest(filename=filename):
                source = (WORKFLOWS / filename).read_text()
                self.assertIn('ref: ${{ github.sha }}', source)
                self.assertIn('persist-credentials: false', source)
                gate = step(source, 'Authorize exact production operation source before credentials')
                for field in ['SHA', 'REF', 'REPOSITORY', 'EVENT']:
                    expression = dict(SHA='sha', REF='ref', REPOSITORY='repository', EVENT='event_name')[field]
                    self.assertIn('SOURCE_' + field + ': ${{ github.' + expression + ' }}', gate)
                self.assertLess(source.index('Authorize exact production operation'), source.index('secrets.AZURE_CLIENT_ID'))
                self.assertLess(source.index('Authorize exact production operation'), source.index('uses: azure/login@v2'))
                self.assertIn('environment: production', source)
                self.assertNotIn('continue-on-error:', source)
                if filename != 'azure-performance-k6-reusable.yml':
                    self.assertIn('run: python3 deploy/azure/scripts/release-policy.py --operation', gate)

    def test_automatic_smoke_receives_successful_deployment_result(self):
        entry = (WORKFLOWS / 'backend-deploy-production.yml').read_text()
        caller = job_blocks(entry)['performance_smoke']
        self.assertEqual(['deploy'], dependencies(caller))
        self.assertIn("needs.deploy.result == 'success'", caller)
        self.assertIn('deployment_result: ${{ needs.deploy.result }}', caller)
        reusable = (WORKFLOWS / 'azure-performance-k6-reusable.yml').read_text()
        self.assertIn("        default: ''", reusable.split('      deployment_result:', 1)[1].split('      confirmation:', 1)[0])
        self.assertIn('CI_RESULT: ${{ inputs.deployment_result }}', reusable)

    def test_real_smoke_source_step_selects_release_or_manual_policy_and_fails_closed(self):
        source = (WORKFLOWS / 'azure-performance-k6-reusable.yml').read_text()
        script = body(step(source, 'Authorize exact production operation source before credentials'))
        with tempfile.TemporaryDirectory() as directory:
            work = Path(directory)
            stub = work / 'python3'
            stub.write_text('#!/bin/sh\nprintf "%s\n" "$*" >> "$CALLS"\nexit "$POLICY_RESULT"\n')
            stub.chmod(0o755)
            for mode, policy_result, expected in [('manual','0',0), ('automatic-post-deploy','0',0),
                    ('manual','1',1), ('automatic-post-deploy','1',1), ('invalid','0',1)]:
                with self.subTest(mode=mode, policy_result=policy_result):
                    calls = work / 'calls'
                    calls.write_text('')
                    result = subprocess.run(['bash', '-e', '-c', script], capture_output=True, text=True,
                        env=dict(os.environ, PATH=directory + ':' + os.environ['PATH'], CALLS=str(calls),
                                 K6_EXECUTION_MODE=mode, POLICY_RESULT=policy_result))
                    self.assertEqual(expected, result.returncode, result.stderr)
                    if mode == 'invalid':
                        self.assertEqual('', calls.read_text())
                    else:
                        self.assertEqual('deploy/azure/scripts/release-policy.py' +
                            (' --operation' if mode == 'manual' else '') + '\n', calls.read_text())

    def test_required_ci_and_kafka_have_no_skipping_or_privileged_context(self):
        shared = job_blocks((WORKFLOWS / 'backend-release-validation.yml').read_text())
        ci = job_blocks((WORKFLOWS / 'backend-ci.yml').read_text())
        kafka = job_blocks((WORKFLOWS / 'kafka-integration-tests.yml').read_text())
        for block in [shared['ci'], shared['eligibility'], ci['test'], ci['kafka-integration'], kafka['kafka-integration']]:
            header = block.split('    steps:', 1)[0]
            self.assertNotIn('    if:', header)
            for forbidden in ['secrets:', 'environment:', 'id-token:', 'packages:', 'continue-on-error:']:
                self.assertNotIn(forbidden, block)
        self.assertEqual(['ci'], dependencies(shared['eligibility']))
        self.assertIn('bash scripts/test-kafka-integration.sh', kafka['kafka-integration'])
        runner = (ROOT / 'scripts/test-kafka-integration.sh').read_text()
        self.assertIn("int(counters.get('passed')) == count and int(counters.get('executed')) == count", runner)
        self.assertIn("[('GitHub',3),('Jira',2)]", runner)

    def test_every_production_mutating_entry_uses_the_same_non_cancelling_lock(self):
        for filename in ['backend-deploy-production.yml','azure-infrastructure.yml',
                         'azure-runtime-power.yml','azure-runtime-session.yml','azure-performance-k6.yml']:
            with self.subTest(filename=filename):
                source = (WORKFLOWS / filename).read_text()
                self.assertIn('group: researchtrack-azure-production', source)
                self.assertIn('cancel-in-progress: false', source)
                self.assertIn('queue: max', source)


class VpsTargetSafety(unittest.TestCase):
    def setUp(self):
        self.source = (WORKFLOWS / 'backend-deploy-reusable.yml').read_text()
        self.jobs = job_blocks(self.source)
        self.context = dict(DEPLOY_ENVIRONMENT='test', SOURCE_REPOSITORY=policy.REPOSITORY,
                            SOURCE_EVENT='push', SOURCE_REF='refs/heads/develop', SOURCE_SHA='a' * 40)

    def validate(self, **overrides):
        return subprocess.run(['bash', '-e', '-c', body(step(self.jobs['validate-target'],
                'Reject production and untrusted VPS deployment requests'))],
            env=dict(os.environ, **dict(self.context, **overrides)), capture_output=True, text=True)

    def test_production_unknown_and_empty_targets_fail_before_credentials(self):
        for environment in ['production', 'Production', '', 'staging', 'test; echo injected']:
            with self.subTest(environment=environment):
                result = self.validate(DEPLOY_ENVIRONMENT=environment)
                self.assertNotEqual(0, result.returncode)
                self.assertIn('supports only Test', result.stderr)
                self.assertNotIn('echo injected', result.stderr)

    def test_develop_push_and_existing_trusted_manual_test_refs_remain_available(self):
        self.assertEqual(0, self.validate().returncode)
        for ref in ['refs/heads/develop', 'refs/heads/main', 'refs/heads/' + policy.BRANCH,
                    'refs/heads/operator-test-fix', 'refs/tags/operator-test-tag']:
            with self.subTest(ref=ref):
                result = self.validate(SOURCE_EVENT='workflow_dispatch', SOURCE_REF=ref)
                self.assertEqual(0, result.returncode, result.stderr)

    def test_forks_prs_wrong_push_branches_and_malformed_shas_fail(self):
        overrides = [dict(SOURCE_REPOSITORY='fork/researchtrack-backend'), dict(SOURCE_REPOSITORY=''),
                     dict(SOURCE_REF='refs/heads/main'), dict(SOURCE_REF='refs/heads/' + policy.BRANCH),
                     dict(SOURCE_SHA='develop'), dict(SOURCE_SHA='')]
        overrides += [dict(SOURCE_EVENT=event) for event in ['pull_request','pull_request_target','workflow_run','repository_dispatch','']]
        for override in overrides:
            with self.subTest(override=override):
                self.assertNotEqual(0, self.validate(**override).returncode)

    def test_target_authorization_precedes_build_and_static_test_environment(self):
        gate = self.jobs['validate-target']
        self.assertIn('permissions: {}', gate)
        for privileged in ['secrets:', 'environment:', 'id-token:', 'uses: actions/checkout']:
            self.assertNotIn(privileged, gate)
        self.assertEqual(['validate-target'], dependencies(self.jobs['build']))
        self.assertEqual(['build','validate-target'], dependencies(self.jobs['deploy']))
        self.assertIn("needs.validate-target.result == 'success'", self.jobs['deploy'])
        self.assertIn("needs.build.result == 'success'", self.jobs['deploy'])
        self.assertIn('environment: test', self.jobs['deploy'])
        self.assertNotIn('environment: ${{ inputs.environment_name }}', self.source)
        self.assertNotIn('id-token:', self.source)
        self.assertNotIn('azure/login', self.source)
        self.assertNotIn('continue-on-error:', self.source)
        for name in ['build','deploy']:
            self.assertNotIn('always()', self.jobs[name].split('    steps:',1)[0])

    def test_only_test_caller_uses_vps_and_azure_release_route_is_preserved(self):
        callers = [path.name for path in WORKFLOWS.glob('*.yml')
                   if 'uses: ./.github/workflows/backend-deploy-reusable.yml' in path.read_text()]
        self.assertEqual(['backend-deploy-test.yml'], callers)
        test = (WORKFLOWS / callers[0]).read_text()
        self.assertIn('branches: ["develop"]', test)
        self.assertIn('workflow_dispatch:', test)
        self.assertIn('environment_name: test', test)
        self.assertIn('group: researchtrack-backend-test-deployment', test)
        azure = (WORKFLOWS / 'backend-deploy-production.yml').read_text()
        self.assertIn('uses: ./.github/workflows/backend-deploy-azure-production.yml', azure)
        self.assertIn('branches: ["' + policy.BRANCH + '"]', azure)
        self.assertIn('group: researchtrack-azure-production', azure)
        self.assertNotIn('uses: ./.github/workflows/backend-deploy-reusable.yml', azure)


class PublicReadiness(unittest.TestCase):
    def run_check(self, enabled, failure=False, status='200', exit_code=None, responses=None):
        source = (WORKFLOWS / 'backend-deploy-azure-production.yml').read_text()
        block = step(source, 'Verify public API endpoint')
        self.assertIn("if: vars.PRODUCTION_VERIFY_PUBLIC_ENDPOINT == 'true'", block)
        with tempfile.TemporaryDirectory(prefix='rt-public-ready-test-') as directory:
            work = Path(directory)
            # Each synthetic response supplies the HTTP status and curl exit
            # code independently. Exhausted sequences repeat their last result.
            (work / 'responses').write_text(''.join(
                f'{code} {result}\n' for code, result in
                (responses or [(status, exit_code if exit_code is not None else int(failure))])))
            for name, script in [('curl', '''#!/bin/sh
printf "%s\\n" "$*" >> "$CALLS"
response=$(sed -n "$(wc -l < "$CALLS")p" "$RESPONSES")
[ -n "$response" ] || response=$(tail -n 1 "$RESPONSES")
set -- $response
printf "%s" "$1"
exit "$2"
'''), ('sleep', '#!/bin/sh\nprintf "%s\\n" "$*" >> "$SLEEPS"\n')]:
                path = work / name
                path.write_text(script)
                path.chmod(0o755)
            # Actions string equality ignores case. Variables arrive as strings;
            # execute the unchanged body when enabled, without network requests.
            result = subprocess.run(['bash', '-e', '-c', body(block) if enabled.casefold() == 'true' else ':'],
                                    env=dict(os.environ, PATH=directory + ':' + os.environ['PATH'],
                                             API_HOSTNAME='api.example.test', CALLS=str(work / 'calls'),
                                             RESPONSES=str(work / 'responses'), SLEEPS=str(work / 'sleeps')),
                                    capture_output=True, text=True, timeout=10)
            calls = (work / 'calls').read_text().splitlines() if (work / 'calls').exists() else []
            for call in calls:
                self.assertEqual('-fsS --max-time 10 --output /dev/null --write-out %{http_code} https://api.example.test/health/ready', call)
            sleeps = (work / 'sleeps').read_text().splitlines() if (work / 'sleeps').exists() else []
            self.assertEqual(['6'] * (len(calls) - int(bool(calls) and result.returncode == 0)), sleeps)
            return result, calls

    def test_enabled_uses_verified_https_readonly_get_without_credentials(self):
        result, calls = self.run_check('true')
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(1, len(calls))

    def test_disabled_unset_or_other_values_do_not_request(self):
        for enabled in ['false', 'FALSE', '', '1', 'yes', ' true', 'true ']:
            result, calls = self.run_check(enabled)
            self.assertEqual(0, result.returncode)
            self.assertEqual([], calls)

    def test_flag_comparison_matches_actions_case_insensitive_string_equality(self):
        for enabled in ['True', 'TRUE', 'tRuE']:
            with self.subTest(enabled=enabled):
                result, calls = self.run_check(enabled)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(1, len(calls))

    def test_failures_are_bounded_and_fail_the_step(self):
        result, calls = self.run_check('true', failure=True)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(10, len(calls))
        self.assertIn('did not return HTTP 200', result.stderr)

    def test_redirect_or_other_success_code_cannot_impersonate_readiness(self):
        for status in ['201', '204', '301', '302', '303', '307', '308',
                       '401', '403', '404', '429', '500', '502', '503', '504', '599']:
            with self.subTest(status=status):
                result, calls = self.run_check('true', status=status)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual(10, len(calls))

    def test_dns_timeout_and_certificate_failures_cannot_pass_even_with_status_200(self):
        for exit_code in [6, 7, 22, 28, 35, 60]:
            with self.subTest(exit_code=exit_code):
                result, calls = self.run_check('true', status='200', exit_code=exit_code)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual(10, len(calls))

    def test_transient_failure_recovers_only_on_successful_exact_200(self):
        result, calls = self.run_check('true', responses=[('000', 6), ('503', 22), ('302', 0), ('200', 0)])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(4, len(calls))

    def test_final_attempt_can_succeed_without_an_extra_retry_or_sleep(self):
        result, calls = self.run_check('true', responses=[('503', 22)] * 9 + [('200', 0)])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(10, len(calls))


class WorkflowToolingOutputs(unittest.TestCase):
    def validate_configuration(self, filename, name, overrides=None):
        with tempfile.TemporaryDirectory(prefix='rt-workflow-output-test-') as directory:
            target = Path(directory) / 'env'
            target.write_text('EXISTING=value\n')
            env = dict(os.environ, GITHUB_ENV=str(target), RUNNER_TEMP=directory,
                       AZURE_CLIENT_ID='synthetic-client', AZURE_TENANT_ID='synthetic-tenant',
                       AZURE_SUBSCRIPTION_ID='synthetic-subscription',
                       CONFIRMATION='RUN_RUNTIME_SESSION', HOLD_SECONDS='30',
                       K6_PROFILE='smoke', K6_EXECUTION_MODE='manual',
                       K6_RATE_LIMIT_BYPASS_ENABLED='false', P95_OVERRIDE_MS='',
                       ENDPOINT_THRESHOLDS_JSON='{"jira_issues":1000}',
                       SSH_HOST='example.test', SSH_PORT='2222', SSH_USER='synthetic',
                       SSH_PRIVATE_KEY='synthetic', K6_SUPERVISOR_EMAIL='example@example.test',
                       K6_SUPERVISOR_PASSWORD='synthetic', K6_STUDENT_EMAIL='student@example.test',
                       K6_STUDENT_PASSWORD='synthetic', K6_PROJECT_ID='synthetic-project',
                       K6_BASE_URL='https://api.example.test')
            if filename == 'azure-performance-k6-reusable.yml':
                env['CONFIRMATION'] = 'RUN_K6_PERFORMANCE'
            env.update(overrides or {})
            script = body(step((WORKFLOWS / filename).read_text(), name))
            result = subprocess.run(['bash', '-e', '-c', script], env=env, capture_output=True, text=True)
            rows = target.read_text().splitlines()
            return result, rows, directory

    def test_grouped_environment_writes_preserve_all_recovery_paths_and_existing_values(self):
        cases = [
            ('backend-deploy-azure-production.yml', 'Validate workflow configuration',
             dict(ENV_DIR='researchtrack-env', WORK_DIR='researchtrack-specs',
                  RUNTIME_STATE_FILE='researchtrack-runtime-before-deploy.json', RUNTIME_ACTIVE_FILE='researchtrack-runtime-deploy.active')),
            ('azure-runtime-session.yml', 'Validate request and OIDC configuration',
             dict(STATE_FILE='researchtrack-runtime-before-session.json', ACTIVE_FILE='researchtrack-runtime-session.active')),
            ('azure-performance-k6-reusable.yml', 'Validate request and production configuration',
             dict(STATE_FILE='researchtrack-k6-runtime-state.json', ACTIVE_FILE='researchtrack-k6-runtime.active',
                  K6_GATEWAY_STATE_FILE='researchtrack-k6-gateway-state.json', K6_GATEWAY_ACTIVE_FILE='researchtrack-k6-gateway.active')),
        ]
        for filename, name, paths in cases:
            with self.subTest(filename=filename):
                result, rows, directory = self.validate_configuration(filename, name)
                self.assertEqual(0, result.returncode, result.stderr)
                expected = ['EXISTING=value']
                if filename == 'azure-runtime-session.yml':
                    expected.append('HOLD_SECONDS=30')
                expected += [f'{key}={directory}/{value}' for key, value in paths.items()]
                self.assertEqual(expected, rows)

    def test_failed_configuration_does_not_publish_owned_runtime_paths(self):
        for filename, name in [
            ('backend-deploy-azure-production.yml', 'Validate workflow configuration'),
            ('azure-runtime-session.yml', 'Validate request and OIDC configuration'),
            ('azure-performance-k6-reusable.yml', 'Validate request and production configuration'),
        ]:
            with self.subTest(filename=filename):
                result, rows, _ = self.validate_configuration(filename, name, dict(AZURE_CLIENT_ID=''))
                self.assertNotEqual(0, result.returncode)
                self.assertEqual(['EXISTING=value'], rows)

    def test_grouped_smoke_summary_preserves_report_and_explicit_missing_evidence(self):
        script = body(step((WORKFLOWS / 'azure-performance-k6-reusable.yml').read_text(),
                           'Add performance summary to GitHub Actions'))
        for present in [False, True]:
            with self.subTest(present=present), tempfile.TemporaryDirectory() as directory:
                work = Path(directory)
                target = work / 'summary'
                target.write_text('Earlier step evidence\n')
                if present:
                    report = work / 'reports/performance/smoke-report.md'
                    report.parent.mkdir(parents=True)
                    report.write_text('Synthetic k6 threshold: FAIL\n')
                env = dict(os.environ, GITHUB_STEP_SUMMARY=str(target), K6_PROFILE='smoke',
                           K6_EXECUTION_MODE='automatic-post-deploy', K6_RATE_LIMIT_BYPASS_ENABLED='false')
                result = subprocess.run(['bash', '-e', '-c', script], cwd=work, env=env, capture_output=True, text=True)
                self.assertEqual(0, result.returncode, result.stderr)
                output = target.read_text()
                self.assertTrue(output.startswith('Earlier step evidence\n'))
                self.assertIn('Profile: **smoke**', output)
                if present:
                    self.assertIn('Synthetic k6 threshold: FAIL\n\n### Detailed report artifact', output)
                else:
                    self.assertIn('No k6 metrics report was produced', output)


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
