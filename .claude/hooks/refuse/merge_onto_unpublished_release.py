#!/usr/bin/env python3
"""Refuse `gh pr merge` while the base holds a release the CHANGELOG closed and nobody published.

`scripts/release/published_check.py` owns the decision and the reason for it; the workflow and
`scripts/pr/settle.py` ask it too. This is the copy that fires for a merge typed without either.

Which base to ask is the pull request's own answer, read through `lib/merge_target.py`. It was a
constant for as long as one branch took pull requests, and on the day a second one did that constant
refused the maintenance branch's release for a version `main` had left open.

Ordering, not prohibition: dispatch the release, then merge.
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from merge_target import UNRESOLVED, merge_targets, refs_of

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "scripts" / "release"))
import published_check


HOOK_TOOLS = {"Bash"}


# The operand names the pull request, and the pull request names the base this asks about, so an
# operand the shell has not expanded leaves nothing to ask it of. Refused on the same rule as
# stale_merge.py states over the same probe.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr merge $PR --squash --delete-branch'

# A base whose release state cannot be read leaves nothing to decide from, and answering either way
# would be a guess. The merge is still refused: unreadable_state_check.py accepts an "allow" only
# where another guard in this directory refuses the same probe. That sibling is stale_merge.py, and
# it reads the pull request the same way and under the same bound — a bound tightened here alone is
# a gh slow enough to lose this reading and fast enough to keep that one, which is this guard
# skipping a base with the sibling reading it fine.
UNREADABLE_POLICY = "allow"
UNREADABLE_PROBE = {"command": "gh pr merge 1 --squash --delete-branch"}

# Repository state any session can move — the scope rule shared_git_state.py states.
HOOK_SCOPE = "session"




def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0

    command = event.get("tool_input", {}).get("command", "")
    targets = merge_targets(command)
    if not targets:
        return 0
    if UNRESOLVED in targets:
        sys.stderr.write(
            "Refusing `gh pr merge`: the pull request is named by an operand the shell has not "
            "expanded yet, so the base whose release state this asks about cannot be read.\n\n"
            "Name the pull request, or see every merge precondition at once:\n"
            "  python3 scripts/pr/settle.py merge <pr> --dry-run\n")
        return 2

    cwd = Path(event.get("cwd") or ".").resolve()
    # Every merge the command carries, deduplicated: a compound command lands each of them, and the
    # release state is a fact about a base rather than about a pull request, so two merges onto one
    # base cost one reading.
    # The head travels with the base: a base holding an unpublished release refuses the change that
    # reopens it, and only that change's own tree says it is the repair.
    bases = []
    for pr in targets:
        target = refs_of(cwd, pr)
        if target is not None and not any(base == target.base for base, _ in bases):
            bases.append((target.base, getattr(target, "head", None)))

    for base, head in bases:
        reason = published_check.unpublished_reason(cwd, f"origin/{base}", fetch=True, result=head)
        if not reason:
            continue
        sys.stderr.write(
            f"Refusing `gh pr merge`: origin/{base} holds an unpublished release.\n\n"
            f"  {reason}\n\n"
            "Anything merged now is inside that release when it is finally dispatched, and its note "
            "was written before this branch existed. Publish first, then merge.\n"
        )
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
