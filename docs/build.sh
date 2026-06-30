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

docfx metadata docfx.json
docfx build docfx.json

echo "Velvet API site generated at: $(pwd)/_site/index.html"
