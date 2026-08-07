#!/usr/bin/env python3
"""Refuse `gh issue create` / `gh pr create` that set no label, and an issue that sets no assignee.

Every issue on this repository was opened with none of either. Nine of them, before a person
noticed rather than a check. An unlabelled backlog cannot be filtered, so "what is broken" and
"what is a tooling gap" and "what is blocked on a platform decision" read the same from the
list, and the only way to tell is to open each one.

A hook rather than `.github/ISSUE_TEMPLATE`, because a template sets defaults for the web form
and does nothing for `gh issue create`, which is how every one of these was filed. A template
asks; this refuses.

Assignee is required on an issue and not on a pull request: a pull request already records its
author, so a self-assignment adds nothing.

`--web` hands the fields to the browser form, where they can be set, so it is left alone.
"""

import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import program_invocations

# Anchored at a command position — start of input, or after a separator or newline — so the same
# text quoted inside an argument or named in a body does not trip it. This file argues for a
# refusal by naming the command it refuses, which is exactly the text that would.
LABEL_FLAGS = ("--label", "-l")
ASSIGNEE_FLAGS = ("--assignee", "-a")


def carries(operands, flags):
    """Whether one of `flags` is present as a flag rather than as text inside an argument."""
    for token in operands:
        if token.partition("=")[0] in flags:
            return True
    return False


def creations(command):
    """(kind, operands) for each `gh issue create` / `gh pr create` the command runs."""
    found = []
    for kind in ("issue", "pr"):
        for operands in program_invocations(command, "gh", (kind, "create")):
            found.append((kind, operands))
    return found


# The verdict is whether the command carries --label and --assignee, which is its own text. The
# repository is read only to list the labels the refusal offers, so an unexpanded operand costs a
# less helpful message and never a missed check.
UNEXPANDED_POLICY = "allow"
UNEXPANDED_PROBE = 'gh issue create --title $T --label bug --assignee @me'


def labels(cwd):
    listing = subprocess.run(
        ["gh", "label", "list", "--json", "name", "--jq", ".[].name"],
        capture_output=True, text=True, cwd=cwd)
    return [name for name in listing.stdout.splitlines() if name.strip()]


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") != "Bash":
        return 0
    command = event.get("tool_input", {}).get("command", "")
    # Flags read off tokens rather than searched for in the whole command: the flag name occurring
    # as delimited text inside a --title or --body satisfied the guard, and an unlabelled issue was
    # created on the strength of prose describing one.
    missing = []
    kind = ""
    for kind, operands in creations(command):
        if "--web" in operands:
            continue
        missing = []
        if not carries(operands, LABEL_FLAGS):
            missing.append("--label")
        if kind == "issue" and not carries(operands, ASSIGNEE_FLAGS):
            missing.append("--assignee")
        if missing:
            break
    if not missing:
        return 0

    cwd = event.get("cwd") or "."
    reason = [
        f"Refusing `gh {kind} create`: it sets no {' and no '.join(missing)}.",
        "",
        "An unlabelled backlog cannot be filtered, so every entry reads the same from the list and",
        "the only way to know what one is, is to open it.",
        "",
        "Labels on this repository:",
        "  " + (", ".join(labels(cwd)) or "(none yet — `gh label create` makes one)"),
    ]
    if "--assignee" in missing:
        reason += ["", "`--assignee @me` is the usual answer."]
    print("\n".join(reason), file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
