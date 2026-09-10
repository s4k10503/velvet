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

`--web` hands the fields to the browser form, where they can be set, so it is left alone, and so are
`--help` and its short `-h`, which open nothing.
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import (UNPLACEABLE_MOVE, UNRESOLVED_CD, command_directory,
                            program_invocations)
import repository


HOOK_TOOLS = {"Bash"}

# Anchored at a command position — start of input, or after a separator or newline — so the same
# text quoted inside an argument or named in a body does not trip it. This file argues for a
# refusal by naming the command it refuses, which is exactly the text that would.
LABEL_FLAGS = ("--label", "-l")
ASSIGNEE_FLAGS = ("--assignee", "-a")


# --web hands the fields to the browser form, where they can be set; --help opens nothing. Named
# once so GuardCommandCoverageTests asks the guard rather than restating the rule — the copy in that
# table listed only --web, and a row added for --help disagreed with the guard that had exempted it.
EXEMPT_FLAGS = ("--web", "--help", "-h")


def exempt(operands):
    """Whether this invocation creates nothing a label could go on."""
    return any(token in EXEMPT_FLAGS for token in operands)


def carries(operands, flags):
    """Whether one of `flags` is present as a flag rather than as text inside an argument."""
    for token in operands:
        if token.partition("=")[0] in flags:
            return True
    return False


# `new` is gh's own alias for `create`, on both subcommands -- measured, `gh pr new --help` and
# `gh issue new --help` each print a usage. A guard that claims one spelling is skippable by typing
# the other, which for this one costs the label and the assignee and for its neighbour costs the only
# place a mutation receipt is ever asked for.
CREATE_SPELLINGS = ("create", "new")


def creations(command):
    """(kind, operands) for each `gh issue create` / `gh pr create` the command runs."""
    found = []
    for kind in ("issue", "pr"):
        for spelling in CREATE_SPELLINGS:
            for operands in program_invocations(command, "gh", (kind, spelling)):
                found.append((kind, operands))
    return found


# The verdict is whether the command carries --label and --assignee, which is its own text. The
# repository is read only to list the labels the refusal offers, so an unexpanded operand costs a
# less helpful message and never a missed check.
UNEXPANDED_POLICY = "allow"
UNEXPANDED_PROBE = 'gh issue create --title $T --label bug --assignee @me'

UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "gh issue create --title probe"}


def labels(cwd):
    """The repository's label names, or None where gh did not answer.

    Told apart from the empty list, which is a repository that has no labels yet. Answering that of a
    reading that failed offers `gh label create` to someone whose labels are all already there.
    """
    listing = repository.gh(["label", "list", "--json", "name", "--jq", ".[].name"], cwd=cwd)
    return None if listing is None else [name for name in listing.splitlines() if name.strip()]


def label_line(where):
    """The refusal's line about which labels exist, or about why it does not say."""
    if where is UNRESOLVED_CD:
        return "  not read — " + UNPLACEABLE_MOVE
    found = labels(where)
    if found is None:
        return "  not read — gh did not answer."
    return "  " + (", ".join(found) or "(none yet — `gh label create` makes one)")


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0
    command = event.get("tool_input", {}).get("command", "")
    # Flags read off tokens rather than searched for in the whole command: the flag name occurring
    # as delimited text inside a --title or --body satisfied the guard, and an unlabelled issue was
    # created on the strength of prose describing one.
    missing = []
    kind = ""
    for kind, operands in creations(command):
        if exempt(operands):
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

    # The refusal stands on the command's own text, so an unplaced move costs the listing below and
    # not the verdict.
    where = command_directory(command, event.get("cwd") or ".")
    reason = [
        f"Refusing `gh {kind} create`: it sets no {' and no '.join(missing)}.",
        "",
        "An unlabelled backlog cannot be filtered, so every entry reads the same from the list and",
        "the only way to know what one is, is to open it.",
        "",
        "Labels on this repository:",
        label_line(where),
    ]
    if "--assignee" in missing:
        reason += ["", "`--assignee @me` is the usual answer."]
    print("\n".join(reason), file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
