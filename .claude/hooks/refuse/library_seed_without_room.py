#!/usr/bin/env python3
"""Refuse a Library seed the disk has no room for, which otherwise wedges every agent at once.

A session running many agents seeds a Unity `Library` per worktree. Measured: ~90 worktrees at ~2.8 GB
each took `/System/Volumes/Data` to 121 MiB free of 460 GiB, and two agents reported themselves
blocked before anyone noticed. What a full disk does there is not loud: a Unity run that hits ENOSPC
writes no results XML, which reads exactly like a compile failure, and `base_red_check --lane csharp`
simply cannot copy its base tree.

Detection already existed — the SessionStart report prints the counts and the cleanup recipes, and it
did print them. Nothing acted, because acting was nobody's scheduled job. This is the same reading
placed where it stops the command rather than describing the state.

The floor is twice what is being copied, derived rather than chosen: one copy's worth to complete and
one to work in. A number would have to be re-picked as the project grows, and the thing it protects
is the copy.

`Library/` is regenerable whatever anyone is doing, so the remedy is always available: clear it out of
the worktrees that are not running Unity. What is NOT a safe reading is "this worktree has been quiet"
— with the disk full every worktree read as quiet while eight agents were demonstrably running,
because none of them could write.

Exit 2 refuses; exit 1 lets the tool through, so nothing here may raise.
"""

import json
import os
import re
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "lib"))

from shell_commands import (NAME_THE_TREE, UNPLACEABLE_MOVE, UNRESOLVED_CD,  # noqa: E402
                            command_directory, command_segments, tokens_of, unexpanded)

HOOK_TOOLS = {"Bash"}

# The copy programs a seed is spelled with. `rsync` is what CLAUDE.md documents; `cp` is what somebody
# reaches for when they have forgotten the exclusion.
COPIERS = {"rsync", "cp", "ditto"}

# A destination that is a Library, however it is spelled: `Library`, `Library/`, `<path>/Library`.
LIBRARY = re.compile(r"(^|/)Library/?$")

# An operand the shell has not expanded names no directory this can size, and every reading of a
# literal fails — which here is the pass, so the check would silently not happen.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'rsync -a "$OTHER/Library/" Library/'

# This reads the filesystem and neither git nor gh, so the unreadable-state question has no subject
# here. What it does when its own reading fails is stated where it happens: a source it cannot walk
# and a `df` that will not answer both stand down, because the copy fails on its own if there is
# genuinely no room, and neither absence says anything about the disk.
UNREADABLE_POLICY = "none"
UNREADABLE_PROBE = {"command": "rsync -a /nowhere/Library/ Library/"}


def seeds(tokens):
    """(source, destination) where this segment copies something into a Library, else None."""
    index = 0
    while index < len(tokens) and (
            "=" in tokens[index].split("/")[0] and not tokens[index].startswith("-")):
        index += 1
    if index >= len(tokens) or os.path.basename(tokens[index]) not in COPIERS:
        return None
    operands = [token for token in tokens[index + 1:] if not token.startswith("-")]
    if len(operands) < 2:
        return None
    destination = operands[-1]
    if not LIBRARY.search(destination.rstrip("/") or destination):
        return None
    return operands[-2], destination


def directory_size(path):
    """Bytes under `path`, or None where it cannot be walked."""
    total = 0
    try:
        for root, _, names in os.walk(path):
            for name in names:
                try:
                    total += os.lstat(os.path.join(root, name)).st_size
                except OSError:
                    continue
    except OSError:
        return None
    return total


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    try:
        if not isinstance(event, dict) or event.get("tool_name") not in HOOK_TOOLS:
            return 0
        command = event.get("tool_input", {}).get("command") or ""
        if not isinstance(command, str):
            return 0
        # Where the copy lands, which is what decides the filesystem it has to fit on: a relative
        # destination is placed by the command's own move, not by where the tool call started.
        cwd = command_directory(command, event.get("cwd") or ".")
        if cwd is UNRESOLVED_CD:
            if not any(seeds(tokens_of(segment)) for segment in command_segments(command)):
                return 0
            sys.stderr.write("Refusing this Library seed: which filesystem it would land on could "
                             f"not be read.\n\n{UNPLACEABLE_MOVE}\n\n{NAME_THE_TREE}\n")
            return 2

        for segment in command_segments(command):
            found = seeds(tokens_of(segment))
            if found is None:
                continue
            source, destination = found
            if unexpanded(source) or unexpanded(destination):
                sys.stderr.write(
                    "Refusing this Library seed: an operand is still unexpanded, so how much this "
                    "would\ncopy cannot be read here.\n\n"
                    "Spell the paths out.\n")
                return 2
            wanted = directory_size(os.path.expanduser(source))
            if wanted is None:
                return 0
            try:
                free = shutil.disk_usage(cwd).free
            except OSError:
                return 0
            if free >= wanted * 2:
                continue
            sys.stderr.write(
                "Refusing this Library seed: the disk has no room for it and a copy's worth to work "
                "in.\n\n"
                f"  copying   {wanted / 2**30:.1f} GiB\n"
                f"  free      {free / 2**30:.1f} GiB\n"
                f"  wanted    {wanted * 2 / 2**30:.1f} GiB\n\n"
                "A Unity run that hits ENOSPC writes no results XML, which reads exactly like a "
                "compile\nfailure, so filling the disk here wedges every agent quietly. Reclaim "
                "first — a Library is\nregenerable, and one under a worktree no Unity is running in "
                "is always safe to remove:\n\n"
                "  find /private/tmp/claude-* -maxdepth 6 -type d -name Library\n\n"
                "Read that against a Unity process list rather than against how quiet a worktree "
                "looks: with\nthe disk full every worktree looks quiet, because none of them can "
                "write.\n")
            return 2
        return 0
    except Exception as failure:  # noqa: BLE001 - a raise here turns the guard off silently
        print(f"library_seed_without_room: {failure}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
