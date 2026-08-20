#!/usr/bin/env python3
"""Refuse a backgrounded loop over the watcher's state while a watcher is live.

`scripts/pr/settle.py watch` refuses a second `watch`, and that refusal reaches only a process that
spells itself `watch`. A loop doing the same job is not one, so nothing stopped several running at
once against the quota the watcher shares, on their own cycles, past the pull requests they waited
for. The advice existed on both sides of it — `settle.py`'s docstring says an instruction is
re-derived each time — and being read is all an instruction can do.

The subject is the watcher's state rather than the programs that touch it. A loop polling
`settle.py merge --dry-run` and a loop waiting on the ready file are the same duplication, and the
second calls nothing, so a sweep of the process table for `settle.py` leaves it running.
`scripts/pr/watcher_state.py` owns those file names.

Three narrowings beside that subject, and the refusal is wrong without any one of them:

- **backgrounded**, since a foreground loop blocks the caller and ends when the call does;
- **repeating**, since a one-shot `settle.py merge <n>` is the sanctioned way to merge, and a `for`
  over a list of pull requests terminates on its own — what is duplicative is the waiting;
- **a live heartbeat**, since with nothing watching there is no second poller for this to be.
"""

import json
import os
import re
import sys
from pathlib import Path

HOOK_DIRECTORY = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HOOK_DIRECTORY / "lib"))
sys.path.insert(0, str(HOOK_DIRECTORY.parent.parent / "scripts" / "pr"))

from shell_commands import command_segments, leading_program, mask_shell_literals  # noqa: E402
from shell_commands import tokens_of  # noqa: E402
from watcher_state import HEARTBEAT, LOCK, READY_STATE, alive  # noqa: E402

HOOK_TOOLS = {"Bash"}

# A subject behind a variable is not resolved back. Refusing on the presence of one instead would
# reach the loop counter and the condition's substitution, which the waits this must allow are
# spelled with as much as the polls it must refuse.
UNEXPANDED_POLICY = "allow"
UNEXPANDED_PROBE = 'until $POLLER merge 702 --dry-run; do sleep 60; done &'

# Neither git nor gh is asked anything. The heartbeat is a file, and its absence is a live answer
# rather than a failed reading.
UNREADABLE_POLICY = "none"
UNREADABLE_PROBE = {"command": "until gh pr view 1; do sleep 60; done", "run_in_background": True}

# Matched by file name, so a wait reaching one of these through `~` or `$HOME` is still reaching it.
WATCHER_FILES = {path.name for path in (HEARTBEAT, LOCK, READY_STATE)}

SUBJECTS = {"gh", "settle.py"} | WATCHER_FILES

# Read off the command as written rather than off its segments' tokens. A `"$( ... )"` arrives as a
# single token carrying a whole pipeline, so a name compared per token never matches the program the
# shell will run inside it.
SUBJECT_PATTERN = re.compile(
    r"(?<![\w.~-])(" + "|".join(re.escape(name) for name in sorted(SUBJECTS)) + r")(?![\w.-])")

# A loop whose end is a condition rather than a list. `for` is left out because it walks a list and
# stops; `read` is left out of these for the same reason, since it ends with its input.
LOOP_HEADS = {"while", "until"}
WALKS_INPUT = "read"

# Grouping that carries no program name. A segment keeps the ones inside it, and a command word
# behind an unstripped `(` reads as the parenthesis.
GROUPING = {"(", ")", "{", "}"}


def detached(command):
    """Whether the shell backgrounds any part of this, so it outlives the call that started it."""
    masked = mask_shell_literals(command)
    index = 0
    while True:
        index = masked.find("&", index)
        if index < 0:
            return False
        after = masked[index + 1:index + 2]
        if after == "&":
            index += 2
        elif after == ">" or masked[:index].rstrip()[-1:] in (">", "<"):
            index += 1
        else:
            return True


def repeats(command):
    """Whether the command comes back rather than running once."""
    for segment in command_segments(command):
        tokens = [token for token in tokens_of(segment) if token not in GROUPING]
        head = leading_program(tokens)
        if head >= len(tokens):
            continue
        if os.path.basename(tokens[head]) == "sleep":
            return True
        if tokens[head] in LOOP_HEADS and tokens[head + 1:head + 2] != [WALKS_INPUT]:
            return True
    return False


def subjects(command):
    """The watcher's programs and files this command names."""
    return sorted({found.group(1) for found in SUBJECT_PATTERN.finditer(command)})


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0

    tool_input = event.get("tool_input", {})
    command = tool_input.get("command", "")
    if not (tool_input.get("run_in_background") or detached(command)):
        return 0
    if not repeats(command):
        return 0
    found = subjects(command)
    if not found or not alive():
        return 0

    sys.stderr.write(
        "Refusing a backgrounded loop over what the committed watcher already polls:\n\n"
        + "".join(f"  {name}\n" for name in found)
        + "\nA watcher is live and polling this on its own cycle, against the same rate limit.\n\n"
        "Do nothing and let the turn end: `.claude/hooks/stop/unsettled_pr.py` is what reports an "
        "open pull request that has not settled. Which ones the watcher has recorded ready is in:\n\n"
        f"  {READY_STATE}\n\n"
        "Merge one with:\n\n"
        "  python3 scripts/pr/settle.py merge <n>\n")
    return 2


if __name__ == "__main__":
    sys.exit(main())
