#!/usr/bin/env python3
"""Add disabled optional performance settings to legacy Test Gateway env files."""
import os
from pathlib import Path
import re
import sys
import tempfile

DEFAULTS = {
    'RateLimiting__PerformanceTest__Enabled': 'false',
    'RateLimiting__PerformanceTest__AllowedIp': '0.0.0.0',
    'RateLimiting__PerformanceTest__Token': 'DISABLED',
}


def prepare(path):
    if path.is_symlink() or not path.is_file():
        raise ValueError('Expected an existing regular Gateway environment file.')
    original = path.read_bytes()
    entries = {}
    for number, line in enumerate(original.decode('utf-8').splitlines(), 1):
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        key, separator, value = line.partition('=')
        if not separator or not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', key):
            raise ValueError(f'Invalid environment entry at line {number}.')
        if key in entries:
            raise ValueError(f'Duplicate environment key: {key}.')
        entries[key] = value

    def decoded(value):
        if len(value) >= 2 and value[0] in '\"\'' and value[-1] == value[0]:
            return value[1:-1]
        return value

    enabled = decoded(entries.get('RateLimiting__PerformanceTest__Enabled', 'false')).strip().lower()
    if enabled not in ('true', 'false'):
        raise ValueError('PerformanceTest Enabled must explicitly be true or false.')
    if enabled == 'true':
        if any(not decoded(entries.get(key, '')).strip() for key in DEFAULTS):
            raise ValueError('Enabled performance exemption requires explicit complete settings; no defaults applied.')
        return []
    missing = [key for key in DEFAULTS if key not in entries]
    if not missing:
        return []

    # Preserve every original byte/value. Never replace existing settings or
    # print a token. Only previously absent, disabled-feature keys are added.
    newline = b'\r\n' if b'\r\n' in original else b'\n'
    payload = original + (newline if original and not original.endswith(b'\n') else b'')
    payload += b''.join(f'{key}={DEFAULTS[key]}'.encode() + newline for key in missing)
    fd, temporary = tempfile.mkstemp(prefix='.gateway-env-', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as output:
            output.write(payload)
        os.chmod(temporary, 0o600)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)
    return missing


if __name__ == '__main__':
    if len(sys.argv) != 2:
        raise SystemExit('Usage: prepare-gateway-env.py <gateway.env>')
    try:
        added = prepare(Path(sys.argv[1]))
    except (ValueError, OSError) as error:
        raise SystemExit(str(error)) from None
    print('Gateway optional disabled settings added: ' + (', '.join(added) if added else 'none'))
