#!/usr/bin/env python3
"""Refuse `gh pr merge` while the base holds a release the CHANGELOG closed and nobody published.

A release reaches main as an ordinary commit — the section dated, package.json bumped — and the
publish is a separate `workflow_dispatch` that nothing forces. v2.0.1 sat in that gap through five
merges. Each of them was then inside the release the dispatch would build, whose note had been
written before any of them existed and described none of them; recovering meant tagging the release
commit by hand and dispatching from the tag rather than from the branch.

The decision lives in `scripts/release/published_check.py`, which the workflow and
`scripts/pr/settle.py` also ask. This is the copy that fires for a merge typed without either.

Ordering, not prohibition: dispatch the release, then merge.
"""

import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import program_invocations

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "scripts" / "release"))
import published_check


# No operand takes part in the verdict: the base holds an unpublished release or it does not,
# whatever pull request is named. A probe would therefore be answered by the repository's own
# publication state, so posing one would read a live release window as a policy disagreement.
UNEXPANDED_POLICY = "n/a"

# The base's publication state is repository state any session can move, so this is registered on the
# event rather than narrowed to one agent.
HOOK_SCOPE = "session"

BASE = "main"


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") != "Bash":
        return 0

    command = event.get("tool_input", {}).get("command", "")
    if not any(True for _ in program_invocations(command, "gh", ("pr", "merge"))):
        return 0

    cwd = Path(event.get("cwd") or ".").resolve()
    reason = published_check.unpublished_reason(cwd, f"origin/{BASE}", fetch=True)
    if not reason:
        return 0

    sys.stderr.write(
        f"Refusing `gh pr merge`: origin/{BASE} holds an unpublished release.\n\n"
        f"  {reason}\n\n"
        "Anything merged now is inside that release when it is finally dispatched, and its note was "
        "written before this branch existed. Publish first, then merge.\n"
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())
