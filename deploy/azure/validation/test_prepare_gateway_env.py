#!/usr/bin/env python3
"""Legacy Gateway defaults must preserve all existing settings and fail closed."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('prepare_gateway_env', ROOT / 'deploy/prepare-gateway-env.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class GatewayEnvironment(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='rt-gateway-defaults-')
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / 'gateway.env'

    def test_legacy_file_preserves_all_original_bytes_and_adds_disabled_keys_once(self):
        original = b'# Synthetic fixture\r\nAUTH_SERVICE_URL=http://auth:8080\r\nCUSTOM_VALUE=synthetic-private-value'
        self.path.write_bytes(original)
        self.assertEqual(list(module.DEFAULTS), module.prepare(self.path))
        output = self.path.read_bytes()
        self.assertTrue(output.startswith(original + b'\r\n'))
        for key, value in module.DEFAULTS.items():
            self.assertEqual(1, output.count(f'{key}={value}'.encode()))
        self.assertEqual(0o600, self.path.stat().st_mode & 0o777)
        self.assertEqual([], module.prepare(self.path))
        self.assertEqual(output, self.path.read_bytes())

    def test_existing_disabled_settings_and_values_are_preserved(self):
        original = b'RateLimiting__PerformanceTest__Enabled=" False "\nRateLimiting__PerformanceTest__Token=synthetic-existing-token\n'
        self.path.write_bytes(original)
        self.assertEqual(['RateLimiting__PerformanceTest__AllowedIp'], module.prepare(self.path))
        self.assertTrue(self.path.read_bytes().startswith(original))

    def test_complete_enabled_configuration_is_not_changed(self):
        original = b'RateLimiting__PerformanceTest__Enabled=true\nRateLimiting__PerformanceTest__AllowedIp=192.0.2.50\nRateLimiting__PerformanceTest__Token=synthetic-explicit-complete-token-value\n'
        self.path.write_bytes(original)
        self.assertEqual([], module.prepare(self.path))
        self.assertEqual(original, self.path.read_bytes())

    def test_enabled_missing_or_empty_configuration_fails_without_changes(self):
        for suffix in ('', 'RateLimiting__PerformanceTest__Token=\n'):
            with self.subTest(suffix=suffix):
                original = ('RateLimiting__PerformanceTest__Enabled=true\n' + suffix).encode()
                self.path.write_bytes(original)
                with self.assertRaises(ValueError):
                    module.prepare(self.path)
                self.assertEqual(original, self.path.read_bytes())

    def test_invalid_duplicate_or_empty_enabled_values_fail_without_changes(self):
        for original in (b'not-an-entry\n', b'CUSTOM=a\nCUSTOM=b\n',
                         b'RateLimiting__PerformanceTest__Enabled=\n',
                         b'RateLimiting__PerformanceTest__Enabled=invalid\n'):
            with self.subTest(original=original):
                self.path.write_bytes(original)
                with self.assertRaises(ValueError):
                    module.prepare(self.path)
                self.assertEqual(original, self.path.read_bytes())

    def test_unrelated_required_settings_are_not_defaulted(self):
        self.path.write_text('CUSTOM=synthetic\n')
        module.prepare(self.path)
        self.assertNotIn('AUTH_SERVICE_URL=', self.path.read_text())
        self.assertNotIn('FRONTEND_ORIGIN=', self.path.read_text())


if __name__ == '__main__':
    unittest.main()
