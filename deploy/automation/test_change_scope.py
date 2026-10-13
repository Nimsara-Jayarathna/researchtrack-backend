#!/usr/bin/env python3
"""Offline tests for fail-open-for-work selection; no network or credentials."""
import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('change-scope.py')
spec = importlib.util.spec_from_file_location('scope', SCRIPT)
scope = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scope)


class ChangeScopeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='rt-workload-scope-')
        self.addCleanup(self.temp.cleanup)
        self.home = Path(self.temp.name)
        self.git('init', '-q')
        self.git('config', 'user.email', 'ci@example.invalid')
        self.git('config', 'user.name', 'CI fixture')
        self.write('src/Program.cs', 'class Program {}\n')
        self.commit('initial')
        self.base = self.sha()

    def git(self, *args):
        return subprocess.run(['git', '-C', str(self.home), *args], stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE, check=True).stdout.decode().strip()

    def write(self, name, text):
        path = self.home / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)

    def sha(self):
        return self.git('rev-parse', 'HEAD')

    def commit(self, message):
        self.git('add', '-A')
        self.git('commit', '-qm', message)

    def check(self, mode, event='push', force=False, base=None, head=None):
        command = ['python3', str(SCRIPT), '--mode', mode, '--event', event,
                   '--base', base if base is not None else self.base,
                   '--head', head if head is not None else self.sha()]
        if force:
            command.append('--force')
        result = subprocess.run(command, cwd=self.home, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        return 'execute=true' in result.stdout

    def test_only_docs_or_quality_workflow_do_not_deploy_or_scan(self):
        self.write('docs/new manual.md', '# documentation')
        self.write('.github/workflows/backend-code-quality.yml', 'name: quality\n')
        self.commit('docs')
        self.assertFalse(self.check('application'))
        self.assertFalse(self.check('sonar'))

    def test_production_code_always_deploys_and_scans(self):
        self.write('src/Program.cs', 'class Updated {}\n')
        self.commit('code')
        self.assertTrue(self.check('application'))
        self.assertTrue(self.check('sonar'))
        self.assertFalse(self.check('devops', event='pull_request'))

    def test_unit_test_only_avoids_application_release_but_runs_sonar(self):
        self.write('tests/UnitTests/ThingTests.cs', 'class Tests {}\n')
        self.commit('unit')
        self.assertFalse(self.check('application'))
        self.assertTrue(self.check('sonar'))

    def test_deploy_workflow_and_unclassified_files_trigger_release(self):
        self.write('.github/workflows/backend-deploy-production.yml', 'name: change\n')
        self.commit('release change')
        self.assertTrue(self.check('application'))
        self.assertTrue(self.check('devops', event='pull_request'))

    def test_infrastructure_only_bypasses_application_release(self):
        self.write('deploy/azure/main.bicep', 'param x string\n')
        self.commit('infra')
        self.assertFalse(self.check('application'))
        self.assertTrue(self.check('devops', event='pull_request'))

    def test_test_performance_scripts_trigger_devops_validation(self):
        self.write('tests/performance/run.js', 'export const test = 1\n')
        self.commit('perf')
        self.assertTrue(self.check('devops', event='pull_request'))
        self.assertFalse(self.check('application'))
        self.assertFalse(self.check('sonar'))

    def test_unknown_root_file_is_conservative(self):
        self.write('some-new-platform-config.toml', 'name = "x"\n')
        self.commit('unknown')
        for mode in scope.MODES:
            self.assertTrue(self.check(mode, event='pull_request' if mode == 'devops' else 'push'))

    def test_missing_history_force_push_and_initial_push_always_run(self):
        self.write('docs/readme.md', 'doc\n')
        self.commit('doc')
        for base in ['', '0' * 40, 'a' * 40, self.sha()]:
            self.assertTrue(self.check('application', base=base))
        # Existing SHA, non-ancestor of previous checkout HEAD -> fail-closed.
        self.git('checkout', '-qb', 'other', self.base)
        self.write('docs/other.md', 'other\n')
        self.commit('other')
        competing = self.sha()
        self.git('checkout', '-q', 'master')
        self.assertTrue(self.check('application', base=competing))

    def test_manual_force_overrides_empty_history(self):
        self.assertTrue(self.check('application', event='workflow_dispatch', base=''))
        self.assertTrue(self.check('sonar', event='workflow_dispatch', base=''))
        self.assertTrue(self.check('application', force=True))

    def test_release_ci_remains_full_not_pr_optimized(self):
        self.write('src/Program.cs', 'class Other {}\n')
        self.commit('code')
        self.assertTrue(self.check('devops', event='push'))
        self.assertTrue(self.check('devops', event='merge_group'))

    def test_workflow_integration_preserves_release_validation(self):
        root = SCRIPT.parents[2]
        quality = (root / '.github/workflows/backend-code-quality.yml').read_text()
        self.assertNotIn('  push:\n', quality)
        self.assertIn('check-style ci', quality)
        production = (root / '.github/workflows/backend-deploy-production.yml').read_text()
        self.assertIn('needs.release-changes.outputs.execute', production)
        self.assertIn('backend-deploy-azure-production.yml', production)
        reusable = (root / '.github/workflows/backend-release-validation.yml').read_text()
        self.assertIn('uses: ./.github/workflows/backend-ci.yml', reusable)
        self.assertIn('needs: [evidence, ci]', reusable)
        self.assertIn('release-evidence.py probe-ci', reusable)
        self.assertIn("needs.ci.result == 'skipped'", reusable)
        for name in ('backend-deploy-production.yml', 'azure-infrastructure.yml'):
            data = (root / '.github/workflows' / name).read_text()
            self.assertIn('group: researchtrack-azure-production', data)
            self.assertIn('cancel-in-progress: false', data)


if __name__ == '__main__':
    unittest.main(verbosity=2)
