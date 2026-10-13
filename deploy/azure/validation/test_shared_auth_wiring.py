#!/usr/bin/env python3
"""Exercise the real wiring validator on a private, source-only fixture."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
SERVICES = ('Auth', 'Project', 'GitHub', 'Jira', 'Meeting', 'Submission')
CALL = 'builder.Services.AddResearchTrackJwtAuthentication(builder.Configuration);'


class SharedAuthWiring(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='rt-jwt-wiring-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for rel in ('deploy/validate-shared-auth-wiring.sh', 'deploy/compose.yml',
                    'deploy/scripts/deploy-stack.sh', 'scripts/lib/env.sh',
                    'deploy/build/service-impact.py'):
            target = self.root / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / rel, target)
        for service in SERVICES:
            self.program(service).parent.mkdir(parents=True, exist_ok=True)
            self.program(service).write_text(CALL + '\n')

    def program(self, service):
        return self.root / f'src/Services/ResearchTrack.{service}Service/Program.cs'

    def validate(self):
        return subprocess.run([shutil.which('bash'), 'deploy/validate-shared-auth-wiring.sh'],
                              cwd=self.root, env=os.environ.copy(), text=True, capture_output=True)

    def test_canonical_protected_services_pass(self):
        result = self.validate()
        self.assertEqual(0, result.returncode, result.stderr)

    def test_formatter_wrapped_registration_passes(self):
        self.program('Jira').write_text('builder.Services.AddResearchTrackJwtAuthentication(\n    builder.Configuration\n);\n')
        result = self.validate()
        self.assertEqual(0, result.returncode, result.stderr)

    def test_missing_or_different_configuration_registration_fails(self):
        for source in ('builder.Services.AddHealthChecks();', CALL.replace('builder.Configuration', 'other.Configuration')):
            with self.subTest(source=source):
                self.program('Jira').write_text(source + '\n')
                self.assertNotEqual(0, self.validate().returncode)

    def test_comments_or_literal_text_do_not_count_as_registration(self):
        for source in ('// ' + CALL, '/* ' + CALL + ' */', 'var hint = "' + CALL + '";'):
            with self.subTest(source=source):
                self.program('Jira').write_text(source + '\n')
                self.assertNotEqual(0, self.validate().returncode)

    def test_new_protected_service_requires_contract_membership(self):
        extra = self.program('Unexpected')
        extra.parent.mkdir(parents=True)
        extra.write_text(CALL + '\n')
        self.assertNotEqual(0, self.validate().returncode)


if __name__ == '__main__':
    unittest.main()
