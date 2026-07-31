#!/usr/bin/env bash
# Copies the imported starter sample over the shipped one.
#
# Unity does not import a ~-suffixed folder, so Packages/com.velvet.core/Samples~/StarterApp is never
# compiled, opened or laid out in this project. Assets/VelvetStarterSample is the copy the project
# imports, plays and builds, so it is the one to edit; this makes the shipped tree equal to it, and
# StarterSampleShippingTests fails when the two diverge.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_dir="$repo_root/Assets/VelvetStarterSample"
target_dir="$repo_root/Packages/com.velvet.core/Samples~/StarterApp"

if [ ! -d "$source_dir" ]; then
  echo "error: $source_dir does not exist" >&2
  exit 1
fi

rm -rf "$target_dir"
mkdir -p "$target_dir"
cp -R "$source_dir"/. "$target_dir"/
find "$target_dir" -name .DS_Store -delete

echo "synced $source_dir -> $target_dir"
