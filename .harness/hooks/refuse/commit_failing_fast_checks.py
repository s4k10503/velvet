#!/usr/bin/env python3
"""Refuse `git commit` when the content it would record fails a fast deterministic check.

Every check this repository owns runs either at CI, twenty minutes away, or never. Nothing looks
at what is about to be committed until integration. These checks finish in well under a second and
would have caught defects that instead reached a commit or CI.

What is checked is what the commit records, not what the working tree happens to hold. Those are
different files: a broken blob whose working copy was fixed afterwards passed every check, and a
file staged and then deleted was refused with a `FileNotFoundError` for a commit git would accept.
`git commit -a` and `git commit <pathspec>` record the working tree, so for those it is read too.
"""

import collections
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from display import displayed
from shell_commands import (COMMIT_VALUE_FLAGS, NAME_THE_TREE, UNPLACEABLE_MOVE, UNRESOLVED_CD,
                            command_directory, git_invocations, unexpanded)
import repository


HOOK_TOOLS = {"Bash"}

NEUTER_CUTS = "scripts/test_quality/neuter_cuts.json"

COMMIT_ALL_FLAGS = {"-a", "--all"}


# A pathspec the shell has not expanded names no file, so every check runs over nothing and the
# commit records content none of them saw — the check silently not happening. Refusing errs the cheap
# way here: a commit is remade in a second, and the alternative is a blob nothing looked at.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'git commit -m x $PATHS'

UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "git commit -m probe"}


# An empty listing says this commit records nothing a check could fail, and an empty index says
# nothing in it is a submodule, so a reading that did not answer must not resolve to either.
Unreadable = collections.namedtuple("Unreadable", "subject remedy")

# What git wrote, what was asked of it, and the exit code — None where git did not run at all.
GitRead = collections.namedtuple("GitRead", "stdout said code asked")


def git(cwd, *args):
    """git's stdout as the bytes it wrote, alongside what was asked and how it ended.

    Same reason `lib/repository.py` answers rather than raising: a hook that raises exits 1, and 1
    lets the tool through.
    """
    asked = displayed("git " + " ".join(args))
    try:
        done = subprocess.run(["git", "-C", cwd, *args], capture_output=True, timeout=30)
    except (OSError, subprocess.SubprocessError) as failure:
        return GitRead(b"", str(failure), None, asked)
    return GitRead(done.stdout, done.stderr.decode(**repository.DECODING).strip(),
                   done.returncode, asked)


def unread(answer):
    """Which refusal a git reading that did not answer earns.

    A git that never ran and a git that refused are different facts, and the remedy for one does
    not reach the other: `Given_ATildeSpelledPathspec_When_Judged_Then_ItIsNotSentBackToGit` in
    scripts/hooks/test_commit_failing_fast_checks.py is what fails when both take the retry.
    """
    detail = "\n".join("  " + line for line in [answer.asked] + answer.said.splitlines())
    if answer.code is None:
        return Unreadable("what this commit would record could not be read.\n\n" + detail,
                          "Retry when git answers.")
    return Unreadable("git refused to read what this commit would record.\n\n" + detail,
                      "Correct what git named and retry.")


def repo_root(cwd):
    """The tree the checks run in, or an `Unreadable` when the reading did not answer.

    Falling back to `cwd` was rejected: it drops this reading's account of what went wrong and
    sends every reading below into a directory nothing has established is a tree.
    """
    answer = git(cwd, "rev-parse", "--show-toplevel")
    if answer.code != 0:
        return unread(answer)
    # Same terminator-only reading as `repository.toplevel`, and for the reason given there.
    return answer.stdout.decode(**repository.DECODING).removesuffix("\n") or cwd


def commit_invocations(command):
    """(directory, commits all, pathspecs) for each `git commit` in the command."""
    found = []
    for directory, _, operands in git_invocations(command, {"commit"}):
        commits_all = False
        pathspecs = []
        index = 0
        after_separator = False
        while index < len(operands):
            token = operands[index]
            if token == "--":
                after_separator = True
                index += 1
                continue
            if not after_separator and token.startswith("--"):
                flag = token.partition("=")[0]
                if flag in COMMIT_ALL_FLAGS:
                    commits_all = True
                if flag in COMMIT_VALUE_FLAGS and "=" not in token:
                    index += 2
                    continue
                index += 1
                continue

            if not after_separator and token.startswith("-") and len(token) > 1:
                # In a short group such as -am each letter is its own flag; the first value-taking
                # one ends the group, taking either the rest of the token or the next one.
                takes_next = False
                for position, letter in enumerate(token[1:]):
                    if letter == "a":
                        commits_all = True
                    if "-" + letter in COMMIT_VALUE_FLAGS:
                        takes_next = position + 2 == len(token)
                        break
                index += 2 if takes_next else 1
                continue
            pathspecs.append(token)
            index += 1
        found.append((directory, commits_all, pathspecs))
    return found


def records(listing):
    """The paths a NUL-delimited listing named."""
    return [record.decode(**repository.DECODING)
            for record in listing.split(b"\0") if record.strip()]


def staged_paths(cwd):
    # R is included: a rename reports it, and dropping it left a file renamed and broken in one
    # staged change checked by nothing.
    answer = git(cwd, "diff", "--cached", "-z", "--name-only", "--diff-filter=ACMR")
    return records(answer.stdout) if answer.code == 0 else unread(answer)


# The mode an index entry carries for a submodule.
# `Given_ASubmoduleWhoseCommitIsStaged_When_Judged_Then_TheCommitIsAllowed` pins what a commit
# records there, and fails when this stops being read.
GITLINK = "160000"


def index_modes(cwd):
    """path -> the mode the index holds it at, or an `Unreadable` when the reading did not answer."""
    answer = git(cwd, "ls-files", "-s", "-z")
    if answer.code != 0:
        return unread(answer)
    modes = {}
    for record in records(answer.stdout):
        meta, _, path = record.partition("\t")
        modes[path] = meta.partition(" ")[0]
    return modes


def worktree_paths(cwd, pathspecs):
    """The paths a commit would take from the worktree, narrowed by `pathspecs`.

    `~` is expanded before git sees it. The shell expands one and git does not, so a pathspec spelled
    that way reached git literally, matched nothing, and left the checks below reading no content at
    all -- measured, `git commit -m x -- ~/velvet/a.cs` passed every one of them. Expanded, it is an
    absolute path outside the repository and git refuses it, which reaches the refusal above rather
    than the silence.
    """
    args = ["diff", "-z", "--name-only", "--diff-filter=ACMR"]
    if pathspecs:
        args += ["--", *(os.path.expanduser(spec) for spec in pathspecs)]
    answer = git(cwd, *args)
    return records(answer.stdout) if answer.code == 0 else unread(answer)


def recorded(full):
    """The bytes at a worktree path, or None where a commit records no blob there at all."""
    if os.path.islink(full):
        return os.readlink(full).encode(**repository.DECODING)
    if os.path.isdir(full):
        return None
    with open(full, "rb") as handle:
        return handle.read()


def committed_content(cwd, commits_all, pathspecs):
    """path -> bytes the commit would record, or an `Unreadable` when part of it did not read."""
    staged = staged_paths(cwd)
    if isinstance(staged, Unreadable):
        return staged
    modes = index_modes(cwd) if staged else {}
    if isinstance(modes, Unreadable):
        return modes
    content = {}
    for path in staged:
        if modes.get(path) == GITLINK:
            continue
        blob = git(cwd, "show", ":" + path)
        if blob.code != 0:
            return unread(blob)
        content[path] = blob.stdout
    if commits_all or pathspecs:
        worktree = worktree_paths(cwd, pathspecs)
        if isinstance(worktree, Unreadable):
            return worktree
        for path in worktree:
            try:
                data = recorded(os.path.join(cwd, path))
            except OSError as error:
                # Answered rather than skipped, so that the two halves of this function agree about
                # a path that would not read. A skip left the refusal over the files that did read
                # looking like a verdict over the whole commit.
                return Unreadable(
                    "a file this commit records could not be opened.\n\n  {}: {}".format(
                        displayed(path), displayed(str(error))),
                    "Make it readable, or commit without it.")
            if data is not None:
                content[path] = data
    return content


def cut_targets(root):
    cuts_path = os.path.join(root, NEUTER_CUTS)
    if not os.path.exists(cuts_path):
        return set(), []
    # A malformed cut file used to raise out of main, and a hook that exits 1 is treated as
    # non-blocking — so the one file whose breakage matters most turned every check off.
    try:
        with open(cuts_path, encoding="utf-8") as cuts_file:
            raw = json.load(cuts_file)
    except (OSError, ValueError):
        return set(), []
    edits = [edit for cut in raw.get("cuts", []) for edit in cut.get("edits", [])]
    return {edit["file"] for edit in edits}, edits


def refuse(display, output, reproduce):
    sys.stderr.write(
        f"Refusing `git commit`: {displayed(display)} failed a fast check.\n\n"
        f"{output.rstrip()}\n\n"
        f"Reproduce: {reproduce}\n"
    )
    return 2


def run_tool(argv, display, reproduce):
    proc = subprocess.run(argv, capture_output=True, text=True, timeout=30)
    if proc.returncode != 0:
        return refuse(display, proc.stderr or proc.stdout, reproduce)
    return 0


def check_content(display, data):
    """Runs whichever fast check the path's extension names, over the content itself."""
    suffix = os.path.splitext(display)[1]
    # The reproduce line is read as a line too, and it is built here rather than in `refuse`. Two
    # spellings, because one of the two lines below puts the path in a shell word and the other in
    # a Python string literal, which is quoted whatever the name holds.
    shown = displayed(display)
    literal = json.dumps(display)
    if suffix == ".json":
        try:
            json.loads(data.decode("utf-8"))
        except (ValueError, UnicodeDecodeError) as err:
            return refuse(display, str(err), f"python3 -c 'import json; json.load(open({literal}))'")
        return 0

    if suffix in (".yml", ".yaml"):
        try:
            import yaml
        except ImportError:
            return 0
        try:
            yaml.safe_load(data.decode("utf-8"))
        except (yaml.YAMLError, UnicodeDecodeError) as err:
            return refuse(display, str(err),
                          f"python3 -c 'import yaml; yaml.safe_load(open({literal}))'")
        return 0

    if suffix not in (".py", ".sh"):
        return 0

    # py_compile and shellcheck both want a file, and the content under test is the index's rather
    # than the working tree's, so it is written out rather than read from the checkout.
    handle, scratch = tempfile.mkstemp(suffix=suffix)
    try:
        with os.fdopen(handle, "wb") as scratch_file:
            scratch_file.write(data)
        if suffix == ".py":
            return run_tool(["python3", "-B", "-m", "py_compile", scratch],
                            display, f"python3 -m py_compile {shown}")
        code = run_tool(["bash", "-n", scratch], display, f"bash -n {shown}")
        if code:
            return code
        shellcheck = __import__("shutil").which("shellcheck")
        if not shellcheck:
            return 0
        # Warning and above. shellcheck exits non-zero on info-level notes too, and three hooks in
        # this repository carry one — SC1091, for sourcing a sibling it was not given — so the
        # default floor refused every commit that touched them for something nobody intends to fix.
        return run_tool([shellcheck, "--severity=warning", scratch],
                        display, f"shellcheck --severity=warning {shown}")
    finally:
        try:
            os.unlink(scratch)
        except OSError:
            pass


# What mutation_check.py exits with when it refuses, as against any other non-zero status, which
# means it could not answer at all. MutationRefusalStatusTests pins the two against each other.
CARRIED_REFUSAL = 3

# Under the timeout this hook is registered with, so the harness does not kill the hook mid-check and
# take the refusal with it — which is the reading that lets the commit through.
CARRIED_TIMEOUT = 10


def check_carried_mutation(root, paths):
    """Refuses a commit that would record a file a mutation campaign is holding.

    Same hazard as `carried_neuters` above, and not the same shape: a cut is declared in a file this
    can match against, while a mutation is whatever the campaign generated. The campaign records what
    it holds instead, and mutation_check.py owns reading that record.

    Asked wherever a commit records content rather than gated on the record existing here. Where the
    record lives is mutation_check.py's to know, and a copy of that here would go on answering "no
    campaign" after a rename moved it.
    """
    script = os.path.join(root, "scripts", "test_quality", "mutation_check.py")
    if not os.path.exists(script):
        return 0
    try:
        proc = subprocess.run(["python3", "-B", script, "--project", root, "--carried", *paths],
                              capture_output=True, text=True, timeout=CARRIED_TIMEOUT, cwd=root)
    except (OSError, subprocess.SubprocessError) as failure:
        # Raising here exits 1, which lets the tool through — and it would take every check
        # below out with it.
        return refuse_mutation("this check could not be run at all.",
                               "{}: {}".format(script, failure))
    if proc.returncode == 0:
        return 0
    if proc.returncode == CARRIED_REFUSAL:
        return refuse_mutation("a mutation campaign is holding one of these files.",
                               proc.stdout.rstrip() or proc.stderr.rstrip())
    return refuse_mutation(
        "this check could not read whether a campaign holds one of these files.",
        "{} exited {}:\n{}".format(script, proc.returncode, (proc.stderr or proc.stdout).rstrip()))


def refuse_mutation(headline, detail):
    """Which of the two it is, because a reading that did not happen is not one that found nothing —
    and telling a reader the campaign holds their file when nothing established that is a false
    statement about their tree."""
    sys.stderr.write("Refusing `git commit`: " + headline + "\n\n" + detail + "\n")
    return 2


def check_neuter(root):
    script = os.path.join(root, "scripts", "test_quality", "neuter_check.py")
    if not os.path.exists(script):
        return 0
    proc = subprocess.run(["python3", "-B", script, "--validate", "--project", root],
                          capture_output=True, text=True, timeout=30, cwd=root)
    if proc.returncode != 0:
        return refuse(NEUTER_CUTS, proc.stderr or proc.stdout,
                      "python3 scripts/test_quality/neuter_check.py --validate")
    return 0


def carried_neuters(content, edits):
    """Cuts present in the content the commit would record.

    A sweep holds a neuter in a production source until its `finally` restores it, and while it is
    there the cut reads as an ordinary modification — the diff shows a plausible early return.
    Committing then captures a method that silently does nothing, for a reason the author cannot
    see in their own diff.
    """
    found = []
    for edit in edits:
        data = content.get(edit["file"])
        if data is None:
            continue
        try:
            lines = data.decode("utf-8").splitlines()
        except UnicodeDecodeError:
            continue
        for index, line in enumerate(lines):
            if line.strip() != edit["anchor"]:
                continue
            body = next((i for i in range(index + 1, len(lines)) if lines[i].strip()), None)
            after = next((lines[i].strip() for i in range(body + 1, len(lines))
                          if lines[i].strip()), "") if body is not None else ""
            if after == edit["neuter"]:
                found.append(f"{edit['file']}: {edit['anchor']}")
    return found


def refuse_unreadable(state):
    sys.stderr.write(f"Refusing `git commit`: {state.subject}\n\n{state.remedy}\n")
    return 2


def audit(cwd, commits_all, pathspecs):
    root = repo_root(cwd)
    if isinstance(root, Unreadable):
        return refuse_unreadable(root)
    content = committed_content(root, commits_all, pathspecs)
    if isinstance(content, Unreadable):
        return refuse_unreadable(content)
    if not content:
        return 0

    targets, edits = cut_targets(root)
    neutered = carried_neuters(content, edits)
    if neutered:
        sys.stderr.write(
            "Refusing `git commit`: the content being committed carries a neuter from a sweep.\n\n"
            + "\n".join("  " + entry for entry in neutered)
            + "\n\nEach names a method whose body would begin with the cut's early return — it "
              "compiles, it reads as an ordinary change, and it does nothing. Wait for a running "
              "sweep to restore it, or restore it yourself:\n"
              "  git checkout -- <file>\n"
        )
        return 2

    for path in sorted(content):
        code = check_content(path, content[path])
        if code:
            return code

    # After the content checks rather than before them: this one runs the working tree's copy of a
    # script, so a mid-edit one refusing here would hide the py_compile failure that explains it.
    code = check_carried_mutation(root, sorted(content))
    if code:
        return code

    if NEUTER_CUTS in content or any(path in targets for path in content):
        return check_neuter(root)
    return 0


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0

    command = event.get("tool_input", {}).get("command", "")
    commits = commit_invocations(command)
    if not commits:
        return 0

    cwd = command_directory(command, event.get("cwd") or ".")
    if cwd is UNRESOLVED_CD:
        sys.stderr.write("Refusing `git commit`: the tree it runs in could not be read.\n\n"
                         f"{UNPLACEABLE_MOVE}\n\n"
                         "Every check below reads the content the commit would record, and which "
                         "tree holds that\ncontent is what the move decides.\n\n"
                         f"{NAME_THE_TREE}\n")
        return 2
    for directory, commits_all, pathspecs in commits:
        # The two operand kinds are refused apart, because the remedy for one does not reach the
        # other: naming the paths leaves a `-C` unresolved, and the reader told to do it tries
        # something that cannot help. Measured on an agent's own `git -C "$SP" commit`.
        if directory and unexpanded(directory):
            sys.stderr.write(
                "Refusing `git commit`: the tree it runs in is named by an operand the shell has "
                "not\nexpanded yet.\n\n"
                f"  -C {directory}\n\n"
                "Every check below reads the content the commit would record, and which repository "
                "holds\nthat content is what `-C` decides — so this cannot read the tree at all, "
                "rather than reading\nthe wrong one.\n\n"
                "Spell the directory out.\n")
            return 2
        unresolved = [token for token in pathspecs if unexpanded(token)]
        if unresolved:
            sys.stderr.write(
                "Refusing `git commit`: it is scoped by an operand the shell has not expanded yet.\n\n"
                + "\n".join("  " + token for token in unresolved)
                + "\n\nEvery check below reads the content the commit would record, and a pathspec that "
                  "is still a variable names no file — so they would all run over nothing and pass, "
                  "and the commit would record content none of them saw.\n\n"
                  "Name the paths, or commit the index and let the checks read that.\n")
            return 2
        code = audit(directory or cwd, commits_all, pathspecs)
        if code:
            return code
    return 0


if __name__ == "__main__":
    sys.exit(main())
