#!/usr/bin/env python3
"""Run `mutation_check.py` inside the CI editor image, holding a Unity licence for exactly as long.

`game-ci/unity-test-runner` launches one editor per step, and a campaign launches one per mutant from
inside its own loop, with a source edit between each. So the campaign cannot run through the action; it
runs in the same image the action pulls, under `scripts/ci/licensed_editor.py`'s licence.

    python3 scripts/test_quality/ci_mutation_shard.py -- --base <sha> --shard 0/4 --max 12

Everything after `--` is handed to mutation_check.py, with `--unity` set to the image's editor.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "ci"))
import licensed_editor  # noqa: E402

CAMPAIGN = Path(__file__).resolve().with_name("mutation_check.py")


def main(argv, **licensed):
    passthrough = argv[argv.index("--") + 1:] if "--" in argv else argv
    command = [sys.executable, "-u", str(CAMPAIGN), *passthrough, "--unity", licensed_editor.UNITY]
    return licensed_editor.run_licensed(command, "the campaign", **licensed)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
