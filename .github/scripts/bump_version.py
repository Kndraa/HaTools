#!/usr/bin/env python3
"""Bumps "version" in package.json from a merged PR's labels and prints the new version.

Usage: bump_version.py '<JSON list of label names>'   (e.g. '["minor", "documentation"]')
The highest of major / minor / patch wins; without one of them it's a patch. Only the version
string is replaced, so the rest of package.json keeps its formatting. Full notes: CLAUDE.md > Versioning.
"""
import json
import re
import sys

LEVELS = ["patch", "minor", "major"]
VERSION = re.compile(r'("version"\s*:\s*")(\d+)\.(\d+)\.(\d+)(")')


def level(labels):
    found = [LEVELS.index(l.lower()) for l in labels if l.lower() in LEVELS]
    return LEVELS[max(found)] if found else "patch"


def bump(version, kind):
    major, minor, patch = version
    if kind == "major":
        return major + 1, 0, 0
    if kind == "minor":
        return major, minor + 1, 0
    return major, minor, patch + 1


def main():
    labels = json.loads(sys.argv[1]) if len(sys.argv) > 1 and sys.argv[1] else []
    with open("package.json", encoding="utf-8") as f:
        text = f.read()
    m = VERSION.search(text)
    if not m:
        sys.exit("package.json has no plain major.minor.patch version")
    new = bump(tuple(int(m.group(i)) for i in (2, 3, 4)), level(labels))
    new_version = ".".join(map(str, new))
    text = text[:m.start()] + m.group(1) + new_version + m.group(5) + text[m.end():]
    with open("package.json", "w", encoding="utf-8", newline="") as f:
        f.write(text)
    print(new_version)


if __name__ == "__main__":
    main()
