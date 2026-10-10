"""Release gate regression coverage: incomplete or corrupted sets never publish."""
import hashlib
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_release import ROOT, release_version


class ReleaseGateTest(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.dist = Path(self.directory.name)
        self.prefix = 'DoomedLauncher-' + release_version()
        suffixes = [
            'linux-x86_64.tar.gz', 'linux-aarch64.tar.gz',
            'linux-x86_64.flatpak', 'linux-aarch64.flatpak',
            'windows-x86_64.zip', 'windows-x86_64-setup.exe',
            'macos-universal.zip', 'macos-universal.dmg',
        ]
        self.files = [self.dist / (self.prefix + '-' + suffix) for suffix in suffixes]
        hashes = []
        for index, file in enumerate(self.files):
            content = ('test artifact ' + str(index)).encode()
            file.write_bytes(content)
            hashes.append(hashlib.sha256(content).hexdigest() + '  ' + file.name)
        (self.dist / 'SHA256SUMS-test.txt').write_text('\n'.join(hashes) + '\n')

    def gate(self):
        return subprocess.run([sys.executable, str(ROOT / 'scripts/verify_artifacts.py'),
                               '--dist', str(self.dist)], capture_output=True, text=True)

    def test_complete_release_passes(self):
        self.assertEqual(self.gate().returncode, 0)

    def test_missing_platform_fails(self):
        self.files[-1].unlink()
        result = self.gate()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Missing or empty artifact', result.stderr)

    def test_corrupted_archive_fails(self):
        self.files[0].write_bytes(b'corrupted package')
        result = self.gate()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('SHA256 mismatch', result.stderr)

    def test_unexpected_file_fails(self):
        (self.dist / 'unverified.zip').write_bytes(b'extra')
        result = self.gate()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('Unexpected release files', result.stderr)

    def test_duplicate_checksum_fails(self):
        source = self.dist / 'SHA256SUMS-test.txt'
        (self.dist / 'SHA256SUMS-duplicate.txt').write_text(source.read_text())
        self.assertNotEqual(self.gate().returncode, 0)


if __name__ == '__main__':
    unittest.main()
