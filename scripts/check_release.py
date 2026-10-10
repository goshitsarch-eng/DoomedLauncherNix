#!/usr/bin/env python3
"""Check the Flutter/Rust/distribution version contract before packaging."""
import argparse
from pathlib import Path
import re
import tomllib
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


def release_version():
    match = re.search(r"^version:\s*([^+\s]+)(?:\+\d+)?$", (ROOT / "pubspec.yaml").read_text(), re.M)
    if not match:
        raise ValueError("pubspec.yaml must declare a semantic release version")
    return match.group(1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tag")
    parser.add_argument("--notes", type=Path)
    args = parser.parse_args()
    version = release_version()
    cargo = tomllib.loads((ROOT / "rust/Cargo.toml").read_text())
    if cargo["package"]["version"] != version:
        raise SystemExit("Rust and Flutter versions differ")
    app_info = (ROOT / "macos/Runner/Configs/AppInfo.xcconfig").read_text()
    if not re.search(r"^DOOM_APP_VERSION\s*=\s*" + re.escape(version) + r"$", app_info, re.M):
        raise SystemExit("macOS native CLI version differs from Flutter")
    metadata = ET.parse(ROOT / "data/com.goshapps.DoomLauncher.metainfo.xml")
    if metadata.find("id").text != "com.goshapps.DoomLauncher":
        raise SystemExit("AppStream application ID differs")
    if metadata.find("releases/release").get("version") != version:
        raise SystemExit("Latest AppStream release must match Flutter")
    notes = re.search(r"^## \[?" + re.escape(version) + r"\]?[^\n]*\n(.*?)(?=^## |\Z)",
                      (ROOT / "CHANGELOG.md").read_text(), re.M | re.S)
    if not notes or not notes.group(1).strip():
        raise SystemExit("CHANGELOG.md has no notes for " + version)
    if args.tag and args.tag != "v" + version:
        raise SystemExit("Release tag must be v" + version)
    if args.notes:
        args.notes.write_text(notes.group(1).strip() + "\n")
    print(version)


if __name__ == "__main__":
    main()
