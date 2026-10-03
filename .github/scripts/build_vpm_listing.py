#!/usr/bin/env python3
# Builds the site the VRChat Creator Companion reads this package from. Full docs: CLAUDE.md > Releasing.
#
#   index.json  the VPM listing: every released version's package.json plus the address of its .zip
#   index.html  a page that asks the VCC to add the listing (GitHub removes vcc:// links from a README, so it links here)
#
# Released versions are the v* tags. Usage: build_vpm_listing.py [output folder, default site/]
import json
import subprocess
import sys
from pathlib import Path
from urllib.parse import quote

ROOT = Path(__file__).resolve().parents[2]
REPO = "https://github.com/Kndraa/HaTools"
LISTING = "https://kndraa.github.io/HaTools/index.json"
ADD_TO_VCC = "vcc://vpm/addRepo?url=" + quote(LISTING, safe="")

PAGE = f"""<!doctype html>
<html lang="en">
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="light dark">
<title>Add HaTools to VCC</title>
<style>
  body {{ font: 16px/1.5 system-ui, sans-serif; max-width: 34em; margin: 12vh auto; padding: 0 16px; }}
  .button {{ display: inline-block; padding: 10px 18px; border-radius: 6px; background: #1f6feb; color: #fff; text-decoration: none; }}
  code {{ word-break: break-all; }}
</style>
<h1>HaTools</h1>
<p>This page asks the VRChat Creator Companion to add the HaTools package listing. If it didn't open by itself:</p>
<p><a class="button" href="{ADD_TO_VCC}">Add to VCC</a></p>
<p>Or copy this address into the VCC under Settings &gt; Packages &gt; Add Repository:</p>
<p><code>{LISTING}</code></p>
<p><a href="{REPO}">HaTools on GitHub</a></p>
<script>location.href = "{ADD_TO_VCC}";</script>
"""


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, check=True, text=True).stdout


def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / "site"
    packages = {}
    for tag in git("tag", "--list", "v*").split():
        manifest = json.loads(git("show", f"{tag}:package.json"))
        # The release workflow attaches this file to the tag's GitHub Release
        manifest["url"] = f"{REPO}/releases/download/{tag}/{manifest['name']}-{manifest['version']}.zip"
        packages.setdefault(manifest["name"], {"versions": {}})["versions"][manifest["version"]] = manifest

    listing = {"name": "HaTools", "id": "com.kndra.hatools", "url": LISTING, "author": "Kndra", "packages": packages}
    out.mkdir(parents=True, exist_ok=True)
    (out / "index.json").write_text(json.dumps(listing, indent=2) + "\n")
    (out / "index.html").write_text(PAGE)
    # Tells GitHub Pages to serve the files as they are
    (out / ".nojekyll").write_text("")

    versions = [v for p in packages.values() for v in p["versions"]]
    print(f"Built {out} with {len(versions)} version(s): {', '.join(versions)}")


if __name__ == "__main__":
    main()
