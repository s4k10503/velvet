#!/usr/bin/env python3
"""Copy the imported starter sample over the shipped one.

Unity does not import a ~-suffixed folder, so Packages/com.velvet.core/Samples~/StarterApp is never
compiled, opened or laid out in this project. Assets/VelvetStarterSample is the copy the project
imports, plays and builds, so it is the one to edit; this makes the shipped tree equal to it, and
StarterSampleShippingTests fails when the two diverge.
"""

import shutil
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
SOURCE_DIR = REPO_ROOT / "Assets" / "VelvetStarterSample"
TARGET_DIR = REPO_ROOT / "Packages" / "com.velvet.core" / "Samples~" / "StarterApp"


def main():
    if not SOURCE_DIR.is_dir():
        print(f"error: {SOURCE_DIR} does not exist", file=sys.stderr)
        return 1

    shutil.rmtree(TARGET_DIR, ignore_errors=True)
    shutil.copytree(SOURCE_DIR, TARGET_DIR)
    for junk in TARGET_DIR.rglob(".DS_Store"):
        junk.unlink()

    print(f"synced {SOURCE_DIR} -> {TARGET_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
