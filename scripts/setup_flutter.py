#!/usr/bin/env python3
"""Bootstrap the pinned official Flutter Git revision without a release index."""
import argparse
import json
import os
from pathlib import Path
import platform
import subprocess

VERSION = "3.47.7"
REVISION = "abaf9c523780a608bd46686fd5e53740a07077f8"
REPOSITORY = "https://github.com/flutter/flutter.git"


def run(*arguments, **kwargs):
    return subprocess.run([str(argument) for argument in arguments], check=True, **kwargs)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True)
    args = parser.parse_args()
    sdk = args.directory.resolve()
    if not sdk.exists():
        sdk.parent.mkdir(parents=True, exist_ok=True)
        run("git", "clone", "--depth", "1", "--single-branch", "--branch", VERSION,
            "--config", "advice.detachedHead=false", REPOSITORY, sdk)
    if not (sdk / ".git").is_dir():
        raise SystemExit("Refusing to replace a directory that is not a Flutter Git checkout: " + str(sdk))
    revision = run("git", "-C", sdk, "rev-parse", "HEAD", capture_output=True, text=True).stdout.strip()
    if revision != REVISION:
        raise SystemExit("Flutter revision mismatch; expected " + REVISION + ", got " + revision)
    # Check the source tree too; a correct HEAD alone cannot attest edited files.
    status = run("git", "-C", sdk, "status", "--porcelain", "--untracked-files=no",
                 capture_output=True, text=True).stdout
    if status:
        raise SystemExit("Pinned Flutter checkout has tracked modifications")
    flutter = sdk / "bin" / ("flutter.bat" if platform.system() == "Windows" else "flutter")
    # The official wrapper downloads its pinned Dart SDK and builds its tool.
    # TLS/checksum handling is unchanged; desktop artifacts are fetched by build.
    run(flutter, "config", "--no-analytics")
    result = run(flutter, "--version", "--machine", capture_output=True, text=True)
    metadata = json.loads(result.stdout)
    if metadata.get("frameworkRevision") != REVISION or metadata.get("frameworkVersion") != VERSION:
        raise SystemExit("Bootstrapped Flutter version does not match the pinned source")
    if os.environ.get("GITHUB_PATH"):
        with Path(os.environ["GITHUB_PATH"]).open("a", encoding="utf-8") as stream:
            stream.write(str(sdk / "bin") + "\n")
    if os.environ.get("GITHUB_ENV"):
        with Path(os.environ["GITHUB_ENV"]).open("a", encoding="utf-8") as stream:
            stream.write("FLUTTER_ROOT=" + str(sdk) + "\n")
    print("Verified Flutter " + VERSION + " at " + REVISION)
    print("SDK: " + str(sdk))


if __name__ == "__main__":
    main()
