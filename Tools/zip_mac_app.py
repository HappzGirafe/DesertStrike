"""Zips a macOS .app built on Windows so that it still runs on a Mac.

Windows does not keep the Unix "executable" permission, so a plain zip of the app arrives on the Mac with a
program that is not allowed to run. This zip marks the files in Contents/MacOS (and libraries) executable.

    python Tools/zip_mac_app.py "Builds/macOS/Low Strike.app" Builds/LowStrike-macOS.zip ["Tools/How to open on Mac.txt" ...]
Any extra files are put next to the app in the zip.
(Blender's Python works too: "C:/Program Files/Blender Foundation/Blender 5.2/5.2/python/bin/python.exe")
"""
import os
import stat
import sys
import time
import zipfile

app, output, extras = sys.argv[1], sys.argv[2], sys.argv[3:]
base = os.path.dirname(os.path.abspath(app))
now = time.localtime()[:6]

with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
    for root, dirs, files in os.walk(app):
        for name in dirs:
            path = os.path.relpath(os.path.join(root, name), base).replace(os.sep, "/") + "/"
            info = zipfile.ZipInfo(path, now)
            info.create_system = 3  # Unix, so macOS reads the permission bits below
            info.external_attr = (stat.S_IFDIR | 0o755) << 16
            archive.writestr(info, b"")
        for name in files:
            full = os.path.join(root, name)
            path = os.path.relpath(full, base).replace(os.sep, "/")
            executable = "/Contents/MacOS/" in "/" + path or name.endswith((".dylib", ".so"))
            info = zipfile.ZipInfo(path, now)
            info.create_system = 3
            info.external_attr = (stat.S_IFREG | (0o755 if executable else 0o644)) << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            with open(full, "rb") as source:
                archive.writestr(info, source.read())

    for extra in extras:
        info = zipfile.ZipInfo(os.path.basename(extra), now)
        info.create_system = 3
        info.external_attr = (stat.S_IFREG | 0o644) << 16
        info.compress_type = zipfile.ZIP_DEFLATED
        with open(extra, "rb") as source:
            archive.writestr(info, source.read())

print("Wrote", output)
