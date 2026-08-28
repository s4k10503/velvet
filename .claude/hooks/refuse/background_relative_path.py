#!/usr/bin/env python3
"""Refuse a backgrounded command that names a repo path relatively without saying where it runs.

A `cd` reaches only the command it is part of. A later call — and every backgrounded one — starts at
the session's own directory, so a command naming a script under `scripts/` reads the primary
checkout's copy of it, and of whatever it opens, whichever worktree the work is in.

The failure is silent in the direction that matters. The run succeeds, against the wrong tree: a
sweep reported "179 holes across 14 fixtures" for a map that had 18, because the four the worktree
added were in a file the primary checkout does not have. Nothing distinguishes that from a real
answer except knowing which tree it came from, and the number it prints is exactly the shape of one.

What is refused is a background command that reaches a repo path relatively AND does not open with a
`cd`. An absolute path is allowed, as is a leading `cd` — the two spellings that say where the
command runs. The foreground is left alone: its directory is whatever the previous call left, which
is knowable, and blocking it would refuse the ordinary shape of every other command in a session.
"""

import fnmatch
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import unexpanded  # noqa: E402  (path set above)


HOOK_TOOLS = {"Bash"}


# Nothing here reads a path's contents: a token the shell will still rewrite may or may not be
# relative, and a background command that gets it wrong fails silently, so the guard errs toward
# allowing rather than refusing every command carrying a variable.
UNEXPANDED_POLICY = "allow"
UNEXPANDED_PROBE = 'python3 $DIR/scripts/pr/settle.py watch'

UNREADABLE_POLICY = "none"
UNREADABLE_PROBE = {"command": "python3 scripts/pr/settle.py watch", "run_in_background": True}

# The repository's own top-level directories, which a command reaching one of them relatively is
# reaching through the session's directory rather than through a stated one. Derived from the
# checkout so a directory added later is covered by existing.
def repo_roots(project):
    root = Path(project)
    if not (root / ".git").exists():
        return set()
    return {entry.name for entry in root.iterdir()
            if entry.is_dir() and not entry.name.startswith(".")}


# A glob is a selector the shell rewrites into a path, so a token carrying one names the same
# directory a plain spelling would. Read here rather than in `unexpanded`, which several guards share
# and one of them -- `shared_git_state` -- deliberately treats `git checkout '*.cs'` as an ordinary
# restore. Measured before this: backgrounded, `python3 script?/pr/settle.py watch` was allowed while
# `python3 scripts/pr/settle.py watch` was refused, so one character routed around the guard.
GLOB = re.compile(r"[*?\[]")


def relative_repo_tokens(command, roots):
    """Tokens naming a repo directory relatively, in a command that never says where it runs."""
    found = []
    for token in re.findall(r"[A-Za-z0-9_.~/*?\[\]-]+", command):
        if token.startswith("/") or unexpanded(token):
            continue
        if "/" not in token:
            continue
        head = token.split("/", 1)[0]
        # By glob where the head carries one, so `script?` reaches `scripts`. A head with no glob is
        # compared as itself, which is what it was.
        if head in roots or (GLOB.search(head)
                             and any(fnmatch.fnmatch(root, head) for root in roots)):
            found.append(token)
    return sorted(set(found))


def opens_with_cd(command):
    """Whether the command's first segment is a cd, which is what makes a relative path answerable."""
    return re.match(r"\s*cd\s+\S", command) is not None


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0

    tool_input = event.get("tool_input", {})
    if not tool_input.get("run_in_background"):
        return 0

    command = tool_input.get("command", "")
    if opens_with_cd(command):
        return 0

    found = relative_repo_tokens(command, repo_roots(event.get("cwd") or "."))
    if not found:
        return 0

    sys.stderr.write(
        "Refusing a background command that names a repo path relatively:\n\n"
        + "".join(f"  {token}\n" for token in found)
        + "\nA background command does not inherit a `cd` from an earlier call. It starts at the "
        "session's directory, so these resolve against the primary checkout — not against whichever "
        "worktree the work is in. The run then succeeds against the wrong tree, and its output is "
        "the same shape as a right one.\n\n"
        "Say where it runs: open with `cd <path> &&`, or give absolute paths (and `--project` where "
        "the program takes one).\n"
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())
