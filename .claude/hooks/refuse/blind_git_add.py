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
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import git_invocations


HOOK_TOOLS = {"Bash"}

# The sweeping forms. `-u` is not among them: it stages tracked modifications and cannot pick up a
# file that arrived by accident, which is what both incidents were.
#
# Read off tokens rather than matched as text. The pattern this replaces required whitespace or
# end-of-input after the dot, so the ordinary `git add .; git commit` one-liner passed, and its
# command-position anchor accepted only a separator — so a `then`, a `do`, a subshell or an absolute
# path to git all passed as well. Quoting inside an argument is handled by the tokeniser, which is
# what the anchor was there for: the first version of this guard refused its own first use, on a
# pull request body that named the command in prose.
# The sweeping form is recognised from the operand's own text — `-A`, `.` — and an unexpanded one is
# not that text and cannot become it, since the shell substitutes a value rather than a flag. Nothing
# is resolved here, so nothing goes unchecked.
UNEXPANDED_POLICY = "allow"
UNEXPANDED_PROBE = 'git add $FILES'

UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "git add -A"}

SWEEPING = {"-A", "--all", "--no-ignore-removal", ".", ":/", "*"}


def sweeps(command):
    """Whether any segment stages everything rather than named paths."""
    for _, subcommand, operands in git_invocations(command, {"add", "stage"}):
        for token in operands:
            if token == "--":
                continue
            if token in SWEEPING:
                return True
    return False


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0
    command = event.get("tool_input", {}).get("command", "")
    if not sweeps(command):
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
