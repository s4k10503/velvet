#!/usr/bin/env python3
"""Refuse `git commit` when staged files would fail fast deterministic checks.

Every check this repository owns runs either at CI, twenty minutes away, or never.
Nothing looks at what is about to be committed until integration. These checks finish
in well under a second and would have caught defects that instead reached a commit or CI today.
"""

import json
import os
import re
import shutil
import subprocess
import sys

# Anchored at a command position — start of input, or after a separator or newline. Quoted
# arguments and heredoc bodies are masked before matching; a commit message that names
# `git commit` is not one.
_GIT = r"git\s+(?:-C\s+\S+\s+)?"
ANCHOR = r"(?:^|[;&|]|\n)\s*"
COMMIT = re.compile(ANCHOR + _GIT + r"commit\b")
NEUTER_CUTS = "scripts/neuter-cuts.json"


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
        capture_output=True,
        text=True,
        timeout=30,
    )


def repo_root(cwd):
    result = git(cwd, "rev-parse", "--show-toplevel")
    if result.returncode != 0:
        return cwd
    return result.stdout.strip()


def staged_paths(cwd):
    result = git(cwd, "diff", "--cached", "--name-only", "--diff-filter=ACM")
    if result.returncode != 0:
        return []
    return [line for line in result.stdout.splitlines() if line.strip()]


def cut_targets(repo_root):
    cuts_path = os.path.join(repo_root, NEUTER_CUTS)
    if not os.path.exists(cuts_path):
        return set()
    with open(cuts_path, encoding="utf-8") as cuts_file:
        raw = json.load(cuts_file)
    targets = set()
    for cut in raw.get("cuts", []):
        for edit in cut.get("edits", []):
            targets.add(edit["file"])
    return targets


def refuse(path, output, reproduce):
    sys.stderr.write(
        f"Refusing `git commit`: {path} failed a fast check.\n\n"
        f"{output.rstrip()}\n\n"
        f"Reproduce: {reproduce}\n"
    )
    return 2


def check_python(path):
    reproduce = f"python3 -m py_compile {path}"
    proc = subprocess.run(
        ["python3", "-m", "py_compile", path],
        capture_output=True,
        text=True,
        timeout=30,
    )
    if proc.returncode != 0:
        output = proc.stderr or proc.stdout
        return refuse(path, output, reproduce)
    return 0


def check_shell(path):
    reproduce_bash = f"bash -n {path}"
    proc = subprocess.run(
        ["bash", "-n", path],
        capture_output=True,
        text=True,
        timeout=30,
    )
    if proc.returncode != 0:
        output = proc.stderr or proc.stdout
        return refuse(path, output, reproduce_bash)

    shellcheck = shutil.which("shellcheck")
    if not shellcheck:
        return 0

    reproduce_sc = f"shellcheck {path}"
    proc = subprocess.run(
        [shellcheck, path],
        capture_output=True,
        text=True,
        timeout=30,
    )
    if proc.returncode != 0:
        output = proc.stderr or proc.stdout
        return refuse(path, output, reproduce_sc)
    return 0


def check_json(path):
    reproduce = f"python3 -c 'import json; json.load(open({json.dumps(path)}))'"
    try:
        with open(path, encoding="utf-8") as json_file:
            json.load(json_file)
    except json.JSONDecodeError as err:
        return refuse(path, str(err), reproduce)
    except OSError as err:
        return refuse(path, str(err), reproduce)
    return 0


def check_yaml(path):
    try:
        import yaml
    except ImportError:
        return 0

    reproduce = (
        f"python3 -c 'import yaml; yaml.safe_load(open({json.dumps(path)}))'"
    )
    try:
        with open(path, encoding="utf-8") as yaml_file:
            yaml.safe_load(yaml_file)
    except yaml.YAMLError as err:
        return refuse(path, str(err), reproduce)
    except OSError as err:
        return refuse(path, str(err), reproduce)
    return 0


def check_neuter(repo_root):
    script = os.path.join(repo_root, "scripts", "neuter-check.py")
    reproduce = "python3 scripts/neuter-check.py --validate"
    proc = subprocess.run(
        ["python3", script, "--validate", "--project", repo_root],
        capture_output=True,
        text=True,
        timeout=30,
        cwd=repo_root,
    )
    if proc.returncode != 0:
        output = proc.stderr or proc.stdout
        return refuse(NEUTER_CUTS, output, reproduce)
    return 0


def run_checks(cwd):
    root = repo_root(cwd)
    paths = staged_paths(cwd)
    if not paths:
        return 0

    targets = cut_targets(root)
    neuter_needed = NEUTER_CUTS in paths or any(p in targets for p in paths)

    for rel in paths:
        path = os.path.join(root, rel)
        if rel.endswith(".py"):
            code = check_python(path)
            if code:
                return code
        elif rel.endswith(".sh"):
            code = check_shell(path)
            if code:
                return code
        elif rel.endswith(".json"):
            code = check_json(path)
            if code:
                return code
        elif rel.endswith(".yml") or rel.endswith(".yaml"):
            code = check_yaml(path)
            if code:
                return code

    if neuter_needed:
        return check_neuter(root)
    return 0


def staged_neuters(repo_root):
    """Cuts present in the staged content of the files the cut map names.

    A sweep holds a neuter in a production source until its `finally` restores it, and while it is
    there the cut reads as an ordinary modification — `git status` shows the file changed and the
    diff shows a plausible early return. Committing then captures a method that silently does
    nothing, for a reason the author cannot see in their own diff.

    Asked of the staged content rather than of the process list. A pattern over `ps` matches any
    command line that merely names the script, including the one carrying this check's own text,
    and it says nothing about a sweep that died leaving a neuter behind — the state
    `neuter-check.py`'s own `dirty_cut_files` refuses to start on top of.
    """
    cuts = os.path.join(repo_root, NEUTER_CUTS)
    if not os.path.exists(cuts):
        return []
    try:
        with open(cuts, encoding="utf-8") as handle:
            edits = [edit for cut in json.load(handle)["cuts"] for edit in cut["edits"]]
    except Exception:
        return []

    found = []
    for edit in edits:
        blob = subprocess.run(["git", "-C", repo_root, "show", ":" + edit["file"]],
                              capture_output=True, text=True)
        if blob.returncode != 0:
            continue
        lines = blob.stdout.splitlines()
        for index, line in enumerate(lines):
            if line.strip() != edit["anchor"]:
                continue
            body = next((i for i in range(index + 1, len(lines)) if lines[i].strip()), None)
            after = next((lines[i].strip() for i in range(body + 1, len(lines))
                          if lines[i].strip()), "") if body is not None else ""
            if after == edit["neuter"]:
                found.append(f"{edit['file']}: {edit['anchor']}")
    return found


def is_commit(command):
    return COMMIT.search(mask_shell_literals(command)) is not None


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") != "Bash":
        return 0
    command = event.get("tool_input", {}).get("command", "")
    if not is_commit(command):
        return 0

    cwd = event.get("cwd") or "."
    root = subprocess.run(["git", "-C", cwd, "rev-parse", "--show-toplevel"],
                          capture_output=True, text=True)
    if root.returncode == 0:
        neutered = staged_neuters(root.stdout.strip())
        if neutered:
            sys.stderr.write(
                "Refusing `git commit`: the staged content carries a neuter from a sweep.\n\n"
                + "\n".join("  " + entry for entry in neutered)
                + "\n\nEach names a method whose body would begin with the cut's early return — it "
                  "compiles, it reads as an ordinary change, and it does nothing. Wait for a running "
                  "sweep to restore it, or restore it yourself:\n"
                  "  git checkout -- <file>\n"
            )
            return 2

    return run_checks(cwd)


if __name__ == "__main__":
    sys.exit(main())
