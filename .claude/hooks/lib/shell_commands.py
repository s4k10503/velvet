"""Shell-command parsing shared by the hooks that must recognise a git invocation.

A regex over the masked command missed eleven spellings of a branch creation at once, quoting the
argument among them, and every miss was silent. Splitting into segments and tokenising is what
those hooks agree on; each keeps its own table of which subcommand and which flag it cares about.

The mask locates command boundaries, where it is correct, and the tokens come from the original
text, because a masked argument has been replaced by spaces and cannot be read.
"""

import collections
import os
import re
import shlex

SEPARATORS = set(";&|\n")

# Words that may precede the command without changing which command it is. `then`/`do`/`else` because
# a command inside a conditional or a loop is still that command, and `if`/`while`/`until` for the same
# reason one word earlier: the keyword that OPENS the construct was missing while the ones that
# continue it were there, so `if git commit --amend; then ...` was seen by no guard and
# `then git commit --amend` by all of them. `builtin` alongside `command` because a guard reading the
# word after it saw `builtin cd` as neither a move nor a command word, and answered about a file in the
# directory the move had left.
LEADING_WORDS = {"if", "while", "until", "then", "do", "else", "elif",
                 "!", "time", "command", "builtin", "nohup", "exec"}
ENV_ASSIGNMENT = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*=")
REDIRECTION = re.compile(r"^\d*(?:>>|>&|<&|>|<)")

# git's own options, before the subcommand. -C is returned unconditionally: it names the repository
# the command acts on, and evaluating the cwd instead answers about a different tree.
GLOBAL_VALUE_FLAGS = {"-C", "-c", "--git-dir", "--work-tree", "--namespace", "--config-env"}

# `--git-dir` and a `GIT_DIR=` assignment name that tree too, but as a git directory rather than a
# working tree, so a caller that resolves paths under what it is handed cannot take one. Returning
# it is therefore the caller's to ask for, and only a caller that does nothing with the answer but
# hand it back to git may.
GIT_DIRECTORY_FLAG = "--git-dir"
GIT_DIRECTORY_VARIABLE = "GIT_DIR"

GitContext = collections.namedtuple("GitContext", "working_directory git_directory")

# `git commit` options that swallow the token after them. Which flags a guard cares about stays its
# own, per the note above; this is the one reading they share, and two guards taking it two ways
# would disagree about whether `git commit -m --amend` names a message or an amend.
#
# Membership is not "takes a value". `-S`/`--gpg-sign` and `-u`/`--untracked-files` take one only
# when it is attached, so they consume nothing and belong out — `GitOptionGrammarTests` in
# `scripts/hooks/test_amend_of_published_commit.py` is what fails if git stops reading them so.
COMMIT_VALUE_FLAGS = {
    "-m", "--message", "-F", "--file", "-c", "--reedit-message", "-C", "--reuse-message",
    "--fixup", "--squash", "--author", "--date", "-t", "--template", "--cleanup",
    "--trailer", "--pathspec-from-file",
}


def mask_shell_literals(command):
    out = list(command)
    i = 0
    n = len(command)
    # Delimiters whose bodies start at the next newline. A list because `cat <<A <<B` opens two on
    # one line, and the shell takes their bodies in the order they were written.
    pending = []
    while i < n:
        ch = command[i]
        if ch in "'\"":
            quote = ch
            out[i] = " "
            i += 1
            while i < n:
                if quote == '"' and command[i] == "\\" and i + 1 < n:
                    out[i] = out[i + 1] = " "
                    i += 2
                    continue
                if command[i] == quote:
                    out[i] = " "
                    i += 1
                    break
                out[i] = " "
                i += 1
        elif ch == "\\" and i + 1 < n:
            out[i] = out[i + 1] = " "
            i += 2
        elif command.startswith("<<<", i):
            # A here-string is not a heredoc, and `<<` matches its first two characters. Read as one
            # it took `<word` for the delimiter, blanked the rest of the line hunting a body line
            # equal to it, and never found one -- so every line after the opener's own went with it,
            # and a guard reading a command of more than one line saw none of the rest.
            out[i] = out[i + 1] = out[i + 2] = " "
            i += 3
        elif command.startswith("<<", i):
            j = i + 2
            strip_tabs = j < n and command[j] == "-"
            if strip_tabs:
                j += 1
            if j < n and command[j] in "'\"":
                quote = command[j]
                j += 1
                start = j
                while j < n and command[j] != quote:
                    j += 1
                delimiter = command[start:j]
                if j < n:
                    j += 1
            else:
                start = j
                while j < n and not command[j].isspace():
                    j += 1
                delimiter = command[start:j]
            while i < j:
                out[i] = " "
                i += 1
            # Past the delimiter the line is ordinary shell -- a redirect, a separator, the next
            # command -- and blanking it hid where the opening command's operands end. Measured over
            # this project's transcripts, that put the FOLLOWING command's tokens into `git commit`'s
            # operand list in 55 of 55 readings, and the three guards that read those operands took
            # an unexpanded one belonging to another command as the commit's own.
            #
            # So the body waits here rather than being consumed now, and the loop goes on lexing.
            # Skipping to the newline instead left the tail unlexed, and a `|` inside a quoted word
            # there became a segment boundary -- which handed a guard the inside of an argument as
            # a command to judge.
            pending.append((delimiter, strip_tabs))
        elif ch == "\n" and pending:
            i += 1
            for delimiter, strip_tabs in pending:
                while i < n:
                    line_start = i
                    line_end = command.find("\n", i)
                    if line_end == -1:
                        line_end = n
                    line = command[line_start:line_end]
                    body = line.lstrip("\t") if strip_tabs else line
                    if body == delimiter:
                        for k in range(line_start, line_end):
                            out[k] = " "
                        if line_end < n:
                            i = line_end + 1
                        else:
                            i = line_end
                        break
                    for k in range(line_start, line_end):
                        out[k] = " "
                    if line_end < n:
                        out[line_end] = " "
                        i = line_end + 1
                    else:
                        i = line_end
            pending = []
        else:
            i += 1
    return "".join(out)


def command_segments(command):
    """The command's segments, split at separators that are not inside a literal.

    The mask preserves length, so a separator's index in it is its index in the original — which
    is what lets the split find boundaries on masked text and take the tokens from unmasked text.
    """
    masked = mask_shell_literals(command)
    segments = []
    start = 0
    for index, char in enumerate(masked):
        if char in SEPARATORS:
            segments.append(command[start:index])
            start = index + 1
    segments.append(command[start:])
    return [segment for segment in (s.strip().strip("(){} ") for s in segments) if segment]


def tokens_of(segment):
    try:
        return shlex.split(segment, comments=False, posix=True)
    except ValueError:
        return []


def without_redirections(tokens):
    kept = []
    skip_next = False
    for token in tokens:
        if skip_next:
            skip_next = False
            continue
        match = REDIRECTION.match(token)
        if match:
            skip_next = match.end() == len(token)
            continue
        kept.append(token)
    return kept


def composed_directory(accumulated, value):
    """Where a `-C` lands given the ones before it.

    A second `-C` moves again from where the first arrived, so keeping only the last one names a
    different tree than the command acts on; one that reaches git already rooted starts over
    instead. `RepeatedDirectoryTests` is what fails if git stops reading them that way, and
    `--git-dir` is not folded here because git keeps the last of those.
    """
    if accumulated is None or value is None:
        return value
    if value.startswith("~"):
        return value
    return os.path.join(accumulated, value)


def git_invocation(tokens, git_directory=False):
    """(directory, subcommand, operands) when the segment runs git, else None.

    The directory is what the `-C` operands compose to. With `git_directory`, the first value is a
    `GitContext` that keeps it alongside either spelling of the git directory. A caller needs both
    to replay the repository selectors rather than replace one with the other.
    """
    index = 0
    named = None
    while index < len(tokens) and (
        ENV_ASSIGNMENT.match(tokens[index]) or tokens[index] in LEADING_WORDS
    ):
        variable, _, value = tokens[index].partition("=")
        if git_directory and variable == GIT_DIRECTORY_VARIABLE:
            named = value
        index += 1
    if index >= len(tokens) or os.path.basename(tokens[index]) != "git":
        return None

    index += 1
    directory = None
    while index < len(tokens) and tokens[index].startswith("-"):
        flag, _, attached = tokens[index].partition("=")
        if flag in GLOBAL_VALUE_FLAGS:
            if attached:
                value = attached
                index += 1
            else:
                value = tokens[index + 1] if index + 1 < len(tokens) else None
                index += 2
            if flag == "-C":
                directory = composed_directory(directory, value)
            elif git_directory and flag == GIT_DIRECTORY_FLAG:
                named = value
            continue
        index += 1

    if index >= len(tokens):
        return None
    context = GitContext(directory, named) if git_directory else directory
    return context, tokens[index], tokens[index + 1:]


# Where a move left the shell when nothing here can place it. It rides through the walk below rather
# than ending it, because a move nothing runs after changes no reading: `cd -` closing a chain leaves
# the work where the chain already put it.
UNRESOLVED_CD = object()

# The words that change the directory a later segment runs in.
MOVERS = {"cd", "pushd", "popd"}


def moves_directory(segment):
    """Whether this segment changes the directory a later segment runs in.

    The command word is read the way `leading_program` reads one, past `then`/`do`, `builtin` and an
    environment assignment: reading tokens[0] instead missed `if true; then cd /tmp; fi`, which is
    this module's own documented reason for having `leading_program` at all.
    """
    tokens = tokens_of(segment)
    index = leading_program(tokens)
    if index >= len(tokens):
        return False
    return os.path.basename(tokens[index]) in MOVERS


def _moved(where, tokens, index):
    """Where a move lands, given where the shell already is, or UNRESOLVED_CD.

    `popd`, `cd -` and a bare `cd` each name a directory nothing here computes: the stack the shell
    keeps, the one it was in before, and `$HOME`. They arrive as UNRESOLVED_CD rather than as "it did
    not move", which is the reading that would answer about the directory the move has left.
    """
    if os.path.basename(tokens[index]) == "popd":
        return UNRESOLVED_CD
    target = next((token for token in tokens[index + 1:] if not token.startswith("-")), None)
    if target is None or unexpanded(target) or target.startswith("~"):
        return UNRESOLVED_CD
    if os.path.isabs(target):
        return os.path.normpath(target)
    if where is UNRESOLVED_CD:
        return UNRESOLVED_CD
    return os.path.normpath(os.path.join(where, target))


def command_directory(command, cwd):
    """The directory this command's work runs in, or UNRESOLVED_CD where that is not one directory.

    `PreToolUse` fires before the command runs, so `cwd` is where the tool call *started*: the
    session's checkout, for `cd <worktree> && git ...`, which is not the tree the command acts on. A
    guard that reads `cwd` alone answers about that other tree, and it answers positively — which is
    the failure this exists to remove.

    Placing the move is what is done here rather than at the call sites. Reading it was already
    shared; placing it was written three times and no two the same: one joined a relative target
    to the hook PROCESS's own directory, so `cd sub && git commit --amend` refused the amend over
    a path nothing holds wherever that directory was not the one the event named.

    Every segment up to the first that runs a program is read, not the first segment alone. A
    variable assignment is a segment of its own, and stopping at one left `SP=/tmp; cd "$SP/x"`
    reading as no move at all -- the shape a session types whenever it names a worktree once and
    moves into it.

    UNRESOLVED_CD where two programs in the command run in different directories: one answer is
    wrong about one of them, and a caller's contract here is to refuse rather than choose. A group --
    `( cd /w && x ) && y` -- is not read as ending at its close, so the move inside one reaches `y`
    here and does not reach it in the shell.
    """
    where, answer = cwd, None
    for segment in command_segments(command):
        tokens = tokens_of(segment)
        index = leading_program(tokens)
        if index >= len(tokens):
            # An assignment and nothing else. It runs where the last move left the shell, and the
            # next segment is still before anything a caller cares about.
            continue
        if os.path.basename(tokens[index]) in MOVERS:
            where = _moved(where, tokens, index)
            continue
        if where is UNRESOLVED_CD:
            return UNRESOLVED_CD
        if answer is None:
            answer = where
        elif where != answer:
            return UNRESOLVED_CD
    return cwd if answer is None else answer


# What a guard says when `command_directory` declined to place the move, and what it asks for
# instead. Owned here so that a family of guards refusing one shape cannot become a family of
# explanations of it.
UNPLACEABLE_MOVE = (
    "The command changes directory in a way nothing here places: a target the shell has yet to "
    "expand or one opening on `~`; `popd`, `cd -` or a bare `cd`, each naming a directory only the "
    "running shell knows; or two commands running in different directories. So which tree it acts "
    "on was not read, and answering from the directory the tool call started in would be a verdict "
    "about a tree the command has already left.")
NAME_THE_TREE = ("Spell the move out as a single `cd` to a literal path, or run the command from "
                 "the tree itself.")


def git_invocations(command, subcommands, git_directory=False):
    """Each segment that runs git with one of `subcommands`, shaped as `git_invocation`.

    A segment is what `command_segments` splits out, so a git call reached some other way — inside a
    substitution, or behind a keyword `LEADING_WORDS` does not carry — is not among these.
    """
    found = []
    for segment in command_segments(command):
        invocation = git_invocation(without_redirections(tokens_of(segment)), git_directory)
        if invocation and invocation[1] in subcommands:
            found.append(invocation)
    return found


def leading_program(tokens):
    """The index of the command word, past any environment assignment or leading keyword."""
    index = 0
    while index < len(tokens) and (
        ENV_ASSIGNMENT.match(tokens[index]) or tokens[index] in LEADING_WORDS
    ):
        index += 1
    return index


def program_invocations(command, program, words):
    """Operands after `program` followed by `words`, once per segment that runs it.

    For programs whose subcommand is a fixed word sequence — `gh issue create`, `gh pr merge`.
    A pattern over the whole command answered yes to the words appearing inside an argument.
    """
    found = []
    for segment in command_segments(command):
        tokens = without_redirections(tokens_of(segment))
        index = leading_program(tokens)
        if index >= len(tokens) or os.path.basename(tokens[index]) != program:
            continue
        index += 1
        if tokens[index:index + len(words)] != list(words):
            continue
        found.append(tokens[index + len(words):])
    return found


# A hook is handed the command before the shell expands it, so an operand spelled with a variable or
# a substitution is not the text the program will receive. A guard that resolves such an operand
# answers about the literal, and every resolution of a literal fails — which for most guards is the
# pass, so the check silently does not happen. Each guard states which way it errs there; this only
# recognises the case.
UNEXPANDED = re.compile(r"[$`]")


def unexpanded(token):
    """Whether the shell will rewrite this operand before the program sees it."""
    return bool(UNEXPANDED.search(token))
