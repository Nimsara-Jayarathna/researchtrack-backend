#!/usr/bin/env python3
"""Run the real backup shell body with private paths and synthetic dump/CLI data."""
import gzip
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]


class MysqlBackup(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='rt-backup-safety-')
        self.addCleanup(self.temp.cleanup)
        self.work = Path(self.temp.name)
        self.data = self.work / 'data'
        self.backups = self.data / 'backups'
        self.bin = self.work / 'bin'
        self.bin.mkdir()
        # >1 KiB compressed, intentionally synthetic and not production SQL.
        (self.work / 'dump.sql').write_text('CREATE TABLE dr_fixture (payload TEXT);\n' + secrets.token_hex(12000))
        helper = self.bin / 'compose'
        helper.write_text('''#!/usr/bin/env python3
import os, pathlib, sys
assert sys.argv[1:4] == ['exec', '-T', 'mysql']
for flag in ['--single-transaction', '--routines', '--triggers', '--events', '--set-gtid-purged=OFF', '--databases']:
    assert flag in sys.argv[-1]
pathlib.Path(os.environ['CALLS']).write_text('dump attempted')
if os.environ.get('DUMP_MODE') == 'small': print('tiny')
else: sys.stdout.write(pathlib.Path(os.environ['FIXTURE_DUMP']).read_text())
sys.exit(7 if os.environ.get('DUMP_MODE') == 'fail' else 0)
''')
        helper.chmod(0o755)
        self.executable('mountpoint', '#!/bin/sh\n[ "$TEST_MOUNTED" = 1 ]\n')
        # Production uses GNU stat; model its single required operation on macOS.
        self.executable('stat', '#!/usr/bin/env python3\nimport pathlib,sys\nassert sys.argv[1:3]==["-c","%s"]\nprint(pathlib.Path(sys.argv[3]).stat().st_size)\n')
        # Linux xargs supports -r; the only inputs here are fixture backup paths.
        self.executable('xargs', '''#!/usr/bin/env python3
import subprocess, sys
items=sys.stdin.read().splitlines()
args=[x for x in sys.argv[1:] if x!='-r']
if items: subprocess.run(args+items,check=True)
''')
        source = (ROOT / 'deploy/azure/vm/scripts/backup-mysql.sh').read_text()
        self.script = self.work / 'backup.sh'
        self.script.write_text(source.replace('/data/researchtrack', str(self.data)).replace('/opt/researchtrack/scripts/compose.sh', str(helper)))
        self.env = dict(os.environ, PATH=str(self.bin) + ':' + os.environ['PATH'], TEST_MOUNTED='1',
                        CALLS=str(self.work / 'calls'), FIXTURE_DUMP=str(self.work / 'dump.sql'), DUMP_MODE='')

    def executable(self, name, text):
        path = self.bin / name
        path.write_text(text)
        path.chmod(0o755)

    def run_backup(self, **env):
        return subprocess.run(['bash', str(self.script)], env=dict(self.env, **env), capture_output=True, text=True)

    def final_backups(self):
        return sorted(self.backups.glob('researchtrack-mysql-*.sql.gz'))

    def test_success_is_valid_private_gzip_and_preserves_dump_bytes(self):
        result = self.run_backup()
        self.assertEqual(0, result.returncode, result.stderr)
        files = self.final_backups()
        self.assertEqual(1, len(files))
        self.assertEqual((self.work / 'dump.sql').read_bytes(), gzip.decompress(files[0].read_bytes()))
        self.assertEqual(0o600, files[0].stat().st_mode & 0o777)
        self.assertEqual(0o700, self.backups.stat().st_mode & 0o777)
        self.assertFalse(list(self.backups.glob('.researchtrack-mysql-*')))

    def test_failed_dump_is_not_published_or_rotated(self):
        self.backups.mkdir(parents=True)
        old = self.backups / 'researchtrack-mysql-20000101T000000Z.sql.gz'
        old.write_bytes(gzip.compress(b'previous valid fixture'))
        original = old.read_bytes()
        result = self.run_backup(DUMP_MODE='fail')
        self.assertNotEqual(0, result.returncode)
        self.assertEqual([old], self.final_backups())
        self.assertEqual(original, old.read_bytes())
        self.assertFalse(list(self.backups.glob('.researchtrack-mysql-*')))

    def test_small_dump_fails_without_final_artifact(self):
        result = self.run_backup(DUMP_MODE='small')
        self.assertNotEqual(0, result.returncode)
        self.assertEqual([], self.final_backups())

    def test_missing_data_mount_fails_before_directory_or_dump(self):
        result = self.run_backup(TEST_MOUNTED='0')
        self.assertNotEqual(0, result.returncode)
        self.assertFalse(self.data.exists())
        self.assertFalse((self.work / 'calls').exists())

    def test_success_rotates_only_matching_completed_backups_to_seven(self):
        self.backups.mkdir(parents=True)
        old = []
        for day in range(1, 9):
            path = self.backups / f'researchtrack-mysql-200001{day:02}T000000Z.sql.gz'
            path.write_bytes(gzip.compress(b'previous fixture'))
            old.append(path)
        unrelated = self.backups / 'unrelated.sql.gz'
        unrelated.write_bytes(b'unrelated')
        result = self.run_backup()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(7, len(self.final_backups()))
        self.assertFalse(old[0].exists())
        self.assertFalse(old[1].exists())
        self.assertTrue(all(p.exists() for p in old[2:]))
        self.assertEqual(b'unrelated', unrelated.read_bytes())

    def test_same_timestamp_dumps_do_not_overwrite_previous_backup(self):
        self.executable('date', '#!/bin/sh\necho 20261012T000000Z\n')
        first = self.run_backup()
        self.assertEqual(0, first.returncode, first.stderr)
        previous = self.final_backups()[0]
        original = previous.read_bytes()
        (self.work / 'dump.sql').write_text(secrets.token_hex(12000))
        second = self.run_backup()
        self.assertEqual(0, second.returncode, second.stderr)
        self.assertEqual(2, len(self.final_backups()))
        self.assertEqual(original, previous.read_bytes())


if __name__ == '__main__':
    unittest.main()
