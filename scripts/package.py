#!/usr/bin/env python3
"""Build and package the current host desktop target, including its Rust core."""
import argparse
import hashlib
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tarfile
import zipfile

from check_release import ROOT, release_version


def run(*args, **kwargs):
    subprocess.run([str(arg) for arg in args], check=True, cwd=ROOT, **kwargs)


def zip_tree(source, target):
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for file in sorted(source.rglob("*")):
            if file.is_file():
                archive.write(file, source.name + "/" + str(file.relative_to(source)))


def copy_documentation(destination):
    for name in ("LICENSE", "README.md", "BUILDING.md", "PLATFORM_SUPPORT.md",
                 "ARCHITECTURE.md", "MIGRATION_AUDIT.md", "CONTRIBUTING.md",
                 "CHANGELOG.md", "RELEASENOTES.md"):
        if (ROOT / name).is_file():
            shutil.copy2(ROOT / name, destination / name)
    if (ROOT / "docs").is_dir():
        shutil.copytree(ROOT / "docs", destination / "docs")
    run(sys.executable, ROOT / "scripts/third_party_notices.py", "--output",
        destination / "THIRD_PARTY_NOTICES.md")


def windows_runtime(destination, architecture):
    # Microsoft permits redistribution of these DLLs from the VS REDIST list.
    # App-local deployment lets a portable ZIP run without an administrator.
    redist = os.environ.get("VCToolsRedistDir")
    if not redist:
        vswhere = Path(os.environ.get("ProgramFiles(x86)", "C:/Program Files (x86)")) / "Microsoft Visual Studio/Installer/vswhere.exe"
        install = subprocess.check_output([str(vswhere), "-latest", "-products", "*", "-property", "installationPath"], text=True).strip()
        versions = sorted((Path(install) / "VC/Redist/MSVC").glob("*"))
        if not versions:
            raise SystemExit("Visual Studio C++ redistributables were not found")
        redist = versions[-1]
    candidates = sorted((Path(redist) / architecture).glob("Microsoft.VC*.CRT"))
    if not candidates:
        raise SystemExit("Missing Microsoft C++ runtime for " + architecture)
    for file in candidates[-1].glob("*.dll"):
        shutil.copy2(file, destination / file.name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--offline", action="store_true", help="Use already cached locked Pub packages")
    parser.add_argument("--skip-build", action="store_true", help="Package a previously completed release build")
    parser.add_argument("--skip-installer", action="store_true", help="Windows: create only the portable ZIP")
    args = parser.parse_args()
    run(sys.executable, ROOT / "scripts/check_release.py")
    version = release_version()
    target = {"Linux": "linux", "Windows": "windows", "Darwin": "macos"}[platform.system()]
    arch = "aarch64" if platform.machine().lower() in ("arm64", "aarch64") else "x86_64"
    if target == "macos":
        arch = "universal"
    flutter = shutil.which("flutter")
    if not args.skip_build:
        if not flutter:
            raise SystemExit("Flutter is not on PATH")
        run(flutter, "pub", "get", "--enforce-lockfile", *(["--offline"] if args.offline else []))
        # Apple and Windows numeric version resources cannot contain -alpha.
        run(flutter, "build", target, "--release", "--no-pub", "--build-name=" + version.split("-")[0])
    run(sys.executable, ROOT / "scripts/verify_native_bundle.py")
    dist = ROOT / "dist"
    dist.mkdir(exist_ok=True)
    name = f"DoomedLauncher-{version}-{target}-{arch}"
    stage = ROOT / "build/package" / name
    if stage.exists():
        shutil.rmtree(stage)
    stage.parent.mkdir(parents=True, exist_ok=True)
    if target == "linux":
        cpu = "arm64" if arch == "aarch64" else "x64"
        bundle = ROOT / f"build/linux/{cpu}/release/bundle"
        if not (bundle / "lib/libdoomedlauncher_core.so").is_file():
            raise SystemExit("Missing bundled Rust core; run flutter build linux --release")
        shutil.copytree(bundle, stage)
        for file in ("com.goshapps.DoomLauncher.desktop", "com.goshapps.DoomLauncher.metainfo.xml", "com.goshapps.DoomLauncher.png"):
            shutil.copy2(ROOT / "data" / file, stage / file)
        artifact = dist / (name + ".tar.gz")
        copy_documentation(stage)
        with tarfile.open(artifact, "w:gz") as archive:
            archive.add(stage, arcname=name)
    elif target == "windows":
        cpu = "arm64" if arch == "aarch64" else "x64"
        bundle = ROOT / f"build/windows/{cpu}/runner/Release"
        if not (bundle / "doomedlauncher_core.dll").is_file():
            raise SystemExit("Missing bundled Rust core; run flutter build windows --release")
        shutil.copytree(bundle, stage, ignore=shutil.ignore_patterns("*.pdb", "*.exp", "*.lib"))
        windows_runtime(stage, cpu)
        copy_documentation(stage)
        zip_tree(stage, dist / (name + ".zip"))
        if not args.skip_installer:
            compiler = shutil.which("ISCC") or "C:/Program Files (x86)/Inno Setup 6/ISCC.exe"
            run(compiler, "/DAppVersion=" + version, "/DAppNumericVersion=" + version.split("-")[0],
                "/DSourceDir=" + str(stage), "/DOutputDir=" + str(dist),
                "/DPackageName=" + name, "/DTargetArch=" + cpu, ROOT / "packaging/windows/installer.iss")
    else:
        bundle = ROOT / "build/macos/Build/Products/Release/doomedlauncher.app"
        library = bundle / "Contents/Frameworks/libdoomedlauncher_core.dylib"
        if not library.is_file():
            raise SystemExit("Missing bundled Rust core; run flutter build macos --release")
        # Assert the advertised architecture rather than publishing a mislabeled package.
        run("lipo", "-verify_arch", "arm64", "x86_64", library)
        run("lipo", "-verify_arch", "arm64", "x86_64", bundle / "Contents/MacOS/doomedlauncher")
        stage.mkdir()
        shutil.copytree(bundle, stage / bundle.name, symlinks=True)
        copy_documentation(stage)
        run("ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", stage, dist / (name + ".zip"))
        (stage / "Applications").symlink_to("/Applications", target_is_directory=True)
        run("hdiutil", "create", "-volname", "Doom Launcher", "-srcfolder", stage,
            "-ov", "-format", "UDZO", dist / (name + ".dmg"))
    artifact_names = {
        "linux": {name + ".tar.gz"},
        "windows": {name + ".zip"} | (set() if args.skip_installer else {name + "-setup.exe"}),
        "macos": {name + ".zip", name + ".dmg"},
    }[target]
    artifacts = [dist / filename for filename in sorted(artifact_names)]
    with (dist / ("SHA256SUMS-" + target + "-" + arch + ".txt")).open("w") as checksums:
        for artifact in artifacts:
            with artifact.open("rb") as stream:
                checksum = hashlib.file_digest(stream, "sha256").hexdigest()
            checksums.write(checksum + "  " + artifact.name + "\n")
    print("Packaged " + ", ".join(str(path) for path in artifacts))


if __name__ == "__main__":
    main()
