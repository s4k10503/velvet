#!/usr/bin/env python3
"""Refuse `git add -A` / `git add .` and name what it would have swept in.

Twice now a file nobody wrote on purpose has reached a commit this way: an editor's
decompilation residue, and before that a private demo's leftovers on the way to a public
repository. Both times the command was correct for the files being thought about and wrong
for the ones that happened to be in the tree — which is the whole failure mode, since the
sweeping form cannot distinguish them.

Staging by path is the fix, and it costs one line. This prints the untracked set so the
paths are to hand rather than something to go and look up.
"""

import json
import re
import subprocess
import sys

# Anchored at a command position — start of input, or after a separator or newline — so the same text
# quoted inside an argument does not trip it. The first version was not, and refused its own first use:
# a pull request body that named the command in prose, and then the edit that would have fixed it.
BLIND = re.compile(
    r"(?:^|[;&|]|\n)\s*git\s+(?:-C\s+\S+\s+)?add\s+(?:-A\b|--all\b|\.(?:\s|$))")


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") != "Bash":
        return 0
    command = event.get("tool_input", {}).get("command", "")
    if not BLIND.search(command):
        return 0

    cwd = event.get("cwd") or "."
    listing = subprocess.run(
        ["git", "-C", cwd, "status", "--porcelain", "--untracked-files=all"],
        capture_output=True, text=True)
    untracked = [line[3:] for line in listing.stdout.splitlines() if line.startswith("??")]

    reason = [
        "Refusing `git add -A` / `git add .`: it stages files nobody decided to stage.",
        "",
        "A commit here has twice carried something that arrived by accident — a corlib source "
        "file an editor left in a worktree, and a private demo's leftovers on the way to a "
        "public repository. The command was right about the files in mind and wrong about the "
        "ones that happened to be there, which is exactly what the sweeping form cannot tell "
        "apart.",
        "",
        "Stage the paths you mean.",
    ]
    if untracked:
        reason += ["", f"Untracked right now ({len(untracked)}):"]
        reason += [f"  {path}" for path in untracked[:20]]
        if len(untracked) > 20:
            reason.append(f"  … and {len(untracked) - 20} more")
    else:
        reason += ["", "Nothing is untracked right now, so `git add -u` stages every tracked change."]

    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": "\n".join(reason),
        }
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
