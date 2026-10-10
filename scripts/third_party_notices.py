#!/usr/bin/env python3
"""Collect original Rust license notices from locked, offline Cargo metadata."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile


ROOT = Path(__file__).resolve().parents[1]
MAX_NOTICE_BYTES = 1024 * 1024
MAX_OUTPUT_BYTES = 16 * 1024 * 1024
MAX_METADATA_BYTES = 32 * 1024 * 1024
NOTICE_NAME = re.compile(r"^(?:LICEN[CS]E|COPYING|NOTICE|COPYRIGHT)(?:$|[._-])", re.I)

# r-efi's published crate deliberately puts its MIT permission text,
# alternative-license notices and copyright holders in AUTHORS, not LICENSE.
# Keep this version-specific: a changed package layout requires another review.
REVIEWED_FALLBACKS = {("r-efi", "6.0.0"): ("AUTHORS",)}


def read_notice(path):
    with path.open("rb") as handle:
        data = handle.read(MAX_NOTICE_BYTES + 1)
    if len(data) > MAX_NOTICE_BYTES:
        raise ValueError(f"notice {path.name} exceeds the 1 MiB limit")
    text = data.decode("utf-8-sig").replace("\r\n", "\n").replace("\r", "\n")
    if not text.strip() or any(ord(char) < 32 and char not in "\n\t" for char in text):
        raise ValueError(f"notice {path.name} is empty or contains binary/control data")
    return text.rstrip("\n") + "\n"


def package_notices(package):
    directory = Path(package["manifest_path"]).resolve().parent
    paths = {path for path in directory.iterdir() if NOTICE_NAME.match(path.name) and path.is_file()}
    declared = package.get("license_file")
    if declared:
        path = directory / declared
        if not path.is_file():
            raise ValueError(f"declared license_file {Path(declared).name} is missing")
        paths.add(path)
    for name in REVIEWED_FALLBACKS.get((package["name"], package["version"]), ()):
        paths.add(directory / name)

    notices = []
    for path in sorted(paths, key=lambda entry: entry.relative_to(directory).as_posix()):
        if not path.resolve().is_relative_to(directory):
            raise ValueError(f"notice {path.name} resolves outside its published package")
        notices.append((path.relative_to(directory).as_posix(), read_notice(path)))

    # This repository keeps the application's GPL text above rust/Cargo.toml.
    if directory == ROOT / "rust" and package["name"] == "doomedlauncher_core":
        notices.append(("repository LICENSE", read_notice(ROOT / "LICENSE")))
    if not notices:
        raise ValueError(
            "no LICENSE/COPYING/NOTICE file was found; inspect the published "
            "package and add an explicit reviewed fallback before packaging"
        )
    return notices


def render_notices(metadata):
    packages = metadata.get("packages", [])
    if not packages:
        raise ValueError("Cargo metadata contains no packages")
    lines = [
        "# Rust third-party notices\n\n",
        "Original license and notice texts for the locked Cargo dependency graph.\n",
        "This includes development and target-specific packages; inclusion here\n",
        "does not mean every package is linked into every platform's executable.\n",
        "Flutter and Dart notices are distributed separately with Flutter assets.\n\n",
        f"Packages: {len(packages)}\n\n",
    ]
    seen = set()
    size = sum(len(line.encode("utf-8")) for line in lines)
    for package in sorted(packages, key=lambda item: (item["name"], item["version"])):
        identity = (package["name"], package["version"])
        label = " ".join(identity)
        if identity in seen:
            raise ValueError(f"{label}: multiple package origins require an explicit review")
        seen.add(identity)
        try:
            notices = package_notices(package)
        except (OSError, UnicodeError, ValueError) as error:
            raise ValueError(f"{label}: {error}") from error
        section = f"## {label}\n\nDeclared license: {package.get('license') or 'see license file'}\n\n"
        for name, text in notices:
            # Choose a fence that cannot be closed by upstream notice contents.
            longest = max((len(match[0]) for match in re.finditer(r"`+", text)), default=0)
            fence = "`" * max(3, longest + 1)
            section += f"### {name}\n\n{fence}text\n{text}{fence}\n\n"
        size += len(section.encode("utf-8"))
        if size > MAX_OUTPUT_BYTES:
            raise ValueError("combined notices exceed 16 MiB; review before increasing the limit")
        lines.append(section)
    return "".join(lines)


def cargo_metadata(manifest):
    try:
        result = subprocess.run(
            ["cargo", "metadata", "--locked", "--offline", "--format-version", "1",
             "--manifest-path", str(manifest.resolve())],
            cwd=manifest.resolve().parent, check=True, stdout=subprocess.PIPE,
        )
    except FileNotFoundError as error:
        raise ValueError("Cargo is not on PATH; activate the documented Rust toolchain") from error
    except subprocess.CalledProcessError as error:
        raise ValueError(
            "locked offline Cargo metadata failed; run cargo fetch --locked "
            "for this manifest, or restore its vendored dependency configuration"
        ) from error
    if len(result.stdout) > MAX_METADATA_BYTES:
        raise ValueError("Cargo metadata exceeds 32 MiB")
    return json.loads(result.stdout)


def write_atomic(output, text):
    output.parent.mkdir(parents=True, exist_ok=True)
    descriptor, name = tempfile.mkstemp(prefix=".third-party-notices-", dir=output.parent)
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
        temporary.chmod(0o644)
        os.replace(temporary, output)
    finally:
        temporary.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest-path", type=Path, default=ROOT / "rust/Cargo.toml")
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        metadata = cargo_metadata(args.manifest_path)
        text = render_notices(metadata)
        write_atomic(args.output, text)
    except (OSError, UnicodeError, ValueError) as error:
        parser.exit(1, f"Third-party notices: {error}\n")
    print(f"Collected notices for {len(metadata['packages'])} Cargo packages")


if __name__ == "__main__":
    main()
