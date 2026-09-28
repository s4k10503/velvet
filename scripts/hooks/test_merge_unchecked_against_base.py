#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/merge_unchecked_against_base.py's verdict on a merge.

Posed against a real repository with a real `origin`, so every containment the guard asks is git's
own answer, and a `gh` on PATH answering the pull request, each workflow's runs and the head's check
runs from a table.

Run: python3 scripts/hooks/test_merge_unchecked_against_base.py
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/refuse/merge_unchecked_against_base.py"

REFUSED = 2
ALLOWED = 0

MERGE = "gh pr merge 7 --squash --delete-branch"

CHANGELOG = "Packages/com.velvet.core/CHANGELOG.md"
HOME_REPOSITORY = "s4k10503/velvet"

# Answers the pull request, the listing of pull requests based on a branch, the runs listings and
# the check runs from `VELVET_UNCHECKED_TABLE`, and
# appends every call to `VELVET_UNCHECKED_CALLS` so an allow can be told from a guard that asked
# nothing.
STUB_GH = '''#!/usr/bin/env python3
import json, os, sys
from urllib.parse import parse_qs, urlparse

argv = sys.argv[1:]
with open(os.environ["VELVET_UNCHECKED_CALLS"], "a", encoding="utf-8") as calls:
    calls.write(" ".join(argv) + "\\n")
table = json.loads(os.environ["VELVET_UNCHECKED_TABLE"])
if argv[:1] != ["api"]:
    sys.exit(1)
if "/pulls?" in argv[1]:
    if table.get("targets_unreadable"):
        sys.stderr.write("gh: HTTP 502\\n")
        sys.exit(1)
    base = parse_qs(urlparse(argv[1]).query)["base"][0]
    print(json.dumps(table.get("targeted", {}).get(base, [])))
    sys.exit(0)
if "/pulls/" in argv[1]:
    pull = table["pulls"].get(argv[1].rsplit("/", 1)[1])
    if pull is None:
        sys.exit(1)
    print(json.dumps(pull))
    sys.exit(0)
if "/actions/workflows/" in argv[1]:
    if table.get("runs_unreadable"):
        sys.stderr.write("gh: HTTP 502\\n")
        sys.exit(1)
    workflow = argv[1].split("/actions/workflows/")[1].split("/")[0]
    branch = parse_qs(urlparse(argv[1]).query)["branch"][0]
    print(json.dumps({"workflow_runs": table["runs"].get(branch, {}).get(workflow, [])}))
    sys.exit(0)
if "/check-runs" in argv[1]:
    runs = table["checks"]
    print(json.dumps({"total_count": len(runs), "check_runs": runs}))
    sys.exit(0)
sys.exit(1)
'''

SUITES_RAN = [{"name": "Required checks (Unity)", "conclusion": "success"},
              {"name": "Unity tests (EditMode)", "conclusion": "success"},
              {"name": "Unity tests (PlayMode)", "conclusion": "success"}]
SUITES_SKIPPED = [{"name": "Required checks (Unity)", "conclusion": "success"},
                  {"name": "Unity tests (EditMode)", "conclusion": "skipped"},
                  {"name": "Unity tests (PlayMode)", "conclusion": "skipped"}]


def git(project, *args):
    return subprocess.run(["git", "-C", str(project), *args], check=True, capture_output=True,
                          text=True, timeout=60).stdout.strip()


def commit(project, message, changelog=None):
    if changelog is not None:
        (project / CHANGELOG).parent.mkdir(parents=True, exist_ok=True)
        (project / CHANGELOG).write_text(changelog, encoding="utf-8")
        git(project, "add", CHANGELOG)
    git(project, "-c", "user.email=check@velvet", "-c", "user.name=check", "commit", "-q",
        "--allow-empty", "-m", message)
    return git(project, "rev-parse", "HEAD")


def released(*versions):
    """A CHANGELOG with an open section over each of `versions`, closed, newest first."""
    return "# Changelog\n\n## [Unreleased]\n\n" + "".join(
        f"## [{version}] - 2026-01-01\n\n- A change.\n\n" for version in versions)


def run(number, conclusion, sha, status="completed"):
    return {"run_number": number, "status": status, "conclusion": conclusion, "head_sha": sha,
            "run_attempt": 1}


def pull(head, base, sha, home=HOME_REPOSITORY):
    return {"head": {"ref": head, "sha": sha, "repo": {"full_name": home}},
            "base": {"ref": base, "repo": {"full_name": HOME_REPOSITORY}}}


class UncheckedAgainstBaseTests(unittest.TestCase):
    """`topic` is cut from `main` at `fork_point`, which dated 1.0.0; `main` then moves on to `tip`
    with no release in between."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-unchecked-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        origin, self.project = self.root / "origin.git", self.root / "project"
        subprocess.run(["git", "init", "-q", "--bare", str(origin)], check=True, timeout=60)
        subprocess.run(["git", "init", "-q", "-b", "main", str(self.project)], check=True,
                       timeout=60)
        git(self.project, "remote", "add", "origin", str(origin))
        self.fork_point = commit(self.project, "release 1.0.0", released("1.0.0"))
        git(self.project, "branch", "topic")
        self.tip = commit(self.project, "main moves on")
        git(self.project, "push", "-q", "origin", "main", "topic")
        self.head = git(self.project, "rev-parse", "topic")

        self.binaries = self.root / "bin"
        self.binaries.mkdir()
        (self.binaries / "gh").write_text(STUB_GH, encoding="utf-8")
        (self.binaries / "gh").chmod(0o755)
        self.calls = self.root / "calls"

    def ask(self, runs, command=MERGE, runs_unreadable=False, pulls=None, checks=SUITES_RAN,
            targeted=None, targets_unreadable=False):
        """What the guard does with `command`, against runs on each base as given."""
        pulls = pulls or {"7": pull("topic", "main", self.head)}
        environment = dict(os.environ)
        environment["PATH"] = str(self.binaries) + os.pathsep + environment.get("PATH", "")
        environment["HOME"] = str(self.root)
        environment.pop("CLAUDE_PROJECT_DIR", None)
        environment["VELVET_UNCHECKED_CALLS"] = str(self.calls)
        environment["VELVET_UNCHECKED_TABLE"] = json.dumps(
            {"pulls": pulls, "runs": runs, "runs_unreadable": runs_unreadable, "checks": checks,
             "targeted": targeted or {}, "targets_unreadable": targets_unreadable})
        event = {"tool_name": "Bash", "cwd": str(self.project), "tool_input": {"command": command}}
        return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(event),
                              capture_output=True, text=True, env=environment, timeout=120)

    def listings_asked(self):
        """How many runs listings the guard asked for."""
        text = self.calls.read_text(encoding="utf-8") if self.calls.exists() else ""
        return sum("/actions/workflows/" in line for line in text.splitlines())

    def green(self):
        return {"main": {"test.yml": [run(2, "success", self.tip)],
                         "generators.yml": [run(2, "success", self.tip)]}}

    def test_Given_AHeadBehindOnlyNonReleaseCommitsOfAGreenBase_When_TheMergeIsAsked_Then_ItIsLetThrough(self):
        # Arrange / Act — `topic` lacks `tip`, holds the release commit, and both workflows passed.
        result = self.ask(self.green())

        # Assert — the listings asked ride in the comparison, because a guard that recognised
        # nothing lets the merge through too, having read nothing.
        self.assertEqual((result.returncode, self.listings_asked()), (ALLOWED, 2))

    def test_Given_AHeadBehindTheBasesNewestReleaseCommit_When_TheMergeIsAsked_Then_ItIsRefused(self):
        # Arrange — main dates 1.1.0 after `topic` was cut, and every run on it passed.
        dated = commit(self.project, "release 1.1.0", released("1.1.0", "1.0.0"))
        git(self.project, "push", "-q", "origin", "main")

        # Act
        result = self.ask(self.green())

        # Assert
        self.assertEqual((result.returncode, f"does not contain {dated[:7]}, which dated 1.1.0"
                          in result.stderr), (REFUSED, True))

    def test_Given_ABaseWhoseLastPushRunFailedPastTheHead_When_TheMergeIsAsked_Then_ItIsRefused(self):
        # Arrange
        runs = self.green()
        runs["main"]["generators.yml"] = [run(2, "failure", self.tip)]

        # Act
        result = self.ask(runs)

        # Assert
        self.assertEqual((result.returncode, f"generators.yml failed at {self.tip[:7]}" in result.stderr),
                         (REFUSED, True))

    def test_Given_ABaseWhoseLastPushRunFailedInsideTheHead_When_TheMergeIsAsked_Then_ItIsLetThrough(self):
        # Arrange — the failing commit is one `topic` holds, and its own suites ran over it.
        runs = self.green()
        runs["main"]["test.yml"] = [run(1, "failure", self.fork_point)]

        # Act
        result = self.ask(runs)

        # Assert
        self.assertEqual((result.returncode, self.listings_asked()), (ALLOWED, 2))

    def test_Given_AHeadHoldingTheFailingCommitWhoseSuitesWereSkipped_When_TheMergeIsAsked_Then_ItIsRefused(self):
        # Arrange — the aggregate passed with the suite jobs skipped, so nothing ran over the break.
        runs = self.green()
        runs["main"]["test.yml"] = [run(1, "failure", self.fork_point)]

        # Act
        result = self.ask(runs, checks=SUITES_SKIPPED)

        # Assert
        self.assertEqual((result.returncode, f"test.yml failed at {self.fork_point[:7]}"
                          in result.stderr), (REFUSED, True))

    def test_Given_AForkHeadNamedLikeABranchOnOrigin_When_TheMergeIsAsked_Then_ItIsRefused(self):
        # Arrange — `main` on the fork is not origin's `main`, which holds every commit asked of it.
        runs = self.green()
        runs["main"]["test.yml"] = [run(2, "failure", self.tip)]
        pulls = {"7": pull("main", "main", self.head, home="someone/velvet")}

        # Act
        result = self.ask(runs, pulls=pulls)

        # Assert
        self.assertEqual((result.returncode, "head on another repository" in result.stderr),
                         (REFUSED, True))

    def test_Given_RunsThatCannotBeRead_When_TheMergeIsAsked_Then_ItIsRefusedAsUnread(self):
        # Arrange / Act
        result = self.ask(self.green(), runs_unreadable=True)

        # Assert
        self.assertEqual((result.returncode, "could not be read" in result.stderr), (REFUSED, True))

    def test_Given_TwoMergesTheSecondOntoARedBase_When_TheCommandIsAsked_Then_ItIsRefused(self):
        # Arrange — the first merge targets a green base, so a guard reading one operand allows.
        runs = self.green()
        runs["2.x"] = {"test.yml": [run(1, "failure", self.tip)], "generators.yml": []}
        pulls = {"7": pull("topic", "main", self.head), "8": pull("topic", "2.x", self.head)}
        git(self.project, "push", "-q", "origin", "main:refs/heads/2.x")

        # Act
        result = self.ask(runs, command=MERGE + " && gh pr merge 8 --squash --delete-branch",
                          pulls=pulls)

        # Assert
        self.assertEqual((result.returncode, "would merge onto 2.x" in result.stderr),
                         (REFUSED, True))


    def test_Given_AMaintenanceLineMergedForward_When_TheMergeIsAsked_Then_ItIsRefused(self):
        # Arrange — every base reading is green, so the head's name is what refuses it.
        pulls = {"7": pull("2.x", "main", self.head)}

        # Act
        result = self.ask(self.green(), pulls=pulls)

        # Assert
        self.assertEqual((result.returncode, "its head 2.x is a long-lived branch" in result.stderr),
                         (REFUSED, True))

    def test_Given_AHeadAnotherPullRequestIsBasedOn_When_TheMergeIsAsked_Then_ItIsRefused(self):
        # Arrange
        targeted = {"topic": [{"number": 9}]}

        # Act
        result = self.ask(self.green(), targeted=targeted)

        # Assert
        self.assertEqual((result.returncode, "its head topic is a long-lived branch" in result.stderr),
                         (REFUSED, True))

    def test_Given_PullRequestsBasedOnTheHeadThatCannotBeRead_When_TheMergeIsAsked_Then_ItIsRefusedAsUnread(self):
        # Act
        result = self.ask(self.green(), targets_unreadable=True)

        # Assert
        self.assertEqual((result.returncode, "whether any pull request is based on topic could not "
                          "be read" in result.stderr), (REFUSED, True))


if __name__ == "__main__":
    unittest.main(verbosity=2)
