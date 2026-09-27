#!/usr/bin/env python3
# Builds a .unitypackage of this package without Unity. Full docs: CLAUDE.md > Releasing.
#
# A .unitypackage is a .tar.gz with one folder per asset, named by the asset's GUID, holding:
#   asset       the file itself (left out for folders)
#   asset.meta  its .meta file
#   pathname    where Unity puts it, e.g. Packages/com.kndra.hatool/Editor/Core/HaToolMenu.cs
#
# Only package.json and the scripts ship: everything under Editor/ (tools, shared menu, assembly definition).
import gzip
import io
import json
import re
import subprocess
import sys
import tarfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
# Only needed by the tests
EXCLUDE_FILES = {"Editor/Core/AssemblyInfo.cs"}


def included(path):
    parts = Path(path).parts
    if not (path == "package.json" or parts[0] == "Editor") or path in EXCLUDE_FILES:
        return False
    # Unity ignores hidden files and folders ending in ~, so they have no .meta
    return not any(p.startswith(".") or p.endswith("~") for p in parts)


def main():
    package = json.loads((ROOT / "package.json").read_text())
    # The package root folder has no .meta; Unity creates it on import
    install_dir = f"Packages/{package['name']}"
    out = ROOT / f"HaTool-{package['version']}.unitypackage"

    tracked = subprocess.run(["git", "ls-files", "-z"], cwd=ROOT, capture_output=True, check=True, text=True).stdout
    files = sorted(f for f in tracked.split("\0") if f and included(f) and not f.endswith(".meta"))
    folders = sorted({str(p) for f in files for p in Path(f).parents if str(p) != "."})

    missing = [p for p in folders + files if not (ROOT / (p + ".meta")).is_file()]
    if missing:
        sys.exit("Missing .meta files (open the package in Unity and commit them):\n  " + "\n  ".join(missing))

    guids = {}
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w") as tar:
        def add(member, data=None):
            info = tarfile.TarInfo(member)
            if data is None:
                info.type, info.mode = tarfile.DIRTYPE, 0o755
            else:
                info.size, info.mode = len(data), 0o644
            tar.addfile(info, io.BytesIO(data) if data is not None else None)

        for path in folders + files:
            meta = (ROOT / (path + ".meta")).read_bytes()
            match = re.search(rb"^guid: ([0-9a-f]{32})\s*$", meta, re.M)
            if not match:
                sys.exit(f"No guid in {path}.meta")
            guid = match.group(1).decode()
            if guid in guids:
                sys.exit(f"Duplicate guid {guid}: {guids[guid]} and {path}")
            guids[guid] = path

            add(guid)
            if path in files:
                add(f"{guid}/asset", (ROOT / path).read_bytes())
            add(f"{guid}/asset.meta", meta)
            add(f"{guid}/pathname", f"{install_dir}/{path}".encode())

    # Fixed mtime so the same commit always gives the same file
    with open(out, "wb") as f, gzip.GzipFile(filename="", fileobj=f, mode="wb", mtime=0) as gz:
        gz.write(buf.getvalue())

    print(f"Built {out} ({len(files)} files, {len(folders)} folders):")
    for path in folders + files:
        print(f"  {install_dir}/{path}")


if __name__ == "__main__":
    main()
