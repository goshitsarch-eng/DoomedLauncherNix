#!/usr/bin/env python3
"""Validate release versions and optionally extract this version's notes."""
import argparse
import pathlib
import re
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--notes', type=pathlib.Path)
args = parser.parse_args()
match = re.search(r'project\(DoomedLauncher VERSION (\d+\.\d+\.\d+)\b',
                  (root / 'CMakeLists.txt').read_text())
if not match:
    raise SystemExit('No semantic project version found')
version = match.group(1)
releases = ET.parse(root / 'data/com.goshapps.DoomLauncher.metainfo.xml').findall('releases/release')
if not releases or releases[0].get('version') != version:
    raise SystemExit('Latest AppStream release must match CMake')
if f'Current release: **{version}**' not in (root / 'README.md').read_text():
    raise SystemExit('README release must match CMake')
notes = re.search(r'^## ' + re.escape(version) + r'\s*\n(.*?)(?=^## |\Z)',
                  (root / 'RELEASENOTES.md').read_text(), re.M | re.S)
if not notes or not notes.group(1).strip():
    raise SystemExit('Release notes missing for ' + version)
if args.notes:
    args.notes.write_text(notes.group(1).strip() + '\n')
print(version)
