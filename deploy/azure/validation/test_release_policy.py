#!/usr/bin/env python3
"""Offline release trust boundary regressions; no GitHub or Azure calls."""
import importlib.util
import contextlib
import io
import json
import os
from pathlib import Path
import runpy
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('release_policy', ROOT / 'deploy/azure/scripts/release-policy.py')
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)
summary_spec = importlib.util.spec_from_file_location('release_summary', ROOT / 'deploy/azure/scripts/release-summary.py')
summary = importlib.util.module_from_spec(summary_spec)
summary_spec.loader.exec_module(summary)


class ReleasePolicy(unittest.TestCase):
    def setUp(self):
        self.context = dict(CI_RESULT='success', SOURCE_SHA='a' * 40,
                            SOURCE_REF='refs/heads/' + policy.BRANCH,
                            SOURCE_REPOSITORY=policy.REPOSITORY, SOURCE_EVENT='push')
        self.remote = {'name': policy.BRANCH, 'commit': {'sha': 'a' * 40}}

    def test_current_trusted_push_is_eligible(self):
        self.assertEqual('a' * 40, policy.verify(self.context, self.remote))

    def test_manual_request_requires_the_same_ci_and_current_sha(self):
        self.context['SOURCE_EVENT'] = 'workflow_dispatch'
        self.assertEqual('a' * 40, policy.verify(self.context, self.remote))
        self.context['CI_RESULT'] = 'failure'
        with self.assertRaises(ValueError):
            policy.verify(self.context, self.remote)

    def test_missing_failed_cancelled_skipped_and_inconclusive_ci_is_rejected(self):
        for result in ['', 'failure', 'cancelled', 'skipped', 'neutral', 'pending', None]:
            with self.subTest(result=result), self.assertRaises(ValueError):
                policy.verify(dict(self.context, CI_RESULT=result), self.remote)

    def test_pr_events_and_privileged_workflow_run_are_rejected(self):
        for event in ['pull_request', 'pull_request_target', 'workflow_run', 'repository_dispatch']:
            with self.subTest(event=event), self.assertRaises(ValueError):
                policy.verify(dict(self.context, SOURCE_EVENT=event), self.remote)

    def test_other_branches_tags_and_forks_are_rejected(self):
        for ref in ['refs/heads/main', 'refs/heads/develop', 'refs/tags/' + policy.BRANCH, 'refs/pull/1/merge']:
            with self.subTest(ref=ref), self.assertRaises(ValueError):
                policy.verify(dict(self.context, SOURCE_REF=ref), self.remote)
        with self.assertRaises(ValueError):
            policy.verify(dict(self.context, SOURCE_REPOSITORY='fork/researchtrack-backend'), self.remote)

    def test_stale_malformed_and_spoofed_source_is_rejected(self):
        for sha in ['', 'main', 'a' * 39, 'A' * 40, 'a' * 40 + '\n']:
            with self.subTest(sha=sha), self.assertRaises(ValueError):
                policy.verify(dict(self.context, SOURCE_SHA=sha), self.remote)
        for remote in [{}, {'name': policy.BRANCH, 'commit': {'sha': 'b' * 40}},
                       {'name': 'main', 'commit': {'sha': 'a' * 40}}]:
            with self.subTest(remote=remote), self.assertRaises(ValueError):
                policy.verify(self.context, remote)

    def test_release_build_is_after_required_ci_and_source_check(self):
        workflows = ROOT / '.github/workflows'
        azure = (workflows / 'backend-deploy-azure-production.yml').read_text()
        self.assertIn('  build:\n    needs: validate\n', azure)
        self.assertIn('    needs: [build, validate]\n', azure)
        self.assertIn('uses: ./.github/workflows/backend-release-validation.yml', azure)
        self.assertLess(azure.index('Recheck exact validated source'), azure.index('Materialize environment files'))
        validation = (workflows / 'backend-release-validation.yml').read_text()
        self.assertIn('uses: ./.github/workflows/backend-ci.yml', validation)
        self.assertIn('    needs: ci\n', validation)
        self.assertNotIn('secrets: inherit', validation)
        self.assertNotIn('id-token:', validation)
        ci = (workflows / 'backend-ci.yml').read_text()
        self.assertNotIn('  push:', ci)
        self.assertIn('  workflow_call:', ci)
        self.assertIn('uses: ./.github/workflows/kafka-integration-tests.yml', ci)
        for filename in ['backend-ci.yml', 'backend-build-images.yml', 'backend-deploy-azure-production.yml', 'kafka-integration-tests.yml']:
            source = (workflows / filename).read_text()
            self.assertEqual(source.count('uses: actions/checkout@v4'), source.count('ref: ${{ github.sha }}'), filename)

    def test_vps_deployment_requires_successful_test_target_and_build(self):
        legacy = (ROOT / '.github/workflows/backend-deploy-reusable.yml').read_text()
        deploy = legacy.split('  deploy:', 1)[1]
        self.assertIn("needs.build.result == 'success'", deploy.split('    name:', 1)[0])
        self.assertIn("needs.validate-target.result == 'success'", deploy.split('    name:', 1)[0])
        self.assertIn('    environment: test\n', deploy)
        self.assertNotIn('environment: ${{ inputs.environment_name }}', legacy)
        self.assertNotIn('validate-production:', legacy)


class OperationAuthorization(unittest.TestCase):
    setUp = ReleasePolicy.setUp
    def test_manual_operations_preserve_main_and_temporary_integration(self):
        for branch in ['main', policy.BRANCH]:
            with self.subTest(branch=branch):
                context = dict(self.context, SOURCE_EVENT='workflow_dispatch',
                               SOURCE_REF='refs/heads/' + branch, CI_RESULT='')
                remote = dict(name=branch, commit=dict(sha='a' * 40))
                self.assertEqual('a' * 40, policy.verify_operation(context, remote))

    def test_operation_rejects_forks_pr_tags_other_branches_and_stale_sha(self):
        context = dict(self.context, SOURCE_EVENT='workflow_dispatch')
        overrides = [dict(SOURCE_REPOSITORY='fork/researchtrack-backend'),
                     dict(SOURCE_REF='refs/heads/develop'), dict(SOURCE_REF='refs/tags/main'),
                     dict(SOURCE_SHA='b' * 40), dict(SOURCE_SHA='main')]
        overrides += [dict(SOURCE_EVENT=event) for event in
                      ['push', 'pull_request', 'pull_request_target', 'workflow_run', 'repository_dispatch', '']]
        for override in overrides:
            with self.subTest(override=override), self.assertRaises(ValueError):
                policy.verify_operation(dict(context, **override), self.remote)

    def test_untrusted_release_and_operation_rejected_before_authenticated_api(self):
        for operation in [False, True]:
            with self.subTest(operation=operation), patch.dict(os.environ, dict(self.context,
                    SOURCE_EVENT='workflow_dispatch', SOURCE_REPOSITORY='fork/researchtrack-backend',
                    GH_TOKEN='synthetic-token'), clear=True), patch.object(policy.urllib.request, 'urlopen') as request:
                with self.assertRaises(ValueError):
                    policy.main(operation=operation)
                request.assert_not_called()

    def test_readonly_head_check_and_operation_summary_do_not_claim_ci(self):
        with tempfile.TemporaryDirectory() as directory:
            summary_path = Path(directory) / 'summary'
            context = dict(self.context, SOURCE_EVENT='workflow_dispatch', SOURCE_REF='refs/heads/main',
                           GH_TOKEN='synthetic-token', GITHUB_STEP_SUMMARY=str(summary_path))
            with patch.dict(os.environ, context, clear=True), patch.object(policy.urllib.request, 'urlopen') as request:
                request.return_value.__enter__.return_value = io.StringIO(json.dumps(
                    dict(name='main', commit=dict(sha='a' * 40))))
                with contextlib.redirect_stdout(io.StringIO()) as output:
                    policy.main(operation=True)
                self.assertNotIn('synthetic-token', output.getvalue())
                call = request.call_args.args[0]
                self.assertEqual('GET', call.get_method())
                self.assertTrue(call.full_url.endswith('/branches/main'))
                self.assertIn('not release CI evidence', summary_path.read_text())

    def test_cli_network_errors_do_not_print_tokens_or_response_bodies(self):
        # HTTP header validation can raise ValueError containing a header value.
        # Only our fixed policy messages, not arbitrary ValueErrors, are safe.
        for error_type in [RuntimeError, ValueError]:
            with self.subTest(error_type=error_type.__name__), \
                 patch.dict(os.environ, dict(self.context, GH_TOKEN='secret-sentinel'), clear=True), \
                 patch('sys.argv', ['release-policy.py']), \
                 patch.object(policy.urllib.request, 'urlopen', side_effect=error_type('secret-sentinel response-body')), \
                 contextlib.redirect_stderr(io.StringIO()) as errors:
                with self.assertRaises(SystemExit) as exit_result:
                    runpy.run_path(str(ROOT / 'deploy/azure/scripts/release-policy.py'), run_name='__main__')
                self.assertEqual(1, exit_result.exception.code)
                self.assertIn(error_type.__name__, errors.getvalue())
                self.assertNotIn('secret-sentinel', errors.getvalue())
                self.assertNotIn('response-body', errors.getvalue())



class ReleaseSummary(unittest.TestCase):
    @staticmethod
    def job(name, result='success', start='2026-10-11T10:00:00Z', end='2026-10-11T10:01:00Z'):
        return dict(name=name, conclusion=result, started_at=start, completed_at=end)

    def render(self, jobs=None, smoke='success', deploy='success', attempt=1):
        run = dict(run_attempt=attempt, created_at='2026-10-11T09:59:00Z', run_started_at='2026-10-11T10:00:00Z')
        if jobs is None:
            jobs = [self.job('validate / ci / Restore, Build and Test'), self.job('validate / ci / kafka-integration')]
            required = ['Static Azure infrastructure preflight', 'Runtime deployment preflight',
                        'Verify VM stack and monitoring', 'Verify private networking and Nginx -> Gateway',
                        'Migrate databases and deploy changed Container Apps', 'Restore exact pre-deployment runtime state']
            if smoke == 'success':
                required += ['Start Azure → optional Gateway allowance → VPS k6 → restore Gateway and Azure',
                             'Final Gateway configuration restoration safeguard', 'Final restoration safeguard']
            jobs.append(dict(self.job('Deploy/Smoke evidence'), steps=[self.job(name) for name in required]))
        return summary.render(run, jobs, 'a' * 40, deploy, smoke)

    def test_success_requires_both_ci_jobs_and_smoke(self):
        self.assertIn('**PASS**', self.render())
        self.assertIn('**FAIL**', self.render(jobs=[self.job('Restore, Build and Test')]))
        self.assertIn('**FAIL**', self.render(deploy='failure'))
        self.assertIn('**FAIL**', self.render(smoke='failure'))

    def test_skipped_smoke_and_unexecuted_health_are_explicit(self):
        report = self.render(smoke='skipped')
        self.assertIn('FINAL ACCEPTANCE PENDING', report)
        self.assertNotIn('**PASS**', report)
        self.assertIn('| Optional public HTTP readiness | NOT EXECUTED | - |', report)
        self.assertIn('| k6 smoke runtime session (includes acquire/restore) | SKIPPED | - |', report)

    def test_cancelled_or_inconclusive_required_checks_cannot_be_reported_as_pass(self):
        for result in ['failure', 'cancelled', 'skipped', None]:
            with self.subTest(result=result):
                self.assertIn('**FAIL**', self.render(jobs=[self.job('Restore, Build and Test'), self.job('kafka-integration', result)]))

    def test_parallel_duration_is_wall_time_and_skipped_work_has_no_duration(self):
        rows = [self.job('a'), self.job('b', start='2026-10-11T10:00:30Z', end='2026-10-11T10:01:30Z')]
        self.assertEqual(('PASS', '90'), summary.phase(rows))
        self.assertEqual(('SKIPPED', '-'), summary.phase([self.job('a', 'skipped')]))
        self.assertEqual(('NOT EXECUTED', '-'), summary.phase([]))

    def test_rerun_cannot_claim_original_run_queue_duration(self):
        self.assertIn('| Workflow queue | MEASURED | 60 |', self.render())
        self.assertIn('| Workflow queue | NOT AVAILABLE (rerun/missing timestamp) | - |', self.render(attempt=2))

    def test_smoke_cancelled_missing_or_inconclusive_fails_and_skipped_stays_pending(self):
        for result in ['cancelled', '', 'neutral', None]:
            with self.subTest(result=result):
                self.assertIn('**FAIL**', self.render(smoke=result))
        self.assertIn('FINAL ACCEPTANCE PENDING', self.render(smoke='skipped'))

    def test_failed_deploy_restoration_readiness_or_infrastructure_is_visible(self):
        # Start with realistic successful metadata, then inject each failure even
        # if caller aggregate results incorrectly claim success.
        names = ['Static Azure infrastructure preflight', 'Runtime deployment preflight',
                 'Verify VM stack and monitoring', 'Verify private networking and Nginx -> Gateway',
                 'Migrate databases and deploy changed Container Apps', 'Restore exact pre-deployment runtime state',
                 'Start Azure → optional Gateway allowance → VPS k6 → restore Gateway and Azure',
                 'Final Gateway configuration restoration safeguard', 'Final restoration safeguard']
        for failed in names + ['Unrelated infrastructure error']:
            with self.subTest(failed=failed):
                steps = [self.job(name, 'failure' if name == failed else 'success') for name in names]
                if failed not in names:
                    steps.append(self.job(failed, 'failure'))
                jobs = [self.job('Restore, Build and Test'), self.job('kafka-integration'),
                        dict(self.job('Deploy'), steps=steps)]
                result = self.render(jobs=jobs)
                self.assertIn('**FAIL**', result)
                self.assertIn('Check restoration first', result)
                self.assertIn('| Runtime/Gateway restoration safeguards |', result)

    def test_missing_readiness_or_restoration_evidence_cannot_claim_pass(self):
        self.assertIn('**FAIL**', self.render(jobs=[self.job('Restore, Build and Test'), self.job('kafka-integration')]))

    def test_reporting_cannot_override_failed_required_jobs(self):
        entry = (ROOT / '.github/workflows/backend-deploy-production.yml').read_text()
        self.assertIn('needs: [deploy, performance_smoke]', entry)
        self.assertIn('DEPLOY_RESULT: ${{ needs.deploy.result }}', entry)
        self.assertIn('SMOKE_RESULT: ${{ needs.performance_smoke.result }}', entry)
        for path in ['backend-deploy-production.yml', 'backend-deploy-azure-production.yml', 'azure-performance-k6-reusable.yml']:
            self.assertNotIn('continue-on-error:', (ROOT / '.github/workflows' / path).read_text())

    def test_metadata_unavailable_retains_failure_and_never_leaks_api_error(self):
        for deploy, smoke in [('failure', 'skipped'), ('success', 'cancelled'), ('success', 'success')]:
            with self.subTest(deploy=deploy, smoke=smoke), tempfile.TemporaryDirectory() as directory:
                target = Path(directory) / 'summary.md'
                env = dict(GITHUB_REPOSITORY=summary.REPOSITORY, GITHUB_SHA='a' * 40,
                           GITHUB_RUN_ID='123', GITHUB_RUN_ATTEMPT='2', GH_TOKEN='secret-sentinel',
                           DEPLOY_RESULT=deploy, SMOKE_RESULT=smoke, GITHUB_STEP_SUMMARY=str(target))
                with patch.dict(os.environ, env, clear=True), \
                     patch.object(summary.urllib.request, 'urlopen', side_effect=RuntimeError('secret-sentinel response-body')), \
                     contextlib.redirect_stderr(io.StringIO()) as errors:
                    with self.assertRaises(SystemExit) as result:
                        runpy.run_path(str(ROOT / 'deploy/azure/scripts/release-summary.py'), run_name='__main__')
                self.assertEqual(1, result.exception.code)
                report = target.read_text()
                self.assertIn('INCOMPLETE', report)
                self.assertIn('NOT VERIFIED', report)
                self.assertNotIn('**PASS**', report)
                self.assertNotIn('secret-sentinel', report + errors.getvalue())
                self.assertNotIn('response-body', report + errors.getvalue())
                if deploy == 'failure' or smoke == 'cancelled':
                    self.assertIn('**FAIL', report)

    def test_wrong_run_sha_fails_and_marks_summary_incomplete(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / 'summary.md'
            env = dict(GITHUB_REPOSITORY=summary.REPOSITORY, GITHUB_SHA='a' * 40,
                       GITHUB_RUN_ID='123', GITHUB_RUN_ATTEMPT='1', GH_TOKEN='synthetic-token',
                       DEPLOY_RESULT='success', SMOKE_RESULT='skipped', GITHUB_STEP_SUMMARY=str(target))
            with patch.dict(os.environ, env, clear=True), patch.object(summary.urllib.request, 'urlopen') as request, \
                 contextlib.redirect_stderr(io.StringIO()):
                request.return_value.__enter__.return_value = io.StringIO(json.dumps({'head_sha': 'b' * 40}))
                with self.assertRaises(SystemExit) as result:
                    runpy.run_path(str(ROOT / 'deploy/azure/scripts/release-summary.py'), run_name='__main__')
            self.assertEqual(1, result.exception.code)
            self.assertEqual(1, request.call_count)
            self.assertIn('FINAL ACCEPTANCE NOT VERIFIED', target.read_text())

    def test_unavailable_summary_output_still_fails_without_sensitive_error(self):
        env = dict(GITHUB_REPOSITORY=summary.REPOSITORY, GITHUB_SHA='a' * 40,
                   GITHUB_RUN_ID='123', GITHUB_RUN_ATTEMPT='1', GH_TOKEN='secret-sentinel',
                   DEPLOY_RESULT='failure', SMOKE_RESULT='skipped')
        with patch.dict(os.environ, env, clear=True), \
             patch.object(summary.urllib.request, 'urlopen', side_effect=RuntimeError('secret-sentinel')), \
             contextlib.redirect_stderr(io.StringIO()) as errors:
            with self.assertRaises(SystemExit) as result:
                runpy.run_path(str(ROOT / 'deploy/azure/scripts/release-summary.py'), run_name='__main__')
        self.assertEqual(1, result.exception.code)
        self.assertIn('Release failure summary unavailable: KeyError', errors.getvalue())
        self.assertNotIn('secret-sentinel', errors.getvalue())


if __name__ == '__main__':
    unittest.main()
