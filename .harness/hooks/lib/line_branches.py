"""Which maintenance lines a shell command would create or push, and what main says of each.

`refuse/branch_from_unmerged.py` asks this; `scripts/pr/maintenance_line.py` owns which lines main
maintains and what a line does not take, and the refusals here only apply it to a command. A tree
whose origin/main holds no Velvet package is not this repository, and nothing here refuses in it.
"""

import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from repository import git_answer  # noqa: E402
from shell_commands import command_segments, git_invocation, tokens_of, without_redirections  # noqa: E402

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "scripts" / "pr"))
import maintenance_line  # noqa: E402

GIT_TIMEOUT = 10

# What renames or copies a branch, naming the new one last.
RENAMES = {"-m", "-M", "--move", "-c", "-C", "--copy"}
WORKTREE_NAMED = {"-b", "-B"}
WORKTREE_DETACHED = {"-d", "--detach"}
WORKTREE_VALUE_FLAGS = {"--reason"}

PUSH_VALUE_FLAGS = {"-o", "--push-option", "--repo", "--receive-pack", "--exec"}
PUSH_DELETES = {"-d", "--delete"}
PUSH_EVERY_BRANCH = {"--all", "--branches", "--mirror"}
CURRENT = {"HEAD", "@"}


def _positionals(operands, valued):
    found, index = [], 0
    while index < len(operands):
        token = operands[index]
        if token.startswith("-") and token != "-":
            index += 2 if token in valued else 1
            continue
        found.append(token)
        index += 1
    return found


def _invocations(command):
    for segment in command_segments(command):
        invocation = git_invocation(without_redirections(tokens_of(segment)))
        if invocation:
            yield invocation


def created(command):
    """The lines this command creates that the branch guard's own reading of a creation does not
    name, as (line, -C directory): a rename or a copy, an orphan, and a worktree named after its
    path."""
    found = []
    for directory, subcommand, operands in _invocations(command):
        name = None
        if subcommand == "branch" and any(token.partition("=")[0] in RENAMES for token in operands):
            named = _positionals(operands, ())
            name = named[-1] if named else None
        elif subcommand in ("checkout", "switch"):
            for index, token in enumerate(operands):
                if token == "--orphan" and index + 1 < len(operands):
                    name = operands[index + 1]
                elif token.startswith("--orphan="):
                    name = token[len("--orphan="):]
        elif subcommand == "worktree" and operands[:1] == ["add"]:
            flags = {token.partition("=")[0] for token in operands[1:]}
            named = _positionals(operands[1:], WORKTREE_VALUE_FLAGS)
            if not flags & (WORKTREE_NAMED | WORKTREE_DETACHED) and len(named) == 1:
                name = os.path.basename(named[0].rstrip("/"))
        if maintenance_line.is_line(name):
            found.append((name, directory))
    return found


def pushes(command):
    """Whether this command runs a `git push` at all, which `pushed` needs a tree to read."""
    return any(subcommand == "push" for _, subcommand, _ in _invocations(command))


def _tree(cwd, directory):
    return str(Path(cwd, directory)) if directory else str(cwd)


def _answer(tree, *args):
    answer = git_answer(list(args), tree, timeout=GIT_TIMEOUT)
    return answer.stdout.strip() if answer.code == 0 else None


def pushed(command, cwd):
    """The lines a `git push` in this command updates on origin, as (line, source, tree). `HEAD`,
    `@` and a push naming no refspec are the current branch; `--all` and a glob are every local
    line. A deletion updates none, and a push to another remote is not to this repository's lines."""
    found = []
    for directory, subcommand, operands in _invocations(command):
        if subcommand != "push" or any(token in PUSH_DELETES for token in operands):
            continue
        tree = _tree(cwd, directory)
        named = _positionals(operands, PUSH_VALUE_FLAGS)
        if named and named[0] != "origin":
            continue
        refspecs = named[1:]
        if PUSH_EVERY_BRANCH & set(operands) or any("*" in refspec for refspec in refspecs):
            local = (_answer(tree, "for-each-ref", "--format=%(refname:short)", "refs/heads") or "")
            found += [(name, f"refs/heads/{name}", tree) for name in local.split()
                      if maintenance_line.is_line(name)]
            refspecs = [refspec for refspec in refspecs if "*" not in refspec]
        elif not refspecs and "--tags" not in operands:
            refspecs = ["HEAD"]
        for refspec in refspecs:
            source, colon, destination = refspec.lstrip("+").partition(":")
            if colon and not source:
                continue
            if not colon:
                destination = (_answer(tree, "symbolic-ref", "--short", "-q", "HEAD")
                               if source in CURRENT else source)
            name = (destination or "").removeprefix("refs/heads/")
            if maintenance_line.is_line(name):
                found.append((name, source, tree))
    return found


def _main_says(tree, verb, name):
    """(`main_state`, None) for `tree`, (None, None) where it is not this repository, or (None, the
    refusal) where it could not be read."""
    git_answer(["fetch", "-q", "origin", maintenance_line.MAIN, maintenance_line.LINES_REFSPEC],
               tree, timeout=GIT_TIMEOUT)
    package = f"origin/{maintenance_line.MAIN}:{maintenance_line.published_check.PACKAGE_JSON_PATH}"
    if git_answer(["cat-file", "-e", package], tree, timeout=GIT_TIMEOUT).code != 0:
        return None, None
    try:
        return maintenance_line.main_state(tree), None
    except maintenance_line.UNREAD as failure:
        said = (getattr(failure, "stderr", None) or str(failure)).strip()
        return None, (f"Refusing to {verb} `{name}`: which lines main maintains could not be read "
                      f"off origin/main.\n\n  {said}\n\nFetch origin and retry, or give a branch "
                      f"that is not a line a name that is not one.")


def refusals(command, cwd, creations):
    """What to say about each line the command creates or pushes. `creations` is the branch guard's
    own reading, as (name, start point, -C directory)."""
    said = []
    for name, directory in [(name, directory) for name, _, directory in creations
                            if maintenance_line.is_line(name)] + created(command):
        state, refused = _main_says(_tree(cwd, directory), "create", name)
        why = maintenance_line.unmaintained(name, state) if state else None
        if refused or why:
            said.append(refused or (
                f"Refusing to create `{name}`: {why}. A branch that is not a line takes a name that "
                f"is not one, and the lines main maintains are "
                f"{', '.join(maintenance_line.maintained(state)) or 'none'} "
                f"({maintenance_line.SECTION})."))
    for name, source, tree in pushed(command, cwd):
        state, refused = _main_says(tree, "push", name)
        why = maintenance_line.unmaintained(name, state) if state else None
        if refused or why:
            said.append(refused or f"Refusing to push `{name}`: {why}: "
                                   f"{maintenance_line.elsewhere(state)}.")
        elif state:
            said.extend(_brought(tree, name, source))
    return said


def _brought(tree, line, source):
    """What a push of `source` to the maintained line `line` is refused for, as a pull request onto
    it would be. A line origin has not got is being cut, and brings nothing to read."""
    if _answer(tree, "rev-parse", "--verify", "-q", f"refs/remotes/origin/{line}") is None:
        return []
    sha = _answer(tree, "rev-parse", "--verify", "-q", f"{source}^{{commit}}")
    if sha is None:
        return []
    try:
        reading = maintenance_line.read(tree, line, sha, GIT_TIMEOUT)
    except maintenance_line.UNREAD as failure:
        said = (getattr(failure, "stderr", None) or str(failure)).strip()
        return [f"Refusing to push to `{line}`: what {sha[:7]} brings onto it could not be read.\n\n"
                f"  {said}\n\nFetch origin and retry."]
    return [f"Refusing to push to `{line}`: {reason}." for reason in
            maintenance_line.head_reasons(line, reading)]
