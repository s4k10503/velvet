#!/usr/bin/env python3
"""Generate the Velvet API reference site into docs/_site.

Prerequisites:
  1. A prior Unity compile so Library/ScriptAssemblies/{Unity.Addressables,
     Unity.ResourceManager}.dll exist (open the project once, or run a batchmode compile).
  2. DocFX: dotnet tool install -g docfx   (ensure ~/.dotnet/tools is on PATH)

For a non-default Unity install, point at its managed-assemblies parent:
  macOS:      UnityEditorContents="/Applications/Unity/Hub/Editor/<ver>/Unity.app/Contents"
  Linux/Win:  UnityEditorContents="<UnityRoot>/Editor/Data"
MSBuild reads UnityEditorContents from the environment automatically.
"""

import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
GUIDES = HERE / "guides"
DOCUMENTATION = HERE.parent / "Packages" / "com.velvet.core" / "Documentation~"


def run(command):
    result = subprocess.run(command, cwd=HERE)
    if result.returncode != 0:
        raise SystemExit(f"error: {' '.join(command)} failed")


def main():
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

    print(f"Velvet API site generated at: {HERE / '_site' / 'index.html'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
