#!/usr/bin/env python3
"""Offline release trust boundary regressions; no GitHub or Azure calls."""
import importlib.util
from pathlib import Path
import unittest

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

    def test_legacy_deployment_requires_build_success_and_rechecks_before_ssh(self):
        legacy = (ROOT / '.github/workflows/backend-deploy-reusable.yml').read_text()
        deploy = legacy.split('  deploy:', 1)[1]
        self.assertIn("needs.build.result == 'success'", deploy.split('    name:', 1)[0])
        self.assertIn("needs.validate-production.result == 'success'", deploy.split('    name:', 1)[0])
        self.assertLess(deploy.index('Recheck production source'), deploy.index('Configure SSH'))


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


if __name__ == '__main__':
    unittest.main()
