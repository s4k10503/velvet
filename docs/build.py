#!/usr/bin/env python3
"""Generate the Velvet API reference site into docs/_site.

Prerequisites:
  1. A prior Unity compile so Library/ScriptAssemblies/{UniTask,Unity.Addressables,
     Unity.ResourceManager}.dll exist (open the project once, or run a batchmode compile).
  2. DocFX: dotnet tool install -g docfx   (ensure ~/.dotnet/tools is on PATH)

For a non-default Unity install, point at its managed-assemblies parent:
  macOS:      UnityEditorContents="/Applications/Unity/Hub/Editor/<ver>/Unity.app/Contents"
  Linux/Win:  UnityEditorContents="<UnityRoot>/Editor/Data"
MSBuild reads UnityEditorContents from the environment automatically.
"""

import json
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
DOCFX = HERE / "docfx.json"
GUIDES = HERE / "guides"
DOCUMENTATION = HERE.parent / "Packages" / "com.velvet.core" / "Documentation~"


def site():
    """Where DocFX writes the built site, which docfx.json owns."""
    return HERE / json.loads(DOCFX.read_text(encoding="utf-8"))["build"]["output"]


def run(command):
    result = subprocess.run(command, cwd=HERE)
    if result.returncode != 0:
        raise SystemExit(f"error: {' '.join(command)} failed")


def main():
    # The caller that publishes the site asks for the path rather than repeating it, so a rename in
    # docfx.json moves the upload with it instead of leaving it pointed at an empty directory.
    if "--site-path" in sys.argv[1:]:
        print(site().relative_to(HERE.parent).as_posix())
        return 0

    # The hand-authored guides under Packages/com.velvet.core/Documentation~ are the single source
    # of truth (that directory is also the standard Unity Package Manager offline-docs location, so
    # it must stay put). DocFX's TOC/xref resolution only matches conceptual pages that live under
    # this project's own base directory, so we stage a disposable, gitignored copy at docs/guides/
    # rather than referencing Documentation~ in place — nothing under docs/guides/ is ever committed.
    shutil.rmtree(GUIDES, ignore_errors=True)
    GUIDES.mkdir(parents=True)
    for guide in DOCUMENTATION.glob("*.md"):
        shutil.copyfile(guide, GUIDES / guide.name)

    run(["docfx", "metadata", "docfx.json"])
    run(["docfx", "build", "docfx.json"])

    print(f"Velvet API site generated at: {site() / 'index.html'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
