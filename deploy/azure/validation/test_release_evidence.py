#!/usr/bin/env python3
"""Offline adversarial regressions: no GitHub network, Azure, or user token."""
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('release_evidence', ROOT / 'deploy/azure/scripts/release-evidence.py')
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
A, B, C = 'a' * 40, 'b' * 40, 'c' * 40


def fake(name, sha, *, job_names=(), event='pull_request', pr=17, conclusion='success', status='completed'):
    return dict(id=1 if name == 'ci' else 2, head_sha=sha, event=event,
                head_repository=dict(full_name=mod.REPO), head_branch='main',
                status=status, conclusion=conclusion, pull_requests=[dict(number=pr)],
                check_suite_id=51 if name == 'ci' else 52)


class GithubStub:
    def __init__(self):
        self.branch = A
        self.main_tree = C
        self.pr_tree = C
        self.pr_merged = True
        self.pr_number = 17
        self.ci_ok = True
        self.quality_ok = True
        self.sonar_ok = True
        self.sonar_sha = A
        self.sonar_event = 'push'
        self.existing_release = False
        self.pr_suite_tree = C
        self.fail_api = False
        self.visited = []

    def __call__(self, path, token):
        self.visited.append(path)
        if self.fail_api:
            raise OSError('mock GitHub API failure')
        if path == '/branches/main':
            return dict(name='main', commit=dict(sha=self.branch))
        if path.startswith('/git/commits/'):
            commit = path.rsplit('/', 1)[-1]
            return dict(tree=dict(sha=self.main_tree if commit == A else self.pr_tree))
        if path.startswith('/check-suites/'):
            return dict(head_sha=B)
        if path.startswith('/commits/') and path.endswith('/pulls?per_page=100'):
            return [dict(number=self.pr_number, merged_at='2026-10-13T10:00:00Z' if self.pr_merged else None,
                         base=dict(ref='main', repo=dict(full_name=mod.REPO)),
                         head=dict(sha=B, repo=dict(full_name=mod.REPO)))]
        if '/actions/workflows/backend-deploy-production.yml/runs' in path or '/actions/workflows/azure-infrastructure.yml/runs' in path:
            return dict(workflow_runs=([fake('ci', A, event='push', conclusion='failure')]
                                       if self.existing_release else []))
        if '/actions/workflows/backend-ci.yml/runs' in path:
            return dict(workflow_runs=[fake('ci', B, conclusion='success' if self.ci_ok else 'failure')])
        if '/actions/workflows/backend-code-quality.yml/runs' in path:
            return dict(workflow_runs=[fake('quality', B, conclusion='success' if self.quality_ok else 'failure')])
        if '/actions/workflows/backend-sonarqube.yml/runs' in path:
            return dict(workflow_runs=[fake('sonar', self.sonar_sha, event=self.sonar_event,
                                            conclusion='success' if self.sonar_ok else 'failure')])
        if '/actions/runs/' in path and '/jobs?' in path:
            run = int(path.split('/')[3])
            if run == 1:
                jobs = ['Restore, Build and Test', 'kafka-integration',
                        'Verify trusted source after required CI']
            elif run == 2:
                jobs = ['code-quality', 'SonarScanner for .NET 10']
            else:
                jobs = []
            return dict(total_count=len(jobs), jobs=[dict(name=x, conclusion='success') for x in jobs])
        raise AssertionError(path)


class Evidence(unittest.TestCase):
    def setUp(self):
        self.api = GithubStub()
        self.env = dict(SOURCE_SHA=A, SOURCE_REF='refs/heads/main',
                        SOURCE_REPOSITORY=mod.REPO, SOURCE_EVENT='push', GH_TOKEN='FAKE_TOKEN')

    def test_content_identical_pr_can_reuse_verified_full_ci_and_quality(self):
        self.assertTrue(mod.reuse_ci(self.env, self.api)[0])

    def test_checked_merge_commit_tree_mismatch_must_not_reuse_even_if_pr_head_matches(self):
        class BadMerge(GithubStub):
            def __call__(self, path, token):
                if path.startswith('/check-suites/'):
                    return dict(head_sha='d' * 40)
                if path == '/git/commits/' + 'd' * 40:
                    return dict(tree=dict(sha='e' * 40))
                return super().__call__(path, token)
        self.assertFalse(mod.reuse_ci(self.env, BadMerge())[0])

    def test_failed_pr_ci_falls_back_to_full(self):
        self.api.ci_ok = False
        self.assertFalse(mod.reuse_ci(self.env, self.api)[0])

    def test_failed_quality_falls_back_to_full(self):
        self.api.quality_ok = False
        self.assertFalse(mod.reuse_ci(self.env, self.api)[0])

    def test_changed_tree_falls_back_to_full(self):
        self.api.pr_tree = B
        self.assertFalse(mod.reuse_ci(self.env, self.api)[0])

    def test_unmerged_pr_falls_back_to_full(self):
        self.api.pr_merged = False
        self.assertFalse(mod.reuse_ci(self.env, self.api)[0])

    def test_associated_pr_mismatch_falls_back_to_full(self):
        self.api.pr_number = 19
        self.assertFalse(mod.reuse_ci(self.env, self.api)[0])

    def test_previous_same_sha_release_ci_jobs_reusable_even_if_infra_failed_later(self):
        self.api.existing_release = True
        self.assertTrue(mod.reuse_ci(self.env, self.api)[0])

    def test_manual_release_forces_full_ci(self):
        self.env['SOURCE_EVENT'] = 'workflow_dispatch'
        self.assertFalse(mod.reuse_ci(self.env, self.api)[0])

    def test_api_outage_falls_back_to_full(self):
        self.api.fail_api = True
        self.assertFalse(mod.reuse_ci(self.env, self.api)[0])

    def test_stale_main_source_falls_back_to_full(self):
        self.api.branch = B
        self.assertFalse(mod.reuse_ci(self.env, self.api)[0])

    def test_untrusted_context_has_no_accepted_evidence(self):
        for key, value in [('SOURCE_REF', 'refs/heads/develop'),
                           ('SOURCE_REPOSITORY', 'fork/repo'),
                           ('SOURCE_EVENT', 'pull_request'),
                           ('SOURCE_SHA', 'notasha'),
                           ('GH_TOKEN', '')]:
            with self.subTest(key=key):
                self.assertFalse(mod.reuse_ci(dict(self.env, **{key: value}), self.api)[0])

    def test_successful_same_commit_sonar_required_and_accepted(self):
        self.assertTrue(mod.await_sonar(self.env, self.api, attempts=1, sleep=lambda _: None))

    def test_failed_sonar_is_not_accepted(self):
        self.api.sonar_ok = False
        self.assertFalse(mod.await_sonar(self.env, self.api, attempts=2, delay=0, sleep=lambda _: None))

    def test_stale_sonar_is_not_accepted(self):
        self.api.sonar_sha = B
        self.assertFalse(mod.await_sonar(self.env, self.api, attempts=1, sleep=lambda _: None))

    def test_pr_event_sonar_is_not_accepted(self):
        self.api.sonar_event = 'pull_request'
        self.assertFalse(mod.await_sonar(self.env, self.api, attempts=1, sleep=lambda _: None))

    def test_main_ref_change_during_sonar_polling_is_rejected(self):
        def api(path, token):
            result = self.api(path, token)
            if path.startswith('/actions/runs/'):
                self.api.branch = B
            return result
        self.assertFalse(mod.await_sonar(self.env, api, attempts=1, sleep=lambda _: None))

    def test_sonar_api_outage_never_passes(self):
        self.api.fail_api = True
        with self.assertRaises(OSError):
            mod.await_sonar(self.env, self.api, attempts=1, sleep=lambda _: None)

    def test_probe_writes_output_only_as_true_or_false(self):
        with tempfile.TemporaryDirectory() as path:
            output = Path(path) / 'output'
            original = os.environ.copy()
            try:
                os.environ.clear()
                os.environ.update(self.env, GITHUB_OUTPUT=str(output))
                from unittest.mock import patch
                import sys
                with patch.object(mod, 'reuse_ci', return_value=(False, 'test')):
                    with patch.object(sys, 'argv', ['release-evidence.py', 'probe-ci']):
                        self.assertEqual(0, mod.main())
                self.assertEqual('reuse=false\n', output.read_text())
                with patch.object(mod, 'reuse_ci', return_value=(True, 'test')):
                    with patch.object(sys, 'argv', ['release-evidence.py', 'probe-ci']):
                        self.assertEqual(0, mod.main())
                self.assertEqual('reuse=false\nreuse=true\n', output.read_text())
            finally:
                os.environ.clear()
                os.environ.update(original)

    def test_workflow_dependency_graph_enforces_sonar_before_image_build(self):
        azure = (ROOT / '.github/workflows/backend-deploy-azure-production.yml').read_text()
        validation = (ROOT / '.github/workflows/backend-release-validation.yml').read_text()
        sonar = (ROOT / '.github/workflows/backend-sonarqube.yml').read_text()
        self.assertIn('needs: [validate, sonar_gate]', azure)
        self.assertIn('needs: [build, validate, sonar_gate]', azure)
        self.assertIn('release-evidence.py require-sonar', azure)
        self.assertIn('release-evidence.py probe-ci', validation)
        self.assertIn("SONAR_ENFORCE_GATE: 'true'", sonar)
        self.assertIn('environment:\n      name: production', sonar)
        self.assertIn('    permissions:\n      contents: read\n      actions: read', azure)

    def test_sonar_wrong_job_cannot_impersonate_scan(self):
        class WrongJob(GithubStub):
            def __call__(self, path, token):
                result = super().__call__(path, token)
                if '/jobs?' in path:
                    return dict(total_count=1, jobs=[dict(name='Skipped / unrelated', conclusion='success')])
                return result
        self.assertFalse(mod.await_sonar(self.env, WrongJob(), attempts=1, sleep=lambda _: None))

    def test_incomplete_paginated_jobs_never_validate_evidence(self):
        class Incomplete(GithubStub):
            def __call__(self, path, token):
                result = super().__call__(path, token)
                if '/jobs?' in path:
                    result['total_count'] = 101
                return result
        self.assertFalse(mod.reuse_ci(self.env, Incomplete())[0])
        self.assertFalse(mod.await_sonar(self.env, Incomplete(), attempts=1, sleep=lambda _: None))


if __name__ == '__main__':
    unittest.main()
