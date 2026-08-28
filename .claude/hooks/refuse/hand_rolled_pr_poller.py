#!/usr/bin/env python3
"""Refuse a backgrounded loop over the watcher's state while the heartbeat says one is watching.

`scripts/pr/settle.py watch` refuses a second `watch`, and that refusal reaches only a process that
spells itself `watch`. A loop doing the same job is not one, so nothing stopped several running at
once against the quota the watcher shares, on their own cycles, past the pull requests they waited
for. The advice existed on both sides of it — `settle.py`'s docstring says an instruction is
re-derived each time — and being read is all an instruction can do.

The subject is the watcher's state rather than the programs that touch it. A loop polling
`settle.py merge --dry-run` and a loop waiting on the ready file are the same duplication, and the
second calls nothing, so a sweep of the process table for `settle.py` leaves it running.
`scripts/pr/watcher_state.py` owns those file names.

Where the subject is looked for is the masked command, so a name a program is handed as data is not
one. What the shell will itself run is followed rather than left behind: a substitution's body, a
`sh -c` operand, an `eval` operand and `watch`'s command are all shell text, and each is taken apart
the same way. A poller written in another language is not shell text and is not followed.

Three narrowings beside that subject, and the refusal is wrong without any one of them:

- **backgrounded**, since a foreground loop blocks the caller and ends when the call does;
- **repeating**, since a one-shot `settle.py merge <n>` is the sanctioned way to merge, and a `for`
  over a list of pull requests terminates on its own — what is duplicative is the waiting;
- **a watcher**, since with nothing watching there is no second poller for this to be.
"""

import json
import os
import re
import sys
from pathlib import Path

HOOK_DIRECTORY = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HOOK_DIRECTORY / "lib"))
sys.path.insert(0, str(HOOK_DIRECTORY.parent.parent / "scripts" / "pr"))

from shell_commands import (  # noqa: E402
    ENV_ASSIGNMENT, LEADING_WORDS, command_segments, leading_program, mask_shell_literals)
from shell_commands import tokens_of, without_redirections  # noqa: E402
from watcher_state import HEARTBEAT, LOCK, READY_STATE, alive, unreadable_beat  # noqa: E402

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

# Read off text rather than off a segment's tokens. A `"$( ... )"` arrives as a single token carrying
# a whole pipeline, so a name compared per token never matches the program the shell runs inside it.
SUBJECT_PATTERN = re.compile(
    r"(?<![\w.~-])(" + "|".join(re.escape(name) for name in sorted(SUBJECTS)) + r")(?![\w.-])")

# Programs whose operand is a command they hand back to a shell.
SHELLS = {"bash", "dash", "ksh", "sh", "zsh"}
SHELL_COMMAND_FLAG = re.compile(r"^-[A-Za-z]*c$")
EVAL = "eval"

# A loop spelled as a program: what ends it is outside the command, the same as a condition is.
RE_RUNS = "watch"

# A loop whose end is a condition rather than a list. `for` is here for the header carrying no list,
# which is how an endless loop is spelled without a condition; `read` is what ends a `while` with its
# input rather than with a condition.
LOOP_HEADS = {"while", "until"}
LIST_LOOP = "for"
LIST_WORD = "in"
WALKS_INPUT = "read"

# Grouping that carries no program name. A segment keeps the ones inside it, and a command word
# behind an unstripped `(` reads as the parenthesis.
GROUPING = {"(", ")", "{", "}"}


def backgrounds(masked):
    """Whether an `&` in this text is the shell's own, rather than an `&&` or a redirection's."""
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


def substituted(text):
    """The outermost `$( … )` and backtick bodies, which the shell runs as commands of their own."""
    found = []
    index = 0
    while index < len(text):
        if text.startswith("$(", index):
            depth, scan = 1, index + 2
            while scan < len(text) and depth:
                depth += {"(": 1, ")": -1}.get(text[scan], 0)
                scan += 1
            found.append(text[index + 2:scan - 1 if depth == 0 else scan])
            index = scan
        elif text[index] == "`":
            end = text.find("`", index + 1)
            if end < 0:
                break
            found.append(text[index + 1:end])
            index = end + 1
        else:
            index += 1
    return found


def segment_tokens(segment):
    return without_redirections([token for token in tokens_of(segment) if token not in GROUPING])


def command_word(tokens):
    """Where the program a segment runs sits, past what `leading_program` skips."""
    return leading_program(tokens)


def loop_head(tokens):
    """Where a loop keyword sits in this segment, or -1.

    Read off the raw tokens rather than off `leading_program`, which skips a loop head along with
    every other word that may precede a command: this guard's subject is the keyword itself, and a
    reading that walks past it cannot see the repetition it exists to refuse.
    """
    for index, token in enumerate(tokens):
        if token in LOOP_HEADS:
            return index
        if not (ENV_ASSIGNMENT.match(token) or token in LEADING_WORDS):
            return -1
    return -1


def handed_to_a_shell(text):
    """Operands this hands to a shell to run, which are shell text rather than data."""
    found = []
    for segment in command_segments(text):
        tokens = segment_tokens(segment)
        index = command_word(tokens)
        if index >= len(tokens):
            continue
        program = os.path.basename(tokens[index])
        operands = tokens[index + 1:]
        if program in SHELLS:
            for position, token in enumerate(operands):
                if SHELL_COMMAND_FLAG.match(token):
                    found.extend(operands[position + 1:position + 2])
                    break
        elif program in (EVAL, RE_RUNS):
            found.append(" ".join(operands))
    return found


def shell_texts(command):
    """This command, then the shell text found inside it, a nesting level at a time.

    Every nested text is strictly shorter than the one it came out of — a substitution loses its
    delimiters, an operand loses the program that carries it — which is what ends the recursion.
    """
    yield command
    for nested in substituted(command) + handed_to_a_shell(command):
        if nested:
            yield from shell_texts(nested)


def detached(command):
    """Whether the shell backgrounds any part of this, so it outlives the call that started it."""
    return any(backgrounds(mask_shell_literals(text)) for text in shell_texts(command))


def repeats(command):
    for text in shell_texts(command):
        for segment in command_segments(text):
            tokens = segment_tokens(segment)
            head = leading_program(tokens)
            looped = loop_head(tokens)
            if looped >= 0:
                word = command_word(tokens)
                if word >= len(tokens) or tokens[word] != WALKS_INPUT:
                    return True
            if head >= len(tokens):
                continue
            if tokens[head] == LIST_LOOP and LIST_WORD not in tokens[head + 1:head + 3]:
                return True
            elif os.path.basename(tokens[head]) == RE_RUNS:
                return True
    return False


def subjects(command):
    """The watcher's programs and files this command names."""
    found = set()
    for text in shell_texts(command):
        masked = mask_shell_literals(text)
        found.update(match.group(1) for match in SUBJECT_PATTERN.finditer(masked))
    return sorted(found)


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
    # A heartbeat this cannot read is not an absent one. `watcher_state.unreadable_beat` separates
    # the two, and reading only `alive` here would let the poller through in that state.
    live = alive()
    if not found or not (live or unreadable_beat()):
        return 0

    sys.stderr.write(
        "Refusing a backgrounded loop over what the committed watcher already polls:\n\n"
        + "".join(f"  {name}\n" for name in found)
        + "\n"
        + ("A watcher is live and polling this on its own cycle, against the same rate limit.\n\n"
           if live else
           "A watcher is writing the heartbeat in a form this cannot read, and polling this on its "
           "own cycle against the same rate limit.\n\n")
        + "Do nothing and let the turn end: `.claude/hooks/stop/unsettled_pr.py` is what reports an "
        "open pull request that has not settled. Which ones the watcher has recorded ready is in:\n\n"
        f"  {READY_STATE}\n\n"
        "Merge one with:\n\n"
        "  python3 scripts/pr/settle.py merge <n>\n")
    return 2


if __name__ == "__main__":
    sys.exit(main())
