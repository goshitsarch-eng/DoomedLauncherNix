#!/usr/bin/env python3
"""Refuse release publication unless every required package passes SHA256."""
import argparse
import hashlib
from pathlib import Path
from check_release import release_version, ROOT


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--tag')
    parser.add_argument('--dist', type=Path, default=ROOT / 'dist')
    args = parser.parse_args()
    version = release_version()
    if args.tag and args.tag != 'v' + version:
        raise SystemExit('Tag differs from application version')
    prefix = 'DoomedLauncher-' + version
    expected = {
        f'{prefix}-linux-x86_64.tar.gz', f'{prefix}-linux-aarch64.tar.gz',
        f'{prefix}-linux-x86_64.flatpak', f'{prefix}-linux-aarch64.flatpak',
        f'{prefix}-windows-x86_64.zip', f'{prefix}-windows-x86_64-setup.exe',
        f'{prefix}-macos-universal.zip', f'{prefix}-macos-universal.dmg',
    }
    verified = set()
    for sums in args.dist.glob('SHA256SUMS-*.txt'):
        for line in sums.read_text().splitlines():
            checksum, name = line.split('  ', 1)
            if Path(name).name != name or name not in expected:
                raise SystemExit('Unexpected artifact in checksums: ' + name)
            if name in verified:
                raise SystemExit('Duplicate checksum entry: ' + name)
            file = args.dist / name
            if not file.is_file() or file.stat().st_size == 0:
                raise SystemExit('Missing or empty artifact: ' + name)
            with file.open('rb') as stream:
                actual = hashlib.file_digest(stream, 'sha256').hexdigest()
            if actual != checksum:
                raise SystemExit('SHA256 mismatch: ' + name)
            verified.add(name)
    if verified != expected:
        raise SystemExit('Incomplete release matrix; missing: ' + ', '.join(sorted(expected - verified)))
    unexpected = {file.name for file in args.dist.iterdir() if file.is_file()
                  and not file.name.startswith('SHA256SUMS-')} - expected
    if unexpected:
        raise SystemExit('Unexpected release files: ' + ', '.join(sorted(unexpected)))
    print('All 8 required release artifacts verified')


if __name__ == '__main__':
    main()
