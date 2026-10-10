#!/usr/bin/env python3
"""Stage verified, locked local inputs for a network-free Flatpak build."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
from urllib.parse import unquote, urlparse

from check_release import ROOT


def run(*args, **kwargs):
    return subprocess.run([str(arg) for arg in args], cwd=ROOT, check=True, **kwargs)


def copy(source, destination):
    if destination.exists():
        shutil.rmtree(destination)
    shutil.copytree(source, destination, symlinks=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--flutter-root", type=Path, default=os.environ.get("FLUTTER_ROOT"))
    args = parser.parse_args()
    flutter = shutil.which("flutter")
    if not flutter:
        raise SystemExit("Install Flutter and add its bin directory to PATH first")
    sdk = Path(args.flutter_root) if args.flutter_root else Path(flutter).resolve().parent.parent
    if not (sdk / "bin/flutter").is_file():
        raise SystemExit("--flutter-root must name a Flutter SDK, not a version-manager shim")
    run(flutter, "precache", "--linux")
    run(flutter, "pub", "get", "--enforce-lockfile")
    stage = ROOT / "build/flatpak-input"
    stage.mkdir(parents=True, exist_ok=True)
    config = run("cargo", "vendor", "--locked", "--manifest-path", ROOT / "rust/Cargo.toml",
                 stage / "vendor", capture_output=True, text=True).stdout
    config = config.replace(str(stage / "vendor"), "vendor")
    (stage / "cargo-config.toml").write_text(config + "\n[net]\noffline = true\n")
    copy(sdk, stage / "flutter-sdk")
    # Flutter validates its own package_config after relocation. Include that
    # locked graph as well as the application's, then regenerate paths offline
    # inside the SDK rather than retaining absolute paths to this machine.
    pub = stage / "pub-cache"
    if pub.exists():
        shutil.rmtree(pub)
    pub.mkdir()
    cache = Path(os.environ.get("PUB_CACHE", str(Path.home() / ".pub-cache"))).resolve()
    seen = set()
    for package_file in (ROOT / ".dart_tool/package_config.json",
                         sdk / "packages/flutter_tools/.dart_tool/package_config.json"):
        package_config = json.loads(package_file.read_text())
        for package in package_config["packages"]:
            uri = urlparse(package["rootUri"])
            path = Path(unquote(uri.path)) if uri.scheme == "file" else (package_file.parent / unquote(package["rootUri"]))
            path = path.resolve()
            if path.is_relative_to(cache) and path not in seen:
                seen.add(path)
                relative = path.relative_to(cache)
                destination = pub / relative
                destination.parent.mkdir(parents=True, exist_ok=True)
                copy(path, destination)
                if relative.parts[0] == "hosted":
                    checksum = cache / "hosted-hashes" / relative.parts[1] / (relative.parts[2] + ".sha256")
                    if checksum.exists():
                        output = pub / checksum.relative_to(cache)
                        output.parent.mkdir(parents=True, exist_ok=True)
                        shutil.copy2(checksum, output)
    print("Prepared offline inputs in " + str(stage))


if __name__ == "__main__":
    main()
