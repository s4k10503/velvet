"""Which maintenance lines a shell command would create or push, and what main says of each.

`refuse/branch_from_unmerged.py` asks this; `scripts/pr/maintenance_line.py` owns which lines main
maintains and what a line does not take, and the refusals here only apply it to a command. A tree
with no origin, an origin with no `main`, or an origin/main that was read and holds no Velvet
package is not this repository, and nothing here refuses in it; one whose origin could not be
read to tell is refused as unread.
"""

import os
import re
import sys
import urllib.parse
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


# A remote spelled `[user@]host:path`, which git reads as ssh where no slash comes before the colon.
SCP = re.compile(r"^(?:[^@/:]+@)?[^/:]+:")


def repository(url, tree):
    """A spelling of the repository `url` names that two spellings of one repository share: the
    host and path of one with a host, and the resolved path of a local one, `url` being read from
    `tree` as git reads it."""
    url = url.strip()
    if url.startswith("file://"):
        url = urllib.parse.unquote(urllib.parse.urlsplit(url).path)
    elif "://" in url:
        parts = urllib.parse.urlsplit(url)
        return f"{(parts.hostname or '').lower()}:{parts.path.strip('/').removesuffix('.git').lower()}"
    elif SCP.match(url):
        host, _, path = url.partition(":")
        return f"{host.rpartition('@')[2].lower()}:{path.strip('/').removesuffix('.git').lower()}"
    return os.path.realpath(os.path.join(tree, url)).removesuffix("/.git")


def _remote_url(tree, remote):
    """The URL `remote` pushes to: a configured remote's, or `remote` itself as git takes it."""
    return _answer(tree, "remote", "get-url", "--push", remote) or remote


def _default_remote(tree):
    branch = _answer(tree, "symbolic-ref", "--short", "-q", "HEAD")
    for key in ([f"branch.{branch}.pushRemote"] if branch else []) + ["remote.pushDefault"] + (
            [f"branch.{branch}.remote"] if branch else []):
        named = _answer(tree, "config", "--get", key)
        if named:
            return named
    return "origin"


def _has_origin(tree):
    """Whether `tree` is a repository with an origin remote, False where it is one without, and
    None where git cannot read it as a repository at all."""
    if _answer(tree, "rev-parse", "--git-dir") is None:
        return None
    return "origin" in (_answer(tree, "remote") or "").split()


def _origins(tree, remote):
    """Whether a push to `remote` from `tree` reaches origin's repository, False in a tree with no
    origin, and None where `tree` or origin's URL cannot be read to tell."""
    has_origin = _has_origin(tree)
    if not has_origin:
        return has_origin
    origin = _answer(tree, "remote", "get-url", "--push", "origin")
    if origin is None:
        return None
    return repository(_remote_url(tree, remote), tree) == repository(origin, tree)


def _globbed(pattern, name):
    """What a refspec glob's `*` stands for where it matches the local branch `name`, or None."""
    expression = re.escape(pattern).replace(r"\*", "(.*)", 1)
    for ref in (f"refs/heads/{name}", name):
        matched = re.fullmatch(expression, ref)
        if matched:
            return matched.group(1)
    return None


def pushed(command, cwd):
    """The lines a `git push` in this command updates, as (line, source, tree, whether the push
    reaches origin's repository or None where that cannot be told). `HEAD`, `@` and a push naming
    no refspec are the current branch; `--all` is every local line, and a glob is each local branch
    its source side matches, written to the line its destination side names. A deletion updates
    none."""
    found = []
    for directory, subcommand, operands in _invocations(command):
        if subcommand != "push" or any(token in PUSH_DELETES for token in operands):
            continue
        tree = _tree(cwd, directory)
        named = _positionals(operands, PUSH_VALUE_FLAGS)
        reaches = _origins(tree, named[0] if named else _default_remote(tree))
        refspecs = named[1:]
        local = (_answer(tree, "for-each-ref", "--format=%(refname:short)", "refs/heads")
                 or "").split()
        if PUSH_EVERY_BRANCH & set(operands):
            found += [(name, f"refs/heads/{name}", tree, reaches) for name in local
                      if maintenance_line.is_line(name)]
        elif not refspecs and "--tags" not in operands:
            refspecs = ["HEAD"]
        for refspec in refspecs:
            source, colon, destination = refspec.lstrip("+").partition(":")
            if colon and not source:
                continue
            if "*" in source:
                for name in local:
                    star = _globbed(source, name)
                    target = (destination if colon else source).replace("*", star or "", 1)
                    if star is not None and maintenance_line.is_line(
                            target.removeprefix("refs/heads/")):
                        found.append((target.removeprefix("refs/heads/"), f"refs/heads/{name}",
                                      tree, reaches))
                continue
            if not colon:
                destination = (_answer(tree, "symbolic-ref", "--short", "-q", "HEAD")
                               if source in CURRENT else source)
            name = (destination or "").removeprefix("refs/heads/")
            if maintenance_line.is_line(name):
                found.append((name, source, tree, reaches))
    return found


def _unread(verb, name, failure):
    return (f"Refusing to {verb} `{name}`: which lines main maintains could not be read off "
            f"origin/main.\n\n  {failure.strip()}\n\nFetch origin and retry, or give a branch that "
            f"is not a line a name that is not one.")


def _main_says(tree, verb, name):
    """(`main_state`, None) for `tree`; (None, None) where it is not this repository, having no
    origin, an origin with no `main`, or an origin/main holding no Velvet package; or (None, the
    refusal) where any of that could not be read."""
    has_origin = _has_origin(tree)
    if has_origin is None:
        failed = git_answer(["rev-parse", "--git-dir"], tree, timeout=GIT_TIMEOUT)
        return None, _unread(verb, name, failed.stderr or "git rev-parse failed")
    if not has_origin:
        return None, None
    heads = git_answer(["ls-remote", "origin", f"refs/heads/{maintenance_line.MAIN}"], tree,
                       timeout=GIT_TIMEOUT)
    if heads.code != 0:
        return None, _unread(verb, name, heads.stderr or "git ls-remote failed")
    if not heads.stdout.strip():
        return None, None
    fetched = git_answer(["fetch", "-q", "origin", maintenance_line.MAIN,
                          maintenance_line.LINES_REFSPEC], tree, timeout=GIT_TIMEOUT)
    if fetched.code != 0:
        return None, _unread(verb, name, fetched.stderr or "git fetch failed")
    listed = git_answer(["ls-tree", "--name-only", f"origin/{maintenance_line.MAIN}", "--",
                         maintenance_line.published_check.PACKAGE_JSON_PATH], tree,
                        timeout=GIT_TIMEOUT)
    if listed.code != 0:
        return None, _unread(verb, name, listed.stderr or "git ls-tree failed")
    if not listed.stdout.strip():
        return None, None
    try:
        return maintenance_line.main_state(tree), None
    except maintenance_line.UNREAD as failure:
        return None, _unread(verb, name, getattr(failure, "stderr", None) or str(failure))


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
    for name, source, tree, reaches in pushed(command, cwd):
        if reaches is False:
            continue
        if reaches is None:
            said.append(f"Refusing to push `{name}`: whether the push reaches origin's repository "
                        f"could not be told: git could not read this tree, or origin's URL in it. "
                        f"Run the push from a readable checkout, or give a branch that is not a "
                        f"line a name that is not one.")
            continue
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
