#!/usr/bin/env python3
"""Check the packaged native CLI and exported Rust ABI on the current host."""
import argparse
import ctypes
from pathlib import Path
import platform
import subprocess

from check_release import ROOT, release_version


def bundle_paths():
    target = platform.system()
    cpu = "arm64" if platform.machine().lower() in ("arm64", "aarch64") else "x64"
    if target == "Linux":
        bundle = ROOT / f"build/linux/{cpu}/release/bundle"
        return bundle / "doomedlauncher", bundle / "lib/libdoomedlauncher_core.so"
    if target == "Windows":
        bundle = ROOT / f"build/windows/{cpu}/runner/Release"
        return bundle / "doomedlauncher.exe", bundle / "doomedlauncher_core.dll"
    bundle = ROOT / "build/macos/Build/Products/Release/doomedlauncher.app/Contents"
    return bundle / "MacOS/doomedlauncher", bundle / "Frameworks/libdoomedlauncher_core.dylib"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--executable", type=Path)
    parser.add_argument("--library", type=Path)
    args = parser.parse_args()
    executable, library = bundle_paths()
    executable = args.executable or executable
    library = args.library or library
    result = subprocess.run([str(executable), "--version"], check=True,
                            capture_output=True, text=True, timeout=10)
    if result.stdout.strip() != "Doom Launcher " + release_version():
        raise SystemExit("Native CLI version mismatch: " + result.stdout.strip())
    help_result = subprocess.run([str(executable), "--help"], check=True,
                                 capture_output=True, text=True, timeout=10)
    if "Usage: doomedlauncher" not in help_result.stdout:
        raise SystemExit("Native CLI help is missing")
    native = ctypes.CDLL(str(library))
    native.dl_abi_version.argtypes = []
    native.dl_abi_version.restype = ctypes.c_uint32
    if native.dl_abi_version() != 1:
        raise SystemExit("Bundled Rust core ABI does not match the Dart bridge")
    for symbol in ("dl_invoke", "dl_free"):
        getattr(native, symbol)
    print("Native CLI and bundled Rust ABI 1 verified")


if __name__ == "__main__":
    main()
