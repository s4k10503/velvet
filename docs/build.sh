#!/usr/bin/env bash
# Generates the Velvet API reference site into docs/_site.
#
# Prerequisites:
#   1. A prior Unity compile so Library/ScriptAssemblies/{UniTask,Unity.Addressables,
#      Unity.ResourceManager}.dll exist (open the project once, or run a batchmode compile).
#   2. DocFX: dotnet tool install -g docfx   (ensure ~/.dotnet/tools is on PATH)
#
# For a non-default Unity install, point at its managed-assemblies parent:
#   macOS:        UnityEditorContents="/Applications/Unity/Hub/Editor/<ver>/Unity.app/Contents"
#   Linux/Win:    UnityEditorContents="<UnityRoot>/Editor/Data"
# MSBuild reads UnityEditorContents from the environment automatically.
set -euo pipefail
cd "$(dirname "$0")"

# The hand-authored guides under Packages/com.velvet.core/Documentation~ are the single source of
# truth (that directory is also the standard Unity Package Manager offline-docs location, so it
# must stay put). DocFX's TOC/xref resolution only matches conceptual pages that live under this
# project's own base directory, so we stage a disposable, gitignored copy at docs/guides/ rather
# than referencing Documentation~ in place — nothing under docs/guides/ is ever committed.
rm -rf guides
mkdir -p guides
cp ../Packages/com.velvet.core/Documentation~/*.md guides/

docfx metadata docfx.json
docfx build docfx.json

echo "Velvet API site generated at: $(pwd)/_site/index.html"
