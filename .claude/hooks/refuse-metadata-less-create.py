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
import re
import subprocess
import sys

# Anchored at a command position — start of input, or after a separator or newline — so the same
# text quoted inside an argument or named in a body does not trip it. This file argues for a
# refusal by naming the command it refuses, which is exactly the text that would.
CREATE = re.compile(r"(?:^|[;&|]|\n)\s*gh\s+(issue|pr)\s+create\b")

LABEL = re.compile(r"(?:^|\s)(?:--label(?:=|\s)|-l\s)")
ASSIGNEE = re.compile(r"(?:^|\s)(?:--assignee(?:=|\s)|-a\s)")
WEB = re.compile(r"(?:^|\s)--web(?:\s|$)")


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
    match = CREATE.search(command)
    if not match or WEB.search(command):
        return 0

    kind = match.group(1)
    missing = []
    if not LABEL.search(command):
        missing.append("--label")
    if kind == "issue" and not ASSIGNEE.search(command):
        missing.append("--assignee")
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
