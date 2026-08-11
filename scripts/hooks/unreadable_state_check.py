#!/usr/bin/env python3
"""Hold every refusing guard to what it does when git or gh cannot answer.

A `PreToolUse` guard lets the tool through by exiting 0, and 0 is also what it exits when a reading
it depends on came back empty. The two are indistinguishable from outside, so a guard whose
repository read fails goes quiet exactly when it is the only thing left between a mistake and the
tool. It happened here: a GitHub GraphQL quota emptied `gh pr view --json headRefName` while the
separate REST quota stayed healthy, so `gh` went on working everywhere else and two merge guards
alone fell silent.

Which way to err is each guard's own decision — `metadata_less_create.py` reads the label list only
to write a more helpful refusal, and a guard over merges cannot allow one it did not check — so the
guard declares it and this compares the declaration against what the guard does.

Run rather than read. Which direction a `return 0` means depends on the call site:
`shared_git_state.py` answers "yes, a commit" when git cannot be asked, and that answer is its
refusal. A stub `git` exiting 1 reported it as failing open here, and exit 1 is what
`git rev-parse --verify --quiet` returns for a ref that is merely absent — so the stubs below fail
the way an unreadable state fails, and the verdict is read off the process.

A `Stop` guard is held to the same declaration and to one thing more. It already blocked when it
could not read, and what was wrong was what it said: it reported its own blindness in the shape of a
fact about its subject, so the deferral it invited named the API error rather than whatever the work
was waiting on. `lib/repository.py` owns the sentence that separates the two, and a Stop guard's
refusal has to carry it — hand-rolled, two guards say it two ways and one of them stops saying it.

Only the gh-error mode is posed to a Stop guard. An empty successful answer from the pull-request
listing is the ordinary state `open_backlog.py` exists to act on, so refusing on it would be wrong
there and right in its sibling; what both must agree on is the case where every way of asking fails.
The git mode is out for a different reason: it stubs git alone, so the real `gh` runs, and a check
that reaches the network answers about the network.

Run: python3 scripts/hooks/test_unreadable_state_check.py
"""

import ast
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
REFUSE_DIRECTORY = ".claude/hooks/refuse"
STOP_DIRECTORY = ".claude/hooks/stop"

# Raised with the tree, the way the hook fixtures' floors are: an empty directory declares nothing
# and would otherwise pass every check below.
GUARD_FLOOR = 14
STOP_FLOOR = 2

POLICY = "UNREADABLE_POLICY"
PROBE = "UNREADABLE_PROBE"
TOOLS = "HOOK_TOOLS"

REFUSE, ALLOW, NONE = "refuse", "allow", "none"
POLICIES = (REFUSE, ALLOW, NONE)

# Each mode breaks one program the way an unreadable state breaks it, and leaves the other alone, so
# a guard reading both is held to each read separately. git's exit 128 is its fatal code — 1 is a
# legitimate negative answer from `rev-parse --verify --quiet` and several other plumbing commands,
# and stubbing that reports a correct guard as failing open. gh has no such overload: it exits 1 on
# an API error. The empty mode is the one a renamed JSON field produces, where `--jq` selects
# nothing and the command still succeeds.
MODES = {
    "gh-error": {"gh": 'echo "gh: HTTP 502" >&2\nexit 1'},
    "gh-empty": {"gh": "exit 0"},
    "git-error": {"git": 'echo "fatal: probe" >&2\nexit 128'},
}

STUB_LOG = "VELVET_UNREADABLE_STATE_LOG"

# Only the mode where every reading fails; the docstring owns why an empty answer is each Stop
# guard's own question.
STOP_MODES = ("gh-error",)

STOP_PAYLOAD = json.dumps({"hook_event_name": "Stop", "stop_hook_active": False})


def load_hook_library():
    """Imports .claude/hooks/lib/repository.py by path, since .claude holds no packages."""
    path = REPO_ROOT / ".claude/hooks/lib/repository.py"
    spec = importlib.util.spec_from_file_location("hook_repository", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


SELF_REPORT = load_hook_library().SELF_REPORT


def guards(refuse_directory):
    return sorted(Path(refuse_directory).glob("*.py"))


def _assigned(module, name):
    """The literal a top-level `name = …` holds, with the line it sits on."""
    for node in module.body:
        if isinstance(node, ast.Assign) and any(
            isinstance(target, ast.Name) and target.id == name for target in node.targets
        ):
            try:
                return ast.literal_eval(node.value), node.lineno
            except ValueError:
                return None, node.lineno
    return None, None


def _has_reason(source, lineno):
    """Whether a comment sits directly above the line, with no blank line between."""
    lines = source.splitlines()
    index = lineno - 2
    return index >= 0 and lines[index].lstrip().startswith("#")


def declaration(path):
    """(policy, probe, tool) for one guard, with every way its declaration falls short."""
    source = Path(path).read_text(encoding="utf-8")
    module = ast.parse(source)
    policy, policy_line = _assigned(module, POLICY)
    probe, _ = _assigned(module, PROBE)
    tools, _ = _assigned(module, TOOLS)

    missing = []
    if policy not in POLICIES:
        missing.append(f"{POLICY} must be one of {', '.join(POLICIES)}, found {policy!r}")
    if not isinstance(probe, dict) or not probe:
        missing.append(f"{PROBE} must be a non-empty tool_input dict, found {probe!r}")
    if not isinstance(tools, set) or not tools:
        missing.append(f"{TOOLS} must be a non-empty set, found {tools!r}")
    if policy == ALLOW and not _has_reason(source, policy_line):
        missing.append(
            f"{POLICY} is \"{ALLOW}\" with no comment above it saying what holds instead")

    tool = sorted(tools)[0] if isinstance(tools, set) and tools else None
    return policy, probe, tool, missing


def _stubs(mode, directory, log):
    for name, body in MODES[mode].items():
        script = directory / name
        script.write_text(f'#!/bin/sh\nprintf \'{name}\\n\' >> "${STUB_LOG}"\n{body}\n')
        script.chmod(0o755)


def run_guard(hook, payload, mode, cwd, home):
    """(exit code, stdout, stderr, whether the broken program was consulted) for one run."""
    workspace = Path(tempfile.mkdtemp(prefix="velvet-unreadable-"))
    try:
        log = workspace / "consulted"
        log.write_text("")
        environment = dict(os.environ)
        environment["HOME"] = str(home)
        environment[STUB_LOG] = str(log)
        # The session's own project would otherwise decide what a guard reads instead of `cwd`.
        environment.pop("CLAUDE_PROJECT_DIR", None)
        _stubs(mode, workspace, log)
        environment["PATH"] = str(workspace) + os.pathsep + environment.get("PATH", "")

        finished = subprocess.run(
            [sys.executable, "-B", str(hook)],
            input=payload, text=True, capture_output=True,
            cwd=str(cwd), env=environment, timeout=180,
        )
        return (finished.returncode, finished.stdout, finished.stderr,
                bool(log.read_text().strip()))
    finally:
        shutil.rmtree(workspace, ignore_errors=True)


def answer(hook, tool, probe, mode, cwd, home):
    """(verdict, whether the broken program was consulted) for one guard under one mode."""
    payload = json.dumps({"tool_name": tool, "cwd": str(cwd), "tool_input": probe})
    code, out, _, consulted = run_guard(hook, payload, mode, cwd, home)
    # Not the exit code alone: blind_git_add.py refuses by printing a deny decision and exiting
    # 0, so reading 0 as a pass would score its refusal as a guard that let the tool through.
    denied = '"permissionDecision"' in out and '"deny"' in out
    return (REFUSE if code == 2 or denied else ALLOW), consulted


def _backed(hook, tool, probe, mode, cwd, home, siblings):
    """Whether some other guard refuses this probe while the same program cannot answer."""
    for sibling in siblings:
        if sibling == hook:
            continue
        verdict, _ = answer(sibling, tool, probe, mode, cwd, home)
        if verdict == REFUSE:
            return sibling.name
    return None


def faults(refuse_directory, cwd, floor=GUARD_FLOOR):
    """Every disagreement between what a guard declares and what it does. Empty means agreement."""
    found = []
    subjects = guards(refuse_directory)
    if len(subjects) < floor:
        found.append(f"{refuse_directory} holds {len(subjects)} guards, fewer than {floor}")

    # One HOME for every run: edit_while_a_ready_pr_sits.py reads the pull-request watcher's files
    # out of it, so a verdict would otherwise depend on what a developer's watcher last wrote.
    home = Path(tempfile.mkdtemp(prefix="velvet-unreadable-home-"))
    try:
        for hook in subjects:
            found += _guard_faults(hook, subjects, cwd, home)
    finally:
        shutil.rmtree(home, ignore_errors=True)
    return found


def _guard_faults(hook, subjects, cwd, home):
    policy, probe, tool, missing = declaration(hook)
    if missing:
        return [f"{hook.name}: {reason}" for reason in missing]

    found, evidence = [], []
    for mode in MODES:
        verdict, consulted = answer(hook, tool, probe, mode, cwd, home)
        if not consulted:
            # This mode broke a program the guard never reached, so its verdict says nothing about
            # what the guard does with an unreadable state. Scoring it would hold the guard to its
            # ordinary answer under a name about failure.
            continue
        evidence.append(mode)
        if policy == NONE:
            found.append(f"{hook.name}: declares \"{NONE}\" and consulted a broken program in {mode}")
        elif verdict != policy:
            found.append(f"{hook.name}: declares \"{policy}\", answers \"{verdict}\" in {mode}")
        elif policy == ALLOW and not _backed(hook, tool, probe, mode, cwd, home, subjects):
            found.append(
                f"{hook.name}: declares \"{ALLOW}\" in {mode} and no other guard refuses its probe, "
                "so the tool call is guarded by nothing at all")

    if policy != NONE and not evidence:
        found.append(
            f"{hook.name}: declares \"{policy}\" but its probe reaches neither git nor gh, so the "
            "declaration is about a reading that never happens")
    return found


def stop_answer(hook, mode, cwd, home):
    """(verdict, whether the broken program was consulted, what it printed) for one Stop guard."""
    code, _, errors, consulted = run_guard(hook, STOP_PAYLOAD, mode, cwd, home)
    return (REFUSE if code == 2 else ALLOW), consulted, errors


def stop_faults(stop_directory, cwd, floor=STOP_FLOOR):
    """Every disagreement between what a Stop guard declares and what it does. Empty means agreement."""
    found = []
    subjects = guards(stop_directory)
    if len(subjects) < floor:
        found.append(f"{stop_directory} holds {len(subjects)} guards, fewer than {floor}")

    home = Path(tempfile.mkdtemp(prefix="velvet-unreadable-home-"))
    try:
        for hook in subjects:
            found += _stop_guard_faults(hook, cwd, home)
    finally:
        shutil.rmtree(home, ignore_errors=True)
    return found


def _stop_guard_faults(hook, cwd, home):
    policy, _ = _assigned(ast.parse(Path(hook).read_text(encoding="utf-8")), POLICY)
    if policy not in POLICIES:
        return [f"{hook.name}: {POLICY} must be one of {', '.join(POLICIES)}, found {policy!r}"]

    found, evidence = [], []
    for mode in STOP_MODES:
        verdict, consulted, errors = stop_answer(hook, mode, cwd, home)
        if not consulted:
            continue
        evidence.append(mode)
        if policy == NONE:
            found.append(f"{hook.name}: declares \"{NONE}\" and consulted a broken program in {mode}")
        elif verdict != policy:
            found.append(f"{hook.name}: declares \"{policy}\", answers \"{verdict}\" in {mode}")
        elif verdict == REFUSE and SELF_REPORT not in errors:
            found.append(
                f"{hook.name}: blocks in {mode} without saying the reading is what failed, so it "
                "reports its own blindness as a fact about its subject")

    if policy != NONE and not evidence:
        found.append(f"{hook.name}: declares \"{policy}\" but its run never reaches gh, the one "
                     "reading posed here, so the declaration is about something that never happens")
    return found


def main(argv):
    directory = Path(argv[1]) if len(argv) > 1 else REPO_ROOT / REFUSE_DIRECTORY
    cwd = Path(argv[2]) if len(argv) > 2 else REPO_ROOT
    stop_directory = Path(argv[3]) if len(argv) > 3 else REPO_ROOT / STOP_DIRECTORY
    found = faults(directory, cwd) + stop_faults(stop_directory, cwd)
    for line in found:
        print(line, file=sys.stderr)
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
