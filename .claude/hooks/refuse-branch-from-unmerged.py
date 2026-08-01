#!/usr/bin/env python3
"""Refuse creating a branch from HEAD when HEAD is not main and local main is not current.

A branch cut from another change's tip carried three commits from an unmerged pull request;
after that pull request was squash-merged, rebasing the new branch replayed content already on
main and conflicted, and the pull request had to be abandoned and reopened at a new number.
Branching from a stale main produces a branch the merge guard refuses later, when the fix is a
rebase rather than a different starting point.
"""

import json
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "lib"))
from velvet_hooks import BRANCH_BASES

# Anchored at a command position — start of input, or after a separator or newline. Quoted
# arguments and heredoc bodies are masked before matching; a newline inside them is not a command
# boundary. The blind-add sibling records the first version refused the change that would have
# fixed it.
_GIT = r"git\s+(?:-C\s+\S+\s+)?"
_END = r"(?:\s*$|\s*[;&|])"
ANCHOR = r"(?:^|[;&|]|\n)\s*"
# Trailing flags only — a non-flag token is a start point, which is what the guard is about.
_TRAILING_FLAGS = r"(?:\s+-\S+)*"
CHECKOUT_B = re.compile(
    ANCHOR + _GIT + r"checkout(?:\s+(?!-b\b)\S+)*\s+-b\s+([^-\s]\S+)" + _TRAILING_FLAGS + _END
)
SWITCH_CREATE = re.compile(
    ANCHOR + _GIT
    + r"switch(?:\s+(?!(?:-c|--create)\b)\S+)*\s+(?:-c|--create)\s+([^-\s]\S+)"
    + _TRAILING_FLAGS + _END
)
BRANCH = re.compile(ANCHOR + _GIT + r"branch\s+([^-\s]\S*)" + _END)


def mask_shell_literals(command):
    out = list(command)
    i = 0
    n = len(command)
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
            while i < n and command[i] != "\n":
                out[i] = " "
                i += 1
            if i < n:
                out[i] = "\n"
                i += 1
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
        else:
            i += 1
    return "".join(out)


def git(cwd, *args):
    return subprocess.run(
        ["git", "-C", cwd, *args],
        capture_output=True, text=True, timeout=30,
    )


def branch_name(command):
    masked = mask_shell_literals(command)
    for pattern in (CHECKOUT_B, SWITCH_CREATE, BRANCH):
        match = pattern.search(masked)
        if match:
            return match.group(1)
    return None


def deferred(key):
    hook_dir = os.path.dirname(os.path.abspath(__file__))
    deferrals = os.path.join(hook_dir, "lib", "deferrals.sh")
    proc = subprocess.run(
        ["bash", "-c",
         f'. "{deferrals}" && deferred "$1" && printf "%s\\t%s" "$DEFER_REASON" "$DEFER_AGE"',
         "deferred_check", key],
        capture_output=True, text=True, timeout=5,
    )
    if proc.returncode != 0 or not proc.stdout.strip():
        return None
    reason, age = proc.stdout.strip().split("\t", 1)
    return reason, int(age)


def head_description(cwd):
    ref = git(cwd, "rev-parse", "--abbrev-ref", "HEAD")
    branch = ref.stdout.strip()
    if branch and branch != "HEAD":
        return branch
    sha = git(cwd, "rev-parse", "--short", "HEAD")
    return f"detached at {sha.stdout.strip()}"


def record_branch_base(name, sha):
    try:
        with open(BRANCH_BASES, "a", encoding="utf-8") as bases:
            bases.write(f"{name} {sha}\n")
        return True
    except OSError as err:
        sys.stderr.write(f"Could not record branch base in {BRANCH_BASES}: {err}\n")
        return False


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") != "Bash":
        return 0
    command = event.get("tool_input", {}).get("command", "")
    name = branch_name(command)
    if not name:
        return 0

    cwd = event.get("cwd") or "."

    if deferred(name):
        # Parent tip at branch creation is gone after squash-merge; rebase --onto needs it recorded now.
        head_sha = git(cwd, "rev-parse", "HEAD").stdout.strip()
        record_branch_base(name, head_sha)
        sys.stderr.write(
            f"Recorded base {head_sha} for `{name}`. "
            f"After parent merges (assumes origin/main is current — fetch first if unsure):\n"
            f"git fetch origin main\n"
            f"git rebase --onto origin/main {head_sha}\n"
        )
        return 0


    head_not_main = False
    head_ref = git(cwd, "rev-parse", "--abbrev-ref", "HEAD").stdout.strip()
    if head_ref != "main":
        on_main = git(cwd, "merge-base", "--is-ancestor", "HEAD", "main")
        if on_main.returncode != 0:
            head_not_main = True

    main_behind = False
    behind_count = None
    origin_main = git(cwd, "rev-parse", "--verify", "origin/main")
    if origin_main.returncode != 0:
        origin_missing = True
    else:
        origin_missing = False
        count = git(cwd, "rev-list", "--count", "main..origin/main")
        behind_count = count.stdout.strip()
        if behind_count and behind_count != "0":
            main_behind = True

    if not head_not_main and not main_behind:
        return 0

    head = head_description(cwd)
    lines = []

    if head_not_main:
        lines.append(
            f"Refusing to create `{name}`: HEAD is {head}, not main and not a commit main contains."
        )
        lines += [
            "",
            "Branching from another change's tip carries its commits into the new branch. Here that "
            "produced a rebase against squash-merged content already on main, conflicts, and a pull "
            "request that had to be abandoned and reopened.",
            "",
            f"git checkout main",
            f"git pull",
            f"git checkout -b {name}",
            "",
            f"Or branch from main in one step: git checkout -b {name} main",
            "",
            f"To stack on unmerged work on purpose, record intent and retry:",
            f'  echo "{name} <why> $(date +%s)" >> ~/.velvet-pr-deferrals',
        ]

    if main_behind:
        if lines:
            lines.append("")
        lines.append(
            f"Refusing to create `{name}`: local main is {behind_count} commit(s) behind origin/main."
        )
        lines += [
            "",
            "Branching from a stale main produces a branch the merge guard refuses later; the fix "
            "then is a rebase rather than a different starting point.",
            "",
            "git checkout main",
            "git pull",
            f"git checkout -b {name}",
        ]

    if origin_missing and not main_behind:
        lines += [
            "",
            "origin/main is not present locally; staleness against it was not checked.",
        ]

    sys.stderr.write("\n".join(lines) + "\n")
    return 2


if __name__ == "__main__":
    sys.exit(main())
