#!/usr/bin/env python3
"""Refuse `gh pr merge` while the base holds a release the CHANGELOG closed and nobody published.

`scripts/release/published_check.py` owns the decision and the reason for it; the workflow and
`scripts/pr/settle.py` ask it too. This is the copy that fires for a merge typed without either.

Ordering, not prohibition: dispatch the release, then merge.
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import program_invocations

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "scripts" / "release"))
import published_check


HOOK_TOOLS = {"Bash"}


# No operand takes part in the verdict: the base holds an unpublished release or it does not, whatever
# pull request is named.
UNEXPANDED_POLICY = "n/a"

# A base whose release state cannot be read leaves nothing to decide from, and answering either way
# would be a guess. The merge is still refused: unreadable_state_check.py accepts an "allow" only
# where another guard in this directory refuses the same probe.
UNREADABLE_POLICY = "allow"
UNREADABLE_PROBE = {"command": "gh pr merge 1 --squash --delete-branch"}

# Repository state any session can move — the scope rule shared_git_state.py states.
HOOK_SCOPE = "session"

BASE = "main"


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
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
