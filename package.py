#!/usr/bin/env python3
"""Builds one release package per platform into dist/.

Run from anywhere:  python package.py 1.0.0
Build the Windows exe first with windows\\build.bat.
"""

import io
import os
import sys
import tarfile
import time
import zipfile

ROOT = os.path.dirname(os.path.abspath(__file__))
SHARED = ["LICENSE", "CREDITS.txt"]  # copied into every package

# platform: (files from that folder, which of them must be executable, archive kind)
PACKAGES = {
    "windows": (["RamAltman.exe", "sam.jpg", "README.md", "RamAltman.cs", "app.manifest", "build.bat"], [], "zip"),
    "linux": (["ram-altman", "sam.png", "README.md"], ["ram-altman"], "tar.gz"),
    "mac": (["ram-altman.command", "sam.png", "README.md"], ["ram-altman.command"], "zip"),
}


def collect(platform):
    """[(name, bytes, executable)] for one package."""
    files, executables, _ = PACKAGES[platform]
    sources = [(name, os.path.join(ROOT, platform, name)) for name in files]
    sources += [(name, os.path.join(ROOT, name)) for name in SHARED]
    entries = []
    for name, path in sources:
        if not os.path.isfile(path):
            sys.exit("Missing %s. For the Windows exe, run windows\\build.bat first." % path)
        with open(path, "rb") as f:
            data = f.read()
        executable = name in executables
        if executable and b"\r" in data:
            sys.exit("%s has Windows line endings; its first line would not work on Linux or macOS." % path)
        entries.append((name, data, executable))
    return entries


def write_zip(path, folder, entries):
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, data, executable in entries:
            info = zipfile.ZipInfo("%s/%s" % (folder, name), time.localtime()[:6])
            info.compress_type = zipfile.ZIP_DEFLATED
            info.create_system = 3  # Unix, so the executable flag survives unzipping on macOS and Linux
            info.external_attr = (0o100755 if executable else 0o100644) << 16
            archive.writestr(info, data)


def write_tar(path, folder, entries):
    with tarfile.open(path, "w:gz") as archive:
        for name, data, executable in entries:
            info = tarfile.TarInfo("%s/%s" % (folder, name))
            info.size = len(data)
            info.mode = 0o755 if executable else 0o644
            info.mtime = int(time.time())
            archive.addfile(info, io.BytesIO(data))


def main():
    if len(sys.argv) != 2:
        sys.exit("Usage: python package.py <version>   e.g. python package.py 1.0.0")
    version = sys.argv[1]
    dist = os.path.join(ROOT, "dist")
    os.makedirs(dist, exist_ok=True)
    for platform, (_, _, kind) in PACKAGES.items():
        folder = "RamAltman-%s-%s" % (version, platform)
        path = os.path.join(dist, "%s.%s" % (folder, kind))
        entries = collect(platform)
        (write_zip if kind == "zip" else write_tar)(path, folder, entries)
        print("%s  (%d files, %d KB)" % (os.path.relpath(path, ROOT), len(entries), os.path.getsize(path) // 1024))


if __name__ == "__main__":
    main()
