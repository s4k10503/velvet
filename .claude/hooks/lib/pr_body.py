"""Where a pull-request body is in a `gh pr` invocation, shared by the guards that read one.

Two guards now read the same description, and the reading is not the obvious one. gh takes a body
under four flags and each flag's value in three spellings, one of which — a short flag carrying its
value attached — was missed once and let `-F/tmp/pr-body.md` past every check with the invocation
claimed and no body found. Held in one place so the next spelling is added once rather than to
whichever guard its author had open.

What is NOT shared is what to do about a body that cannot be read. `read_body_file` names the
obstruction and stops there: each guard decides whether another guard covers that command or the
unreadable body must be refused here.
"""

import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from shell_commands import command_segments, leading_program, program_invocations, tokens_of, unexpanded

BODY_FILE_FLAGS = ("--body-file", "-F")
BODY_FLAGS = ("--body", "-b")
# None of these opens or updates a pull request, so none of them posts a description.
EXEMPT_FLAGS = ("--dry-run", "--help", "-h")

VALUE_FLAGS = {
    "--add-assignee", "--add-label", "--add-project", "--add-reviewer", "--assignee", "-a",
    "--base", "-B", "--body", "-b", "--body-file", "-F", "--head", "-H", "--label", "-l",
    "--milestone", "-m", "--project", "-p", "--recover", "--remove-assignee",
    "--remove-label", "--remove-project", "--remove-reviewer", "--repo", "-R", "--reviewer",
    "-r", "--template", "-T", "--title", "-t",
}

# Why a body could not be read, decided in this order.
UNEXPANDED_PATH = "unexpanded-path"
STDIN = "stdin"
RELATIVE_AFTER_MOVE = "relative-after-move"
MISSING = "missing"
UNREADABLE = "unreadable"

MOVERS = {"cd", "pushd", "popd"}


def options(operands):
    """Every parsed (option, value), preserving order and excluding positional operands."""
    found = []
    index = 0
    while index < len(operands):
        token = operands[index]
        if token == "--":
            break
        name, separator, inline = token.partition("=")
        if name in VALUE_FLAGS:
            if separator:
                found.append((name, inline))
                index += 1
            else:
                value = operands[index + 1] if index + 1 < len(operands) else None
                found.append((name, value))
                index += 2
            continue
        matching = next((flag for flag in VALUE_FLAGS
                         if len(flag) == 2 and token.startswith(flag) and len(token) > 2), None)
        if matching is not None:
            found.append((matching, token[2:]))
        elif token.startswith("-"):
            found.append((token, None))
        index += 1
    return found


def valued(operands, flags):
    """The last value given to one of `flags`, or None.

    Three spellings, because gh takes all three: `--flag v`, `--flag=v`, and `-Fv` — a short flag
    carrying its value attached. The last was missed, so `-F/tmp/pr-body.md` reached none of the
    checks that read this: the invocation was claimed, no body was found, and it took the exemption
    meant for a body that cannot be read. That is the accident these guards exist for, one character
    apart.

    Repeated scalar options use their last value. Options are parsed before names are matched so a
    value belonging to another option is not treated as a body flag.
    """
    values = [value for name, value in options(operands) if name in flags]
    return values[-1] if values else None


def exempted(operands):
    """Whether the invocation carries an exemption as an option rather than another option's value."""
    return any(name in EXEMPT_FLAGS for name, _ in options(operands))


def moves_directory(segment):
    """Whether this segment changes the directory a later segment runs in.

    The command word is read the way shell_commands reads one, past `then`/`do`, `builtin` and an
    environment assignment: reading tokens[0] instead missed `if true; then cd /tmp; fi`, which is
    the lib's own documented reason for having leading_program at all.
    """
    tokens = tokens_of(segment)
    index = leading_program(tokens)
    if index >= len(tokens):
        return False
    word = os.path.basename(tokens[index])
    return word in MOVERS


def invocations(command, *word_sets):
    """Every (words, operands, after_a_move) in the command running `gh` with one of `word_sets`.

    `after_a_move` rides along because a relative body path means one file to a guard reading it here
    and another to a `gh` that an earlier segment moved, and only the caller knows whether that
    matters to the question it is asking.
    """
    found = []
    moved = False
    for segment in command_segments(command):
        for words in word_sets:
            for operands in program_invocations(segment, "gh", words):
                found.append((words, operands, moved))
        moved = moved or moves_directory(segment)
    return found


def read_body_file(path, cwd, after_a_move):
    """(text, None) when the file can be read, else (None, one of the obstructions above)."""
    if unexpanded(path):
        return None, UNEXPANDED_PATH
    if path == "-":
        return None, STDIN
    if after_a_move and not os.path.isabs(path):
        return None, RELATIVE_AFTER_MOVE
    resolved = Path(path) if os.path.isabs(path) else Path(cwd) / path
    if not resolved.exists():
        return None, MISSING
    try:
        return resolved.read_text(encoding="utf-8", errors="replace"), None
    except OSError:
        return None, UNREADABLE
