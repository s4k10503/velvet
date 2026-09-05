#!/usr/bin/env python3
"""Hold every refusing guard to which tree it answers about when the command changes directory.

A `PreToolUse` hook is handed the directory the tool call started in. `cd <worktree> && gh pr create`
runs somewhere else, so a guard reading that directory answers about a checkout the command has left
— and answers positively, printing a verdict about a tree it never opened. Two false refusals of that
shape were reported before it, one guard both times, and the fix put the reading in
`lib/shell_commands.py` where the guards that came after picked it up. What nobody asked afterwards
is which of the others read a directory at all: ten did, and the one that prompted this printed a
positive verdict rather than a refusal, which announces itself to nobody.

The contract is the one the two `cd`-reading guards implement: resolve the tree the command will act
on, or refuse because you cannot — never silently answer about a different one. Both halves are posed
here, since a guard can fail either: a move it could have placed and did not, and a move it could not
place and answered about anyway.

## What is measured, and why one reading is not enough

An exit code alone reports a false neutral. `blind_git_add.py` and `shared_git_state.py` refuse by
printing a deny decision and exiting 0, so scoring the code would read their refusals as passes. And
silence cannot separate a guard whose subject is not the tree from one that read the wrong tree and
had nothing to say about it either way.

So two readings are taken of every run:

- the verdict, from the exit code AND a deny decision on stdout;
- which tree the guard's own subprocesses addressed, from `git` and `gh` shims on PATH that record
  their working directory and their arguments before doing anything.

The tree reading decides wherever it speaks, and the verdict is what is left when a run addressed no
tree at all. A guard that addressed none in any run and answered the same from both trees is one this
cannot decide, and that covers two unlike cases: `merge_without_branch_deletion.py` reads the
command's own flags and no directory, while `library_seed_without_room.py` reads a directory this
sees nothing of, since it asks the filesystem for free space rather than asking git. So it is
reported as undecided rather than counted as agreement, and a guard that lands there wants a case in
its own suite. `DECIDED_FLOOR` is what fails when the instrument stops deciding at all: a shim that
logs nothing, or two trees that stop differing, scores every guard undecided and leaves the fault
list empty.

What is not decided here is what a guard SHOULD do once it has declined to place a move. Refusing and
standing down are both defensible and the guards differ, `tracked_writes.py` naming its own gap; what
none of them may do is read the tree the command left. So the unplaceable form scores that alone, and
reports the verdict beside it.

Run: python3 scripts/hooks/test_cwd_resolution_check.py
"""

import ast
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
REFUSE_DIRECTORY = ".claude/hooks/refuse"

# Raised with the tree, the way the sibling checks' floors are: an empty directory holds no guard
# that answers about the wrong tree, and would otherwise pass this.
GUARD_FLOOR = 16

# How many guards the two trees have to tell apart before a clean fault list means anything. Every
# guard undecided is what a dead shim and two identical trees both look like.
DECIDED_FLOOR = 8

PROBE = "UNREADABLE_PROBE"
TOOLS = "HOOK_TOOLS"
BASH = "Bash"

REFUSE, ALLOW = "refuse", "allow"

# What a guard did with a move it could have placed, and with one it could not.
FOLLOWS = "follows the cd"
HANDED = "answers about the handed tree"
UNDECIDED = "undecided: no tree reading seen"
REFUSES = "refuses, having not placed it"
STANDS_DOWN = "stands down, having not placed it"

HANDED_TREE, MOVED_TREE = "handed", "moved"

# A move nothing can place: the shell rewrites it after the hook has read the command, so the target
# is a literal naming no directory. `cd -` and `popd` reach the same outcome by naming a directory
# only the running shell knows.
UNPLACEABLE = 'cd "$VELVET_UNSET_WORKTREE"'

STUB_LOG = "VELVET_CWD_RESOLUTION_LOG"

# The one session every run here reports as, so a guard reading a deferral answers about these guards
# rather than about whoever ran the check.
SESSION = "velvet-cwd-resolution-check"

REAL_GIT = shutil.which("git") or "/usr/bin/git"

# gh answers nothing rather than reaching the network: what is read off a gh call here is the
# directory it was made from, which a failing gh records as surely as a working one, and a check that
# reached the network would answer about the network.
GH_STUB = 'echo "gh: no answer" >&2\nexit 1'


def _assigned(module, name):
    for node in module.body:
        if isinstance(node, ast.Assign) and any(
            isinstance(target, ast.Name) and target.id == name for target in node.targets
        ):
            try:
                return ast.literal_eval(node.value)
            except ValueError:
                return None
    return None


def declaration(path):
    """(probe command, the probe's other tool_input keys), or (None, None) where it poses none.

    The probe each guard already declares for the unreadable-state check, rather than a second one
    per guard: it is maintained for the property this needs — that it reaches the guard's own
    readings — and a guard added without one fails that check before it reaches this.
    """
    module = ast.parse(Path(path).read_text(encoding="utf-8"))
    probe = _assigned(module, PROBE)
    tools = _assigned(module, TOOLS)
    if not isinstance(probe, dict) or not isinstance(tools, set) or BASH not in tools:
        return None, None
    command = probe.get("command")
    if not isinstance(command, str) or not command:
        return None, None
    return command, {key: value for key, value in probe.items() if key != "command"}


def guards(refuse_directory):
    return sorted(Path(refuse_directory).glob("*.py"))


def _tree(root, name):
    """A git repository of its own, so a guard placing a tree lands in one or the other."""
    made = Path(root) / name
    made.mkdir(parents=True)
    (made / "seed.txt").write_text(name, encoding="utf-8")
    for args in (["init", "-q", "-b", "main", "."], ["add", "-A"],
                 ["-c", "user.email=check@velvet", "-c", "user.name=check", "commit", "-qm", name]):
        subprocess.run([REAL_GIT, *args], cwd=str(made), capture_output=True, check=True)
    return Path(os.path.realpath(made))


def _shims(root):
    """A `git` and a `gh` that record where they were run before doing anything.

    git goes on to the real one, because the tree readings this decides on are git's and a stub
    answering for it would decide the verdicts rather than observe them.
    """
    directory = Path(root) / "bin"
    directory.mkdir()
    (directory / "git").write_text(
        f'#!/bin/sh\nprintf \'%s\\t%s\\n\' "$PWD" "$*" >> "${STUB_LOG}"\nexec {REAL_GIT} "$@"\n',
        encoding="utf-8")
    (directory / "gh").write_text(
        f'#!/bin/sh\nprintf \'%s\\t%s\\n\' "$PWD" "$*" >> "${STUB_LOG}"\n{GH_STUB}\n',
        encoding="utf-8")
    for name in ("git", "gh"):
        (directory / name).chmod(0o755)
    return directory


def addressed(line, handed, moved):
    """Which of the two trees one recorded call was about, or None.

    A `-C` is what the call is about wherever it carries one, since that is the tree git acts on;
    otherwise it is the directory the call was made from. Composed in git's own order, so a second
    `-C` moves again from where the first arrived.
    """
    where, _, arguments = line.partition("\t")
    tokens = arguments.split()
    index = 0
    while index < len(tokens):
        flag, separator, attached = tokens[index].partition("=")
        if flag in ("-C", "--git-dir"):
            value = attached if separator else (tokens[index + 1] if index + 1 < len(tokens) else "")
            where = value if os.path.isabs(value) else os.path.join(where, value)
            index += 1 if separator else 2
            continue
        index += 1
    resolved = Path(os.path.realpath(where))
    for tree, name in ((moved, MOVED_TREE), (handed, HANDED_TREE)):
        if resolved == tree or tree in resolved.parents:
            return name
    return None


def run_guard(hook, tool_input, cwd, places):
    """(verdict, the trees this run addressed) for one guard posed one payload from one directory."""
    shims, home, handed, moved = places
    handle, log = tempfile.mkstemp(prefix="velvet-cwd-resolution-")
    os.close(handle)
    environment = dict(os.environ)
    environment["PATH"] = str(shims) + os.pathsep + environment.get("PATH", "")
    environment[STUB_LOG] = log
    environment["HOME"] = str(home)
    # The session's own project would otherwise decide what a guard reads instead of the directory it
    # was handed, and its own id whose a deferral is.
    environment.pop("CLAUDE_PROJECT_DIR", None)
    environment["CLAUDE_CODE_SESSION_ID"] = SESSION
    environment.pop("VELVET_UNSET_WORKTREE", None)
    event = {"tool_name": BASH, "cwd": str(cwd), "tool_input": tool_input}
    try:
        done = subprocess.run([sys.executable, "-B", str(hook)], input=json.dumps(event), text=True,
                              capture_output=True, timeout=120, env=environment, cwd=str(cwd))
        code, out = done.returncode, done.stdout
    except subprocess.TimeoutExpired:
        code, out = 1, ""
    finally:
        recorded = Path(log).read_text(encoding="utf-8")
        os.unlink(log)
    # Not the exit code alone: a guard that prints a deny decision and exits 0 has refused, and
    # reading 0 as a pass scores that refusal as a guard that let the tool through.
    denied = '"permissionDecision"' in out and '"deny"' in out
    trees = {name for line in recorded.splitlines() if line.strip()
             for name in [addressed(line, handed, moved)] if name}
    return (REFUSE if code == 2 or denied else ALLOW), trees


def placed_outcome(moved_run, cd_run, handed_run):
    """What a guard did with a `cd` into a literal directory it could have placed."""
    cd_verdict, cd_trees = cd_run
    if HANDED_TREE in cd_trees:
        return HANDED
    if MOVED_TREE in cd_trees:
        return FOLLOWS
    # No tree was addressed, so the verdict is what is left — and it says something only where the
    # two trees make the guard answer differently without one.
    if moved_run[0] == handed_run[0]:
        return UNDECIDED
    return FOLLOWS if cd_verdict == moved_run[0] else HANDED


def unplaced_outcome(run, placed):
    """What a guard did with a move nothing can place.

    Reading the handed tree is the fault here and the verdict is not: refusing and standing down are
    both defensible once a guard knows it cannot place the move, and which one is right is the
    guard's own declaration rather than this check's.
    """
    verdict, trees = run
    if HANDED_TREE in trees:
        return HANDED
    if placed == UNDECIDED:
        return UNDECIDED
    return REFUSES if verdict == REFUSE else STANDS_DOWN


def readings(refuse_directory, floor=GUARD_FLOOR):
    """[(guard, placed outcome, unplaced outcome, probe)] for every guard, and the faults in them."""
    found, faults = [], []
    subjects = guards(refuse_directory)
    if len(subjects) < floor:
        faults.append(f"{refuse_directory} holds {len(subjects)} guards, fewer than {floor}")

    root = Path(tempfile.mkdtemp(prefix="velvet-cwd-resolution-"))
    try:
        handed, moved = _tree(root, HANDED_TREE), _tree(root, MOVED_TREE)
        home = root / "home"
        home.mkdir()
        places = (_shims(root), home, handed, moved)
        for hook in subjects:
            command, extra = declaration(hook)
            if command is None:
                continue
            placed = placed_outcome(
                run_guard(hook, dict(extra, command=command), moved, places),
                run_guard(hook, dict(extra, command=f"cd {moved} && {command}"), handed, places),
                run_guard(hook, dict(extra, command=command), handed, places))
            unplaced = unplaced_outcome(
                run_guard(hook, dict(extra, command=f"{UNPLACEABLE} && {command}"), handed, places),
                placed)
            found.append((hook.name, placed, unplaced, command))
    finally:
        shutil.rmtree(root, ignore_errors=True)

    faults += [f"{name}: `cd <worktree> && {command}` {placed}"
               for name, placed, _, command in found if placed == HANDED]
    faults += [f"{name}: `{UNPLACEABLE} && {command}` {unplaced}"
               for name, _, unplaced, command in found if unplaced == HANDED]
    return found, faults


def decided(found):
    return [name for name, placed, _, _ in found if placed != UNDECIDED]


def main(argv):
    directory = Path(argv[1]) if len(argv) > 1 else REPO_ROOT / REFUSE_DIRECTORY
    found, faults = readings(directory)
    for name, placed, unplaced, command in found:
        print(f"{name:<42} {placed:<38} {unplaced:<36} {command}")
    if len(decided(found)) < DECIDED_FLOOR:
        faults.append(f"{len(decided(found))} guards were decided, fewer than {DECIDED_FLOOR} — the "
                      "two trees told nothing apart, so a clean list says nothing")
    for line in faults:
        print(line, file=sys.stderr)
    return 1 if faults else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
