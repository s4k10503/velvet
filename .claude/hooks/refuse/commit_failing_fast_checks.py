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

import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import COMMIT_VALUE_FLAGS, git_invocations, unexpanded


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


# What a reading that did not answer resolves to. Every reader below otherwise reports it as an
# empty result, and an empty result is "this commit records nothing that could fail a check".
UNREADABLE = object()


def git(cwd, *args):
    """A finished `git`, or None when it could not be run at all.

    Same reason `lib/repository.py` answers None rather than raising: a hook that raises exits 1,
    and 1 lets the tool through.
    """
    try:
        return subprocess.run(["git", "-C", cwd, *args], capture_output=True, text=True, timeout=30)
    except (OSError, subprocess.SubprocessError):
        return None


def git_bytes(cwd, *args):
    try:
        result = subprocess.run(["git", "-C", cwd, *args], capture_output=True, timeout=30)
    except (OSError, subprocess.SubprocessError):
        return None
    return result.stdout if result.returncode == 0 else None


def repo_root(cwd):
    result = git(cwd, "rev-parse", "--show-toplevel")
    return result.stdout.strip() if result and result.returncode == 0 else cwd


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


def staged_paths(cwd):
    # R is included: a rename reports it, and dropping it left a file renamed and broken in one
    # staged change checked by nothing.
    result = git(cwd, "diff", "--cached", "--name-only", "--diff-filter=ACMR")
    if result is None or result.returncode != 0:
        return UNREADABLE
    return [line for line in result.stdout.splitlines() if line.strip()]


def worktree_paths(cwd, pathspecs):
    """The paths a commit would take from the worktree, narrowed by `pathspecs`.

    `~` is expanded before git sees it. The shell expands one and git does not, so a pathspec spelled
    that way reached git literally, matched nothing, and left the checks below reading no content at
    all -- measured, `git commit -m x -- ~/velvet/a.cs` passed every one of them. Expanded, it is an
    absolute path outside the repository and git refuses it, which reaches the refusal above rather
    than the silence.
    """
    args = ["diff", "--name-only", "--diff-filter=ACMR"]
    if pathspecs:
        args += ["--", *(os.path.expanduser(spec) for spec in pathspecs)]
    result = git(cwd, *args)
    if result is None or result.returncode != 0:
        return UNREADABLE
    return [line for line in result.stdout.splitlines() if line.strip()]


def committed_content(cwd, commits_all, pathspecs):
    """path -> bytes the commit would record, or UNREADABLE when git did not answer."""
    staged = staged_paths(cwd)
    if staged is UNREADABLE:
        return UNREADABLE
    content = {}
    for path in staged:
        blob = git_bytes(cwd, "show", ":" + path)
        if blob is None:
            return UNREADABLE
        content[path] = blob
    if commits_all or pathspecs:
        worktree = worktree_paths(cwd, pathspecs)
        if worktree is UNREADABLE:
            return UNREADABLE
        for path in worktree:
            try:
                with open(os.path.join(cwd, path), "rb") as handle:
                    content[path] = handle.read()
            except OSError:
                continue
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
        f"Refusing `git commit`: {display} failed a fast check.\n\n"
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
    if suffix == ".json":
        try:
            json.loads(data.decode("utf-8"))
        except (ValueError, UnicodeDecodeError) as err:
            return refuse(display, str(err), f"python3 -c 'import json; json.load(open(\"{display}\"))'")
        return 0

    if suffix in (".yml", ".yaml"):
        try:
            import yaml
        except ImportError:
            return 0
        try:
            yaml.safe_load(data.decode("utf-8"))
        except (yaml.YAMLError, UnicodeDecodeError) as err:
            return refuse(display, str(err), f"python3 -c 'import yaml; yaml.safe_load(open(\"{display}\"))'")
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
                            display, f"python3 -m py_compile {display}")
        code = run_tool(["bash", "-n", scratch], display, f"bash -n {display}")
        if code:
            return code
        shellcheck = __import__("shutil").which("shellcheck")
        if not shellcheck:
            return 0
        # Warning and above. shellcheck exits non-zero on info-level notes too, and three hooks in
        # this repository carry one — SC1091, for sourcing a sibling it was not given — so the
        # default floor refused every commit that touched them for something nobody intends to fix.
        return run_tool([shellcheck, "--severity=warning", scratch],
                        display, f"shellcheck --severity=warning {display}")
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
        # Raising here exits 1, which this file's header records as letting the tool through — and
        # it would take every check below out with it.
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


def audit(cwd, commits_all, pathspecs):
    root = repo_root(cwd)
    content = committed_content(root, commits_all, pathspecs)
    if content is UNREADABLE:
        sys.stderr.write(
            "Refusing `git commit`: what this commit would record could not be read.\n\n"
            "Every check below reads that content, so they would all run over nothing and pass, and "
            "the commit would be recorded with none of them having seen it.\n\n"
            "Retry when git answers.\n")
        return 2
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

    commits = commit_invocations(event.get("tool_input", {}).get("command", ""))
    if not commits:
        return 0

    cwd = event.get("cwd") or "."
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
