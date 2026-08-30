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

Two gh modes are posed to a Stop guard: nothing answers, and the listing answers while every
per-pull-request read fails. The second is the one a REST fallback creates, and it is where a guard
that reads its subject two ways can report on pull requests it learned nothing about. An empty
successful answer is posed to neither — no open pull request is the ordinary state
`open_backlog.py` exists to act on. The git mode is out for a different reason: it stubs git alone,
so the real `gh` runs, and a check that reaches the network answers about the network.

A guard whose own question is answered in a mode that broke somebody else's reading declares
`UNREADABLE_ALLOWS`, with a comment and with a sibling that refuses there — an exemption no other
guard stands behind is the silence this exists to stop.

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
GUARD_FLOOR = 16
STOP_FLOOR = 2

POLICY = "UNREADABLE_POLICY"
PROBE = "UNREADABLE_PROBE"
TOOLS = "HOOK_TOOLS"
ALLOWS = "UNREADABLE_ALLOWS"

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

# Its own table rather than a selection from MODES: the second mode below is about a guard that
# reads one thing through REST and the rest through GraphQL, which no PreToolUse guard does.
#
# gh-graphql-error is the state that made the fallback worse than no fallback. The listing answers
# over REST, every per-pull-request read still fails, and a guard that reads the first and reports on
# the second has answered about pull requests it learned nothing about. An empty answer is left out
# for the reason the docstring gives.
STOP_MODES = {
    "gh-error": {"gh": 'echo "gh: HTTP 502" >&2\nexit 1'},
    "gh-graphql-error": {"gh": 'if [ "$1" = "api" ]; then\n'
                               '  case "$2" in\n'
                               '    *pulls*) echo 42 ;;\n'
                               '    user) echo someone ;;\n'
                               '    *) echo "[]" ;;\n'
                               '  esac\n'
                               '  exit 0\n'
                               'fi\n'
                               'echo "gh: API rate limit already exceeded" >&2\n'
                               'exit 1'},
}

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


def _stubs(mode, directory, log, table=None):
    for name, body in (table or MODES)[mode].items():
        script = directory / name
        script.write_text(f'#!/bin/sh\nprintf \'{name}\\n\' >> "${STUB_LOG}"\n{body}\n')
        script.chmod(0o755)


# The session every guard here runs under, and the one a case writes its deferrals as.
SESSION = "velvet-unreadable-state-check"


def run_guard(hook, payload, mode, cwd, home, table=None):
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
        # And its own id would decide whose a deferral is, so a case writing one wrote a line the
        # guard then disowned — three of them passed only where the variable was absent, which is CI
        # and not a session. Pinned rather than popped: with no id at all every line suppresses,
        # which is the reading a case has to be able to fail against.
        environment["CLAUDE_CODE_SESSION_ID"] = SESSION
        _stubs(mode, workspace, log, table)
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
    code, _, errors, consulted = run_guard(hook, STOP_PAYLOAD, mode, cwd, home, STOP_MODES)
    return (REFUSE if code == 2 else ALLOW), consulted, errors


def _stop_backed(hook, mode, cwd, home, siblings):
    """Whether some other Stop guard refuses under the same mode."""
    for sibling in siblings:
        if sibling == hook:
            continue
        verdict, _, _ = stop_answer(sibling, mode, cwd, home)
        if verdict == REFUSE:
            return sibling.name
    return None


def stop_faults(stop_directory, cwd, floor=STOP_FLOOR):
    """Every disagreement between what a Stop guard declares and what it does. Empty means agreement."""
    found = []
    subjects = guards(stop_directory)
    if len(subjects) < floor:
        found.append(f"{stop_directory} holds {len(subjects)} guards, fewer than {floor}")

    home = Path(tempfile.mkdtemp(prefix="velvet-unreadable-home-"))
    try:
        for hook in subjects:
            found += _stop_guard_faults(hook, subjects, cwd, home)
    finally:
        shutil.rmtree(home, ignore_errors=True)
    return found


def _stop_guard_faults(hook, subjects, cwd, home):
    source = Path(hook).read_text(encoding="utf-8")
    module = ast.parse(source)
    policy, policy_line = _assigned(module, POLICY)
    if policy not in POLICIES:
        return [f"{hook.name}: {POLICY} must be one of {', '.join(POLICIES)}, found {policy!r}"]
    if policy == ALLOW and not _has_reason(source, policy_line):
        return [f"{hook.name}: {POLICY} is \"{ALLOW}\" with no comment above it saying what holds "
                "instead"]

    allows, allows_line = _assigned(module, ALLOWS)
    if allows is not None and not _has_reason(source, allows_line):
        return [f"{hook.name}: {ALLOWS} with no comment above it saying why letting the session end "
                "is right in those modes"]
    allows = set(allows or ())

    found, evidence = [], []
    for mode in STOP_MODES:
        verdict, consulted, errors = stop_answer(hook, mode, cwd, home)
        if not consulted:
            continue
        evidence.append(mode)
        if mode in allows:
            found += _letting_go_faults(hook, mode, verdict, cwd, home, subjects, ALLOWS)
        elif policy == NONE:
            found.append(f"{hook.name}: declares \"{NONE}\" and consulted a broken program in {mode}")
        elif policy == ALLOW:
            found += _letting_go_faults(hook, mode, verdict, cwd, home, subjects, POLICY)
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


def _letting_go_faults(hook, mode, verdict, cwd, home, subjects, claim):
    """What is wrong with a guard that lets the session end: a stale claim, or one nothing backs.

    Two claims arrive here and neither may be taken on its own word. A guard whose own question was
    answered may let the session end in a mode that broke somebody else's reading, and a guard may
    declare outright that letting go is its policy — but what neither may do is be the only guard
    there, which is the claim turning into the silence the whole check exists to stop.

    The backing is measured under the empty HOME `stop_faults` makes, so it answers about the guards
    and not about a machine's deferrals — which is what makes it a verdict about this repository
    rather than about whoever ran it, and also the limit of what it can say: a sibling whose refusal
    a live deferral would suppress still counts as backing here.
    """
    if verdict == REFUSE:
        return [f"{hook.name}: {claim} says it lets go in {mode} and it refuses there — the "
                "declaration is stale"]
    backing = _stop_backed(hook, mode, cwd, home, subjects)
    return [] if backing else [
        f"{hook.name}: {claim} lets the session end in {mode} and no sibling refuses there, so it "
        "ends with the reading that failed unreported"]


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
