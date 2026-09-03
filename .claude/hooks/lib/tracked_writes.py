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
imply coverage: `UNREAD` is the list of what it gives up and `LIMITS` the sentence built from it,
owned here so two guards cannot state it two ways.

One shape that was on that list is now read instead, along with a here-string that never reached
it, and closing them meant changing `mask_shell_literals`, which every caller of `command_segments`
shares. What decided it was measured on this project's transcripts rather than argued: over those,
the git subcommands the change makes newly visible are `log`, `push`, `status`, `merge` and `diff`,
and none is in the guarded set of `add`, `checkout`, `commit`, `stage`, `stash` and `switch` -- a
`git add` or a `git stash` written there does become visible and is refused, correctly, but none
occurs. Against that, the change corrects `git commit`'s operand list in every reading of it that
moves, and the two verdicts that move both move from refusal to allowing, one of them a commit
refused over an unexpanded operand belonging to a command three further along the line.

The last of those is not this reading's own doing and is not fixable here. `mask_shell_literals`
does not read a comment, so an apostrophe in one opens a quote span that swallows the newline and
every line after it -- measured, a following `printf x > notes.md` is then named by nothing, and so
is a following `git add -A` for the guards that read git. Blanking comments there does fix it and
costs more than it buys: the mask is where `command_segments` finds its boundaries and it slices the
ORIGINAL text, so a comment that swallows a `;` puts its own words into the preceding command's
operand list. Constructed commands move guards' verdicts both ways from there -- a flag inside a
comment satisfying one, a name inside a comment refused by another -- against a class `main` gets
wrong in exactly the same way. `;#` and `|#` reach it too, and a comment carrying none of the shapes
`UNREAD` names does not.

`tee` is the obvious fourth shape and is not read: two independent readings of those transcripts put
it at no tracked file at all. Nor is `git mv`, which wants a criterion of its own rather than another
spelling of the one below: its destination is ordinarily a path git does not yet track, and where
`-f` overwrites one that it does, what git tracks is what the move is about rather than a test that
separates it from a scratch write.
"""

import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import pr_body  # noqa: E402
import repository  # noqa: E402
from shell_commands import (SEPARATORS, UNRESOLVED_CD, command_segments, leading_cd,  # noqa: E402
                            mask_shell_literals, program_invocations, tokens_of, unexpanded,
                            without_redirections)

# Each gap the reading leaves. `LIMITS` is built from this so the sentence cannot name fewer of them
# than the tuple carries; what stops the TUPLE naming fewer than the reading has is the suite, which
# spells them out on its own side and counts them, a comparison against this tuple being satisfied by
# however many it has come to hold. Every entry was measured against the reading rather than guessed.
UNREAD = (
    "a target the shell has yet to expand",
    "a directory the command moves into partway through",
    "`>&`, which writes the file it names",
    "`>|`, whose bar the segment split takes for a separator",
    "a destination `cp -t` takes off the end of the operands",
    "`tee`, and `git mv`",
    "a writing command reached through another, as `xargs` and `sudo` reach one",
    "a write made from inside a script or a program",
    "a command below a comment carrying an apostrophe, an unmatched quote, a line continuation "
    "or a heredoc opener",
)

# What a refusal built on this must admit to. Three shapes and a literal operand is the whole of it.
LIMITS = (
    "This reads three shapes carrying a literal operand — a redirect, an operand of an in-place "
    "`sed`, and the destination of a `cp` or an `mv`. Not read at all, so that nothing refusing "
    "here is claiming to have seen them: " + ", ".join(UNREAD) + ", and every other command that "
    "writes."
)

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


def _redirect_targets(segment):
    """The operands a `>` or a `>>` in this segment writes to, less the spellings named below.

    The operator is located in the masked text and the operand read out of the original, because a
    token has lost its quotes by the time `shlex` hands it back: a shell word that IS `>` --
    `grep -n '>' CHANGELOG.md` -- is indistinguishable there from the operator, and the word after
    it then reads as a file being written. That refusal cannot be argued with, since the command
    writes nothing for its author to point at.
    """
    # The descriptor spellings need no branch, because the split has already decided them. `&` is a
    # separator, so `2>&1` leaves a `>` at a segment's end with no operand after it and yields
    # nothing; `&>` opens a segment on `>` and is read like any other write; `>&` leaves that same
    # trailing `>`, so a `make >& log`, which does write the file, is missed rather than refused --
    # the direction an under-approximation is allowed to be wrong in. A quoted `&` is not a
    # separator and does survive into a segment, which changes none of the above: it is blanked in
    # the mask this scans -- which holds because the opener's tail is lexed rather than skipped.
    masked = mask_shell_literals(segment)
    found = []
    index = 0
    while index < len(masked):
        # A `#` opening a word comments out the rest of the line, and an arrow in one -- `notes.md
        # -> CONTRIBUTING.md` -- is otherwise read as a redirect onto the name after it, which is
        # the refusal this reading calls unanswerable. The rule is here rather than in
        # `mask_shell_literals` because blanking a comment there removes the separators that ended
        # the span, and `command_segments` slices the original: the comment's own words then land in
        # the preceding command's operand list, which moves guards' verdicts both ways on commands
        # a person writes. `UNREAD` states what that leaves.
        if masked[index] == "#" and (index == 0 or masked[index - 1] in " \t"):
            break
        if masked[index] != ">" or (index and masked[index - 1] in "<>"):
            index += 1
            continue
        end = index + 2 if masked.startswith(">>", index) else index + 1
        # Skipped in the segment, not in the mask: a quoted or escaped operand is blanked there, so
        # a skip that reads the mask walks straight over it and off the end of the line.
        while end < len(segment) and segment[end] in " \t":
            end += 1
        operand = tokens_of(segment[end:])
        if operand:
            found.append(operand[0])
        index = end
    return found


def _comment_opens_at(segment):
    """Where this segment's comment starts, or None if no `#` in it starts one.

    Asked of the mask, which is the one place that already knows a quote from a bare character: an
    unquoted `#` survives it, a quoted one is blanked. Asking the token list instead cannot tell
    `cp a.md b.md '#c'`, which writes `#c`, from a trailing comment -- and dropping the last operand
    there makes a SOURCE the destination, which is a refusal over a file nothing writes.

    The position rather than a yes: the caller needs it to count, and a predicate that knows where
    the comment is and answers only whether there is one forces a cut on the wrong term.
    """
    masked = mask_shell_literals(segment)
    for index, character in enumerate(masked):
        if character == "#" and (index == 0 or masked[index - 1] in " \t"):
            return index
    return None


def _before_any_comment(operands, segment):
    """The operands this segment carries ahead of its comment, where it has one.

    `tokens_of` reads the original text and is handed `comments=False`, so a trailing comment's
    words arrive as operands and the last of them reads as a destination -- which named a file the
    command does not touch. Python's own comment mode is not the fix: measured, `shlex` with
    `comments=True` cuts mid-word, turning `note#1.md` into `note` and `a#b` into `a`, where the
    shell keeps both whole.
    """
    at = _comment_opens_at(segment)
    if at is None:
        return operands
    # Counted from the text rather than matched on a leading `#`, because an operand may begin with
    # one: `cp a.md '#c' # note` writes `#c`, and cutting at the first `#`-initial token drops the
    # write along with the comment.
    #
    # Counted over the SAME reading the operands came from. `program_invocations` hands on
    # `without_redirections(tokens_of(segment))`, so counting the unfiltered list charges a real
    # operand for every comment word that reads as a redirection -- `# keep > 2 copies` took two.
    # Safe because the only caller reaches here through `program_invocations`, which skips a segment
    # `tokens_of` could not read: the whole tokenises, and a prefix ending before a bare `#` cannot
    # be the half of a quote that did not.
    dropped = (len(without_redirections(tokens_of(segment)))
               - len(without_redirections(tokens_of(segment[:at]))))
    return operands[:max(0, len(operands) - dropped)] if dropped > 0 else operands


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


def _sed_targets(operands, segment):
    """Every operand of an in-place `sed` that is not one of its options.

    The script is among them, because the flag reading above declines to say where it ends. Which of
    these is a path is left to the tracked-file test, which answers it exactly and answers no for a
    substitution expression.
    """
    operands = _before_any_comment(operands, segment)
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


def _copy_targets(operands, segment):
    """Where a `cp` or an `mv` writes: its last operand, and each source placed under it.

    Both, because whether the last operand is the file written or the directory written into is
    decided by the filesystem rather than by the command, and the one that is not there is a path
    git tracks nothing at. `-t` moves the destination off the end, and that spelling is not read:
    a destination taken off the wrong end names a source, and a source is not written.
    """
    operands = _before_any_comment(operands, segment)
    if any(token == "-t" or token.startswith("--target-directory") for token in operands):
        return []
    named = [token for token in operands if not token.startswith("-")]
    if len(named) < 2:
        return []
    sources, destination = named[:-1], named[-1]
    return [destination] + [os.path.join(destination, os.path.basename(source))
                            for source in sources if not unexpanded(source)]


def _moves(segments):
    """How many of these segments change the directory a later one runs in.

    `pr_body.moves_directory` rather than a comparison against `cd`, so that `pushd`, `popd` and a
    path-qualified `/bin/cd` count here as they do there. A second set of the same words is the
    drift the derived stylesheet table exists to prevent one level down, and this one has a fixture
    behind it.
    """
    return sum(1 for segment in segments if pr_body.moves_directory(segment))


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
        candidates += _redirect_targets(segment)
        for operands in program_invocations(segment, "sed", ()):
            candidates += _sed_targets(operands, segment)
        for program in ("cp", "mv"):
            for operands in program_invocations(segment, program, ()):
                candidates += _copy_targets(operands, segment)

    found = []
    for candidate in candidates:
        placed = _placed(candidate, base)
        # A directory is where a write lands, never what it lands on, and one reaching the tracked
        # test below would be refused over a path nothing writes. The directory case in
        # `scripts/hooks/test_tracked_writes.py` holds git's half of that reason.
        if placed is not None and placed not in found and not os.path.isdir(placed):
            found.append(placed)
    return found


def _inside_a_repository(directory):
    """Whether some ancestor of `directory` carries a `.git`, asked of the filesystem.

    Not of git, and that is the whole point: a git that refuses a repository refuses every question
    about it, this one included, so it cannot separate "no repository here" from "git would not read
    the one that is here" -- and those want opposite answers. Reproduced against a git that answers
    `--version` and refuses the tree: a reading that asked instead whether git runs at all took the
    refusal for "no repository", stood both guards down, and left the wiring fixture reporting a
    table with nothing in it they decide about. That is the shape this repository's Unity job fails
    in, its container running as root over a checkout owned by another user while the safe.directory
    the checkout action sets names the host's path.

    A worktree carries `.git` as a file rather than a directory, so existence is what is asked.
    """
    current = Path(directory).resolve()
    return any((candidate / ".git").exists() for candidate in (current, *current.parents))


def _tracked(path):
    """Whether git names `path` a file it tracks.

    Inside a repository, anything but an answer from git counts as tracked: a guard reading "I
    cannot tell" as "nothing to refuse" falls silent exactly where it is the only thing left, and
    a git that will not start, one that times out and one that refuses the repository are alike
    that. Outside one there is nothing to be tracked in and no git is asked at all, which is also
    what keeps a scratch path from costing a subprocess.

    `ls-files` without `--error-unmatch`, because that spelling reports a path it does not track
    with the same exit code git uses for never having run.
    """
    parent = os.path.dirname(path)
    if not os.path.isdir(parent) or not _inside_a_repository(parent):
        return False
    # git tracks nothing inside the git directory, and asked about a path there from a `-C` inside it
    # it fails rather than answering -- which the reading below would take for a failure to read.
    if ".git" in Path(path).parts:
        return False
    answer = repository.git_answer(["-C", parent, "ls-files", "--", path], cwd=None, timeout=10)
    return answer.code != 0 or bool(answer.stdout.strip())


def tracked_writes(command, cwd):
    """The tracked repository files this command names as the literal target of a write."""
    return [path for path in literal_write_targets(command, cwd) if _tracked(path)]
