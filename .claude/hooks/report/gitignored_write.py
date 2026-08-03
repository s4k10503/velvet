#!/usr/bin/env python3
"""Report a just-written file that .gitignore excludes.

An excluded file never reaches a fresh clone, yet every local run keeps passing on the copy that is
present here — so the suite that would have caught the gap is the one that stops existing in CI. That
is how the generator solution came to have no test coverage at all: nothing downstream can see it,
because the failing configuration is the one nobody runs. `git check-ignore` answers it at the moment
the file appears, which is the only cheap place to ask.
"""

import json
import os
import subprocess
import sys

# Build output is excluded on purpose; only a file that wants tracking is worth interrupting for.
TRACKABLE_SUFFIXES = (
    ".cs", ".uss", ".uxml", ".asmdef", ".csproj", ".sln",
    ".md", ".json", ".yml", ".yaml", ".sh", ".ps1", ".py",
)


def main():
    try:
        payload = json.load(sys.stdin)
    except ValueError:
        return 0

    path = (payload.get("tool_input") or {}).get("file_path")
    if not path or not path.endswith(TRACKABLE_SUFFIXES):
        return 0

    # Ask the tree that owns the FILE, not the session's cwd and not CLAUDE_PROJECT_DIR. An agent is
    # given the main checkout as its cwd and writes into a worktree under .claude/worktrees/, which the
    # main checkout excludes — so consulting either one reports every file the agent creates as
    # unreachable by CI while its own repository stages it normally. Fixing this by preferring cwd
    # addressed the wrong half and left the common case reporting a false positive on every write.
    root = subprocess.run(
        ["git", "-C", os.path.dirname(path) or ".", "rev-parse", "--show-toplevel"],
        stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=10,
    ).stdout.decode().strip() or payload.get("cwd") or os.environ.get("CLAUDE_PROJECT_DIR") or "."
    try:
        check = subprocess.run(
            ["git", "-C", root, "check-ignore", "-v", "--", path],
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
        )
    except OSError:
        return 0

    # Anything but a match — no git, a path outside the work tree, a tracked file — is not the case
    # this asks about, and staying silent is what keeps the hook usable on every write.
    if check.returncode != 0:
        return 0

    rule = check.stdout.decode("utf-8", "replace").split("\t")[0].strip()
    detail = (
        "`{}` is excluded by .gitignore ({}), so a fresh clone will not have it and CI cannot see it "
        "— while every local run keeps passing on the copy present here. The write itself succeeded. "
        "Decide which is true before continuing: the ignore rule is too broad and wants narrowing, or "
        "the file is genuinely generated and belongs nowhere. Force-adding it without narrowing the "
        "rule leaves the trap set for the next file.".format(path, rule)
    )
    # Two audiences, two channels, per the hook output spec. `additionalContext` is injected as a
    # system reminder and deliberately does not appear in the transcript; `systemMessage` is what the
    # person watching sees. Exit 2 is not an option here — PostToolUse cannot block, because the tool
    # has already run, so the spec ignores it.
    json.dump(
        {
            "systemMessage": "gitignored write — {} matches {}".format(path, rule),
            "hookSpecificOutput": {
                "hookEventName": "PostToolUse",
                "additionalContext": detail,
            },
        },
        sys.stdout,
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
