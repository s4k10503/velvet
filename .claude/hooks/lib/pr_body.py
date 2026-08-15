"""Where a pull-request body is in a `gh pr` invocation, shared by the guards that read one.

Two guards now read the same description, and the reading is not the obvious one. gh takes a body
under four flags and each flag's value in three spellings, one of which — a short flag carrying its
value attached — was missed once and let `-F/tmp/pr-body.md` past every check with the invocation
claimed and no body found. Held in one place so the next spelling is added once rather than to
whichever guard its author had open.

What is NOT shared is what to do about a body that cannot be read. `read_body_file` names the
obstruction and stops there: one guard declines each of them with its own remedy, and the other has
nothing to say about a description it never sees.
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

# Why a body could not be read, decided in this order.
UNEXPANDED_PATH = "unexpanded-path"
STDIN = "stdin"
RELATIVE_AFTER_MOVE = "relative-after-move"
MISSING = "missing"
UNREADABLE = "unreadable"

MOVERS = {"cd", "pushd", "popd"}


def valued(operands, flags):
    """The value given to one of `flags`, or None.

    Three spellings, because gh takes all three: `--flag v`, `--flag=v`, and `-Fv` — a short flag
    carrying its value attached. The last was missed, so `-F/tmp/pr-body.md` reached none of the
    checks that read this: the invocation was claimed, no body was found, and it took the exemption
    meant for a body that cannot be read. That is the accident these guards exist for, one character
    apart.

    A name is read off any token, including one that is another option's value, so `-t -Fx` gives a
    body file of `x` and the file asked about is not the one gh will post. Separating the two needs a
    mirror of which of gh's options take a value, and an unpinned mirror drifts.
    """
    short = tuple(flag for flag in flags if len(flag) == 2 and flag.startswith("-") and flag[1] != "-")
    for index, token in enumerate(operands):
        name, sep, inline = token.partition("=")
        if name in flags:
            if sep:
                return inline
            if index + 1 < len(operands):
                return operands[index + 1]
            return None
        for flag in short:
            if len(token) > 2 and token.startswith(flag):
                return token[2:]
    return None


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
