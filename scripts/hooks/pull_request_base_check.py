#!/usr/bin/env python3
"""Hold every `gh pr merge` guard to the base its pull request names.

Two guards over merges were written while `main` was the only branch that took pull requests, and
the day a maintenance branch was cut both refused its release outright: one compared the head
against `origin/main` whatever the pull request targeted, the other asked `origin/main` for a
closed-and-unpublished release. No suite noticed, because nothing in the repository named a second
branch at all.

So the subject here is the directory rather than those two guards. A guard added later that reaches
for `main` instead of for the pull request's own base fails this without anybody remembering to
write a case for it.

Three worlds, each a real repository with a real `origin`:

- `maintenance-current` — the pull request targets `2.x`, its head contains `origin/2.x`, and `2.x`
  has published everything its CHANGELOG closed. `main` is meanwhile ahead and holds a closed
  version nobody published. **Every guard must allow**: each thing that would block this merge is
  true of `main` alone, and `main` is not what the pull request names.
- `maintenance-stale` — same, except the head does not contain `origin/2.x`.
- `maintenance-unpublished` — same as the first, except `2.x` is what holds the unpublished version.

The last two carry the floor. A directory of guards that never refuse anything would satisfy the
first world by doing nothing at all, so each of the other two has to leave at least one refusal
behind — which is also what says the guards read the named base rather than ignoring bases entirely.

The first world is posed a third time as a compound command naming two pull requests, the second of
them the merge onto `main` that the same world refuses on its own. A guard that answers differently
about the two is reading an operand rather than the command it was handed.

Run: python3 scripts/hooks/pull_request_base_check.py
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
REFUSE_DIRECTORY = ".claude/hooks/refuse"

# Raised with the tree, the way the other hook harnesses' floors are: an empty directory refuses
# nothing and would otherwise pass every world below.
GUARD_FLOOR = 16

# The command posed to every guard. It carries the deletion flag because a merge without one is
# refused for a reason that has nothing to do with the base, and this asks about the base alone.
COMMAND = "gh pr merge 1 --squash --delete-branch"

# Two merges in one command, the second onto a base the first does not name. A guard that reads one
# operand covers one of the merges it was posed, and the command lands both.
COMPOUND = COMMAND + " && gh pr merge 2 --squash --delete-branch"

CHANGELOG_PATH = "Packages/com.velvet.core/CHANGELOG.md"
PACKAGE_JSON_PATH = "Packages/com.velvet.core/package.json"

# A closed version and a tag to match it, so `origin/2.x` reads as a branch with nothing outstanding.
PUBLISHED = "1.0.0"
# Closed in the CHANGELOG with no tag anywhere, which is what `published_check.py` refuses a merge on.
UNPUBLISHED = "9.9.9"

# What the stub gh was asked that no world models. A guard reaching for a reading nobody arranged
# gets an unreadable state rather than a healthy one, and its verdict would then be about that.
# gh's alone: git is real here, so a git reading no world arranges answers about the world's own
# repository rather than failing, and nothing below sees it.
UNMODELLED = "VELVET_BASE_CHECK_UNMODELLED"

STUB_GH = '''#!/usr/bin/env python3
import json, os, sys

BY_NUMBER = json.loads(os.environ["VELVET_BASE_CHECK_PULLS"])


def unmodelled():
    with open(os.environ["VELVET_BASE_CHECK_UNMODELLED"], "a", encoding="utf-8") as record:
        record.write("gh " + " ".join(sys.argv[1:]) + "\\n")
    sys.stderr.write("gh: this world models no such call\\n")
    return 1


def selected(argv):
    """The dotted path --jq asks for, empty for the whole payload, None for anything else."""
    if "--jq" not in argv:
        return []
    query = argv[argv.index("--jq") + 1]
    return None if not query.startswith(".") else query.removeprefix(".").split(".")


def main():
    argv = sys.argv[1:]
    if not argv:
        return unmodelled()
    if argv[0] == "api" and "/pulls/" in argv[1]:
        path = selected(argv)
        number = argv[1].rsplit("/", 1)[1]
        if path is None or number not in BY_NUMBER:
            return unmodelled()
        answer = BY_NUMBER[number]
        for step in path:
            if not isinstance(answer, dict) or step not in answer:
                return unmodelled()
            answer = answer[step]
        sys.stdout.write(answer if isinstance(answer, str) else json.dumps(answer))
        return 0
    if argv[0] == "pr" and argv[1] == "view":
        pull = BY_NUMBER[next((token for token in argv if token.isdigit()), "1")]
        known = {"headRefOid": pull["head"]["sha"],
                 "headRefName": pull["head"]["ref"],
                 "baseRefName": pull["base"]["ref"]}
        asked = argv[argv.index("--json") + 1].split(",") if "--json" in argv else []
        if not asked or any(field not in known for field in asked):
            return unmodelled()
        sys.stdout.write(json.dumps({field: known[field] for field in asked}))
        return 0
    if argv[0] == "pr" and argv[1] == "checks":
        sys.stdout.write(json.dumps([{"name": "Required checks (Unity)", "bucket": "pass"}]))
        return 0
    return unmodelled()


sys.exit(main())
'''


def git(project, *args):
    subprocess.run(["git", "-C", str(project), *args], check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=60)


def changelog(*versions):
    """A CHANGELOG whose sections are all closed, newest first."""
    body = "# Changelog\n\n"
    for version in versions:
        body += f"## [{version}] - 2026-01-01\n\n### Highlights\n\nA change.\n\n"
    return body


def write(project, version, *closed):
    (project / CHANGELOG_PATH).parent.mkdir(parents=True, exist_ok=True)
    (project / CHANGELOG_PATH).write_text(changelog(*closed), encoding="utf-8")
    (project / PACKAGE_JSON_PATH).write_text(json.dumps({"name": "com.velvet.core",
                                                         "version": version}), encoding="utf-8")


def commit(project, message):
    git(project, "add", "-A")
    git(project, "-c", "user.email=check@velvet", "-c", "user.name=check",
        "commit", "-q", "-m", message, "--allow-empty")


def in_worktree(project, root, branch, body):
    """Runs `body` over a checkout of `branch`, leaving no worktree holding it afterwards.

    Nothing may still hold a branch when the guards run: one of them refuses a merge whose
    `--delete-branch` would fail against a worktree, and that refusal is not about the base.
    """
    work = root / ("work-" + branch.replace("/", "-").replace(".", "-"))
    subprocess.run(["git", "-C", str(project), "worktree", "add", "-q", str(work), branch],
                   check=True, timeout=60)
    body(work)
    subprocess.run(["git", "-C", str(project), "worktree", "remove", "--force", str(work)],
                   check=True, timeout=60)


def build_world(root, unpublished_on_maintenance):
    """A repository where `main` and `2.x` differ in the two facts a merge guard reads off a base.

    `main` is deliberately ahead of the fork point and deliberately holds a version nobody published:
    a guard that reaches for it instead of for the named base has both of the things it refuses on
    waiting there, so reaching is what the verdict reports.
    """
    root.mkdir(parents=True)
    origin = root / "origin.git"
    project = root / "project"
    subprocess.run(["git", "init", "-q", "--bare", str(origin)], check=True, timeout=60)
    subprocess.run(["git", "init", "-q", "-b", "main", str(project)], check=True, timeout=60)
    git(project, "remote", "add", "origin", str(origin))

    write(project, PUBLISHED, PUBLISHED)
    commit(project, "release " + PUBLISHED)
    git(project, "tag", "v" + PUBLISHED)
    # Cut before main moves, so a head off this line contains neither main's tip nor its CHANGELOG.
    git(project, "branch", "2.x")
    git(project, "branch", "behind")

    write(project, UNPUBLISHED, UNPUBLISHED, PUBLISHED)
    commit(project, "close " + UNPUBLISHED)

    def maintenance_commit(work):
        if unpublished_on_maintenance:
            write(work, UNPUBLISHED, UNPUBLISHED, PUBLISHED)
        commit(work, "work on the maintenance line")

    in_worktree(project, root, "2.x", maintenance_commit)
    git(project, "branch", "topic", "2.x")
    in_worktree(project, root, "topic", lambda work: commit(work, "the change under review"))

    git(project, "push", "-q", "origin", "main", "2.x", "behind", "topic", "--tags")
    return project


def at(project, head, base):
    """The pull request payload the stub gh answers with, carrying the head's real SHA."""
    sha = subprocess.run(["git", "-C", str(project), "rev-parse", "origin/" + head],
                         capture_output=True, text=True, check=True, timeout=60).stdout.strip()
    return {"number": 1, "draft": False, "mergeable_state": "clean",
            "head": {"ref": head, "sha": sha, "repo": {"full_name": "s4k10503/velvet"}},
            "base": {"ref": base, "repo": {"full_name": "s4k10503/velvet"}}}


def refusals(project, pulls, home, unmodelled, refuse_directory, command=COMMAND):
    """Which guards refuse this pull request, with the first line of each refusal."""
    workspace = Path(tempfile.mkdtemp(prefix="velvet-base-check-"))
    refused = []
    try:
        stub = workspace / "gh"
        stub.write_text(STUB_GH, encoding="utf-8")
        stub.chmod(0o755)
        environment = dict(os.environ)
        environment["PATH"] = str(workspace) + os.pathsep + environment.get("PATH", "")
        environment["HOME"] = str(home)
        # The session's own project would otherwise decide what a guard reads instead of `cwd`.
        environment.pop("CLAUDE_PROJECT_DIR", None)
        environment["VELVET_BASE_CHECK_PULLS"] = json.dumps(pulls)
        environment[UNMODELLED] = str(unmodelled)
        event = json.dumps({"tool_name": "Bash", "cwd": str(project),
                            "tool_input": {"command": command}})
        for guard in sorted(Path(refuse_directory).glob("*.py")):
            finished = subprocess.run([sys.executable, "-B", str(guard)], input=event, text=True,
                                      capture_output=True, cwd=str(project), env=environment,
                                      timeout=180)
            # Not the exit code alone: blind_git_add.py refuses by printing a deny decision and
            # exiting 0, so reading 0 as a pass would score such a refusal as a guard that allowed.
            denied = '"permissionDecision"' in finished.stdout and '"deny"' in finished.stdout
            if finished.returncode == 2 or denied:
                refused.append((guard.name, finished.stderr.strip().splitlines()[0]
                                if finished.stderr.strip() else ""))
    finally:
        shutil.rmtree(workspace, ignore_errors=True)
    return refused


def faults(refuse_directory=None, floor=GUARD_FLOOR):
    """Every guard that judged a base the pull request did not name. Empty means agreement."""
    refuse_directory = Path(refuse_directory or REPO_ROOT / REFUSE_DIRECTORY)
    found = []
    guards = sorted(refuse_directory.glob("*.py"))
    if len(guards) < floor:
        found.append(f"{refuse_directory} holds {len(guards)} guards, fewer than {floor}")

    root = Path(tempfile.mkdtemp(prefix="velvet-base-world-"))
    home = root / "home"
    home.mkdir()
    unmodelled = root / "unmodelled"
    unmodelled.write_text("", encoding="utf-8")
    try:
        def refused(project, head, base):
            return refusals(project, {"1": at(project, head, base)}, home, unmodelled,
                            refuse_directory)

        current = build_world(root / "current", unpublished_on_maintenance=False)
        for name, message in refused(current, "topic", "2.x"):
            found.append(f"{name}: refuses a pull request based on 2.x that nothing about 2.x "
                         f"blocks — {message}")

        if not refused(current, "behind", "2.x"):
            found.append("no guard refuses a head that does not contain the base it names, so the "
                         "world above is satisfied by guards that refuse nothing")

        outstanding = build_world(root / "outstanding", unpublished_on_maintenance=True)
        if not refused(outstanding, "topic", "2.x"):
            found.append("no guard refuses a merge onto a base holding a version the CHANGELOG "
                         "closed and nobody published, so the world above is satisfied by guards "
                         "that refuse nothing")

        # A compound command lands every merge it carries, so a guard that refuses one of them on
        # its own has to refuse the pair. Compared per guard rather than counted over the directory:
        # a floor is satisfied by whichever sibling still covers the command, and the guard that
        # stopped covering it is the one that has to be nameable.
        compound = {"1": at(current, "topic", "2.x"), "2": at(current, "topic", "main")}
        alone = {name for name, _ in refused(current, "topic", "main")}
        together = {name for name, _ in refusals(current, compound, home, unmodelled,
                                                 refuse_directory, COMPOUND)}
        for name in sorted(alone - together):
            found.append(f"{name}: refuses that merge on its own and allows a command carrying it "
                         f"second, so it reads an operand rather than the command")

        asked = sorted(set(unmodelled.read_text(encoding="utf-8").split("\n")) - {""})
        if asked:
            found.append("a guard asked gh for a reading no world here arranges, so its verdict is "
                         "about an unreadable state rather than about the base:\n  "
                         + "\n  ".join(asked))
    finally:
        shutil.rmtree(root, ignore_errors=True)
    return found


def main():
    found = faults()
    for fault in found:
        print(fault, file=sys.stderr)
    if found:
        print(f"\n{len(found)} guard(s) judged a base the pull request did not name. The base "
              f"belongs to the pull request; read it from there.", file=sys.stderr)
        return 1
    print("every refusing guard judges the base its pull request names")
    return 0


if __name__ == "__main__":
    sys.exit(main())
