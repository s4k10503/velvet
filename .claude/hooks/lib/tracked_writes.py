"""Which tracked repository files a Bash command names as the literal target of a write.

A guard over the editing tools is handed a `file_path`, so every write that reaches a file from the
shell is outside it — including, in this project's own session transcripts, edits to
`.claude/settings.json` and to the guards' own sources, which is the registration deciding what any
of them sees at all. Deciding whether an arbitrary command writes to the repository is the reading
`pr_body_of_another_branch.py` has been reduced for twice, and nothing here attempts it.

Read back over those transcripts, the writes reaching a file `git ls-files` names arrived in three
shapes: a redirect, an operand of an in-place `sed`, and the destination of a `cp` or an `mv`. So
the question here is narrowed to "is this literal operand a tracked file", which git answers
exactly. Where the shell has yet to expand the operand, nothing here resolves it.

That makes the reading an under-approximation, and a refusal built on it has to say so rather than
imply coverage: `LIMITS` is that sentence, owned here so two guards cannot state it two ways. What
it gives up is above all that unexpanded operand, and the directory a command moves into partway
through.

`tee` is the obvious fourth shape and is not read: two independent readings of those transcripts put
it at no tracked file at all. Nor is `git mv`: its destination is by definition a path git does not
yet track, so it wants a second criterion beside the one below rather than another spelling of it.
"""

import os
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import repository  # noqa: E402
from shell_commands import (SEPARATORS, UNRESOLVED_CD, command_segments, leading_cd,  # noqa: E402
                            leading_program, mask_shell_literals, program_invocations, tokens_of,
                            unexpanded)

# What a refusal built on this must admit to. Three shapes and a literal operand is the whole of it.
LIMITS = (
    "This reads three shapes carrying a literal operand — a redirect, an operand of an in-place "
    "`sed`, and the destination of a `cp` or an `mv`. A target the shell has yet to expand, a write "
    "made from inside a script or a program, and every other command that writes are not read at "
    "all, so nothing refusing here is claiming to have seen them."
)

# `>` and `>>` alone. `>&` and `<&` move a descriptor and `<` reads, so none of them writes a file.
# `&>` does, and is left out: over the transcripts above it named a tracked file no times at all.
WRITING_REDIRECT = re.compile(r"^\d*>>?(?!&)")

# sed options that swallow the token after them, so a script file's name is not offered as a file
# being edited. `-i` is not among them and does not need to be: whatever follows it is offered like
# any other operand, and an empty extension is dropped for being empty while a substitution
# expression is dropped for not being a path git names.
SED_VALUE_FLAGS = {"-e", "-f", "--expression", "--file"}


def _visible_segments(command):
    """The command's segments, minus the spans the mask replaced.

    Split as `shell_commands.command_segments` splits it; what is added is dropping a blanked span,
    which under that split is a segment of its own. A pull-request body written through a heredoc
    puts its markdown in one, and a blockquote there reads as a redirect onto the file its line
    names.
    """
    masked = mask_shell_literals(command)
    bounds, start = [], 0
    for index, character in enumerate(masked):
        if character in SEPARATORS:
            bounds.append((start, index))
            start = index + 1
    bounds.append((start, len(command)))
    return [normalised
            for first, last in bounds if masked[first:last].strip()
            for normalised in command_segments(command[first:last])]


def _redirect_targets(tokens):
    """Every operand a `>` or a `>>` in this segment writes to."""
    found = []
    index = 0
    while index < len(tokens):
        match = WRITING_REDIRECT.match(tokens[index])
        if match:
            attached = tokens[index][match.end():]
            if attached:
                found.append(attached)
            elif index + 1 < len(tokens):
                found.append(tokens[index + 1])
                index += 1
        index += 1
    return found


def _in_place(operands):
    """Whether sed's options name its in-place form."""
    for token in operands:
        if token.startswith("--"):
            if token == "--in-place" or token.startswith("--in-place="):
                return True
        # A script or a script file attached to its own option is not an option cluster, and reading
        # it as one makes any `sed` whose expression happens to contain an `i` in-place.
        elif token.startswith(("-e", "-f")):
            continue
        elif token.startswith("-") and "i" in token[1:]:
            return True
    return False


def _sed_targets(operands):
    """Every operand of an in-place `sed` that is not one of its options.

    The script is among them, because the flag reading above declines to say where it ends. Which of
    these is a path is left to the tracked-file test, which answers it exactly and answers no for a
    substitution expression.
    """
    if not _in_place(operands):
        return []
    found, skip = [], False
    for token in operands:
        if skip:
            skip = False
            continue
        if token.startswith("-") and len(token) > 1:
            skip = token in SED_VALUE_FLAGS
            continue
        if token:
            found.append(token)
    return found


def _copy_targets(operands):
    """Where a `cp` or an `mv` writes: its last operand, and each source placed under it.

    Both, because whether the last operand is the file written or the directory written into is
    decided by the filesystem rather than by the command, and the one that is not there is a path
    git tracks nothing at. `-t` moves the destination off the end, and that spelling is not read:
    a destination taken off the wrong end names a source, and a source is not written.
    """
    if any(token == "-t" or token.startswith("--target-directory") for token in operands):
        return []
    named = [token for token in operands if not token.startswith("-")]
    if len(named) < 2:
        return []
    sources, destination = named[:-1], named[-1]
    return [destination] + [os.path.join(destination, os.path.basename(source))
                            for source in sources if not unexpanded(source)]


def _moves(segments):
    """How many of these segments run `cd`."""
    return sum(1 for segment in segments
               for tokens in [tokens_of(segment)]
               for index in [leading_program(tokens)]
               if index < len(tokens) and tokens[index] == "cd")


def _base_directory(command, segments, cwd):
    """Where a relative operand is rooted, or None where nothing here can place one.

    `leading_cd` reads the move a command opens with, and a command that moves again after it has
    started running has left that directory by the time a later segment writes. Rooting that
    segment's operand at the event's own directory is how a write into a temporary tree reads as one
    onto the repository, so a second move gives up on relative operands instead — measured over this
    project's transcripts, `S=…; rm -rf $S; mkdir -p $S; cd $S; printf … > .gitignore` is the shape,
    and it is the repository's own `.gitignore` that the reading without this names.
    """
    moved = leading_cd(command)
    if moved is UNRESOLVED_CD or _moves(segments) > (0 if moved is None else 1):
        return None
    if moved is None:
        return cwd
    if os.path.isabs(moved):
        return moved
    return os.path.join(cwd, moved) if cwd else None


def _placed(target, base):
    """`target` as an absolute path, or None where nothing here can place it."""
    if not target or unexpanded(target) or target.startswith("~"):
        return None
    if os.path.isabs(target):
        return os.path.normpath(target)
    return os.path.normpath(os.path.join(base, target)) if base else None


def literal_write_targets(command, cwd):
    """Absolute paths this command names, literally, as somewhere it writes."""
    segments = _visible_segments(command)
    base = _base_directory(command, segments, cwd)
    candidates = []
    for segment in segments:
        candidates += _redirect_targets(tokens_of(segment))
        for operands in program_invocations(segment, "sed", ()):
            candidates += _sed_targets(operands)
        for program in ("cp", "mv"):
            for operands in program_invocations(segment, program, ()):
                candidates += _copy_targets(operands)

    found = []
    for candidate in candidates:
        placed = _placed(candidate, base)
        # A directory is where a write lands, never what it lands on, and one reaching the tracked
        # test below would be refused over a path nothing writes. The directory case in
        # `scripts/hooks/test_tracked_writes.py` holds git's half of that reason.
        if placed is not None and placed not in found and not os.path.isdir(placed):
            found.append(placed)
    return found


_GIT_RUNS = None


def _git_runs():
    """Whether git can be started at all, asked once."""
    global _GIT_RUNS
    if _GIT_RUNS is None:
        _GIT_RUNS = repository.git(["--version"], cwd=None, timeout=10) is not None
    return _GIT_RUNS


def _tracked(path):
    """Whether git names `path` a file it tracks.

    A git that cannot answer counts as yes, because a guard reading "I cannot tell" as "nothing to
    refuse" falls silent exactly where it is the only thing left. Being outside a repository is not
    that state and must not reach it: git reports it with the same failure it reports being unable
    to run with, so what separates the two is asking whether it runs.
    """
    parent = os.path.dirname(path)
    if not os.path.isdir(parent):
        return False
    answer = repository.git_answer(
        ["-C", parent, "ls-files", "--error-unmatch", "--", path], cwd=None, timeout=10)
    return True if answer.code == 0 else not _git_runs()


def tracked_writes(command, cwd):
    """The tracked repository files this command names as the literal target of a write."""
    return [path for path in literal_write_targets(command, cwd) if _tracked(path)]
