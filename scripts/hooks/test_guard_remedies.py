#!/usr/bin/env python3
"""Put every command a guard prints as a remedy back through the guards that run on a Bash call.

A refusal exists to leave the caller somewhere to go. When the command it names is one another guard
refuses, it leaves the repository with no sanctioned exit at all, and the state that produces is the
ordinary one: `branch_from_unmerged.py` printed `git checkout main`, `git pull` and
`git checkout -b <name>`, and from a worktree on a feature branch — the shape an agent here is
handed — the third of those was refused twice over: once by a sibling guard, and once by the guard
that had just printed it.

Read off what the guard prints rather than compared with a fixed string: a remedy is allowed to
change shape, and a case that compares text passes the day the text moves without anyone checking
the new text is runnable. What the reading cannot reach is a command written into prose with neither
backticks around it nor a line of its own; those are the two shapes the guards here use.

Being let through is half of being runnable, so some cases run the commands instead and read the
state they leave: a command every guard here allows can still leave unchanged what it was meant to
change, or change it over a file the checkout held. git declining to fetch into a branch a worktree
has checked out is one way of producing the first, and no guard here has anything to say about
either.

Which guards get asked comes from .claude/settings.json rather than from the directory listing, so a
guard registered on a Bash call is asked whether or not anyone remembered this suite, and a file
sitting in the directory unregistered is not counted as refusing anything.

Run: python3 scripts/hooks/test_guard_remedies.py
"""

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SETTINGS = REPO_ROOT / ".claude/settings.json"
BRANCH_GUARD = REPO_ROOT / ".claude/hooks/refuse/branch_from_unmerged.py"
MERGE_GUARD = REPO_ROOT / ".claude/hooks/refuse/merge_branch_held_by_worktree.py"
STALE_MAIN = REPO_ROOT / ".claude/hooks/report/stale_main.py"

REFUSED = 2
ALLOWED = 0

# A default branch that is not `main`, with an ordinary `main` beside it, so a remedy spelling the
# base out and one reading git's record name different branches.
TRUNK = "trunk"

MERGE = "gh pr merge 7 --squash --delete-branch"

# The branch the primary checkout holds, and what the `gh` below names as the merge's head. Named by
# the pull request rather than read off HEAD, because the remedy moves that HEAD and a merge read the
# other way would then be asked about a different branch afterwards.
PRIMARY = "primary-work"
STUB_MERGE_GH = f'#!/bin/sh\nprintf "%s\\n" "{PRIMARY}"\n'

# A name no deferral could be carrying, since a deferred name suppresses the refusal under test.
CREATED = "tooling/remedy-probe"

# gh answers nothing: a pull-request read reaches the network, which this suite has no need of, and
# `stale_main` treats an unread base as no base rather than as main.
STUB_GH = "#!/bin/sh\nexit 1\n"

# A remedy line, and a remedy quoted inside a sentence. Both shapes are printed by the guards here.
COMMAND_LINE = re.compile(r"^[ \t]*((?:git|gh) .+?)[ \t]*$", re.M)
QUOTED_COMMAND = re.compile(r"`((?:git|gh) [^`]+)`")

# `<path>` and `<name>` stand where the caller substitutes their own, and left as they are the shell
# reads the first as a redirection — so the guards would be asked about a command no reader runs.
PLACEHOLDER = re.compile(r"<[^<>\s]+>")


def commands(text, quoted=True):
    """Each git or gh command the text offers as a line or in backticks, placeholders filled in.

    `quoted=False` drops the backticked shape, for a guard that spells the command it is refusing
    that way — `Refusing \\`gh pr merge\\`` is not a way out, and putting it back through the guards
    asks whether the refused command is refused.
    """
    found = []
    quotations = list(QUOTED_COMMAND.findall(text)) if quoted else []
    for match in list(COMMAND_LINE.findall(text)) + quotations:
        filled = PLACEHOLDER.sub("substituted", match).strip()
        if filled not in found:
            found.append(filled)
    return found


def bash_guards():
    """Each hook .claude/settings.json runs before a Bash tool call.

    A registration whose command spells its script some other way raises rather than being passed
    over: a sweep that quietly shrinks reports the same green as one that asked everything.
    """
    settings = json.loads(SETTINGS.read_text(encoding="utf-8"))
    found = []
    for entry in settings.get("hooks", {}).get("PreToolUse", []):
        if "Bash" not in (entry.get("matcher") or "").split("|"):
            continue
        for hook in entry.get("hooks", []):
            command = hook.get("command", "")
            named = re.search(r"\$CLAUDE_PROJECT_DIR/([^\"']+)", command)
            if not named:
                raise AssertionError(f"no script path read out of a Bash hook: {command!r}")
            found.append(REPO_ROOT / named.group(1))
    return found


def git(cwd, *args):
    """A failed arrangement is not a verdict, so one must not reach the assertion as an answer."""
    return subprocess.run(["git", "-C", str(cwd), *args], check=True, capture_output=True,
                          text=True, timeout=60)


def commit(cwd, message):
    git(cwd, "-c", "user.email=t@velvet", "-c", "user.name=t", "commit", "-q", "--allow-empty",
        "-m", message)


class GuardSet:
    """Putting a command to the guards a Bash tool call runs, given a fixture holding `stub`."""

    def environment(self, tree):
        made = dict(os.environ)
        made["PATH"] = str(self.stub) + os.pathsep + made.get("PATH", "")
        made["CLAUDE_PROJECT_DIR"] = str(tree)
        return made

    def refusal_of(self, guard, command, tree):
        """What `guard` writes to stderr when asked about `command` running in `tree`."""
        event = {"tool_name": "Bash", "cwd": str(tree), "tool_input": {"command": command}}
        return subprocess.run([sys.executable, "-B", str(guard)], input=json.dumps(event),
                              capture_output=True, text=True, env=self.environment(tree),
                              timeout=120)

    def refused_among(self, printed, tree):
        """(command, guard, first line of the refusal) for each printed command a guard refuses."""
        found, guards = [], bash_guards()
        for command in printed:
            event = {"tool_name": "Bash", "cwd": str(tree), "tool_input": {"command": command}}
            for guard in guards:
                answer = subprocess.run([sys.executable, "-B", str(guard)],
                                        input=json.dumps(event), capture_output=True, text=True,
                                        env=self.environment(tree), timeout=120)
                if answer.returncode != 0:
                    found.append((command, guard.name, answer.stderr.strip().splitlines()[:1]))
        return found


class RemedyTests(GuardSet, unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-remedies-"))
        self.origin = self.root / "origin.git"
        self.project = self.root / "project"
        self.wt = self.root / "wt"
        subprocess.run(["git", "init", "-q", "--bare", str(self.origin)], check=True, timeout=60)
        subprocess.run(["git", "init", "-q", "-b", "main", str(self.project)], check=True,
                       timeout=60)
        git(self.project, "remote", "add", "origin", str(self.origin))
        commit(self.project, "first")
        self.first = git(self.project, "rev-parse", "HEAD").stdout.strip()
        commit(self.project, "work that never landed")
        git(self.project, "branch", "feature")
        git(self.project, "reset", "-q", "--hard", self.first)
        commit(self.project, "second")
        self.second = git(self.project, "rev-parse", "HEAD").stdout.strip()
        commit(self.project, "third")
        git(self.project, "push", "-q", "origin", "main", f"main:{TRUNK}")
        git(self.project, "fetch", "-q", "origin")
        # The recorded default is not the branch these two guards report on. `shared_git_state.py`
        # lets a checkout of the recorded default through, so a remedy spelling `main` out is
        # refused here while one that moves no HEAD is not — which is what separates the two.
        git(self.project, "remote", "set-head", "origin", TRUNK)
        git(self.project, "worktree", "add", "-q", str(self.wt), "feature")

        self.stub = self.root / "bin"
        self.stub.mkdir()
        (self.stub / "gh").write_text(STUB_GH, encoding="utf-8")
        (self.stub / "gh").chmod(0o755)

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def report_of(self, tree):
        """What the session-start report prints for `tree`."""
        return subprocess.run([sys.executable, "-B", str(STALE_MAIN)], capture_output=True,
                              text=True, cwd=str(tree), env=self.environment(tree), timeout=120)

    def distance(self):
        """How many commits local main is behind origin/main."""
        return int(git(self.project, "rev-list", "--count", "main..origin/main").stdout.strip())

    def land(self, name, text):
        (self.project / name).write_text(text, encoding="utf-8")
        git(self.project, "add", name)
        commit(self.project, f"land {name}")
        git(self.project, "push", "-q", "origin", "HEAD:main")
        return git(self.project, "rev-parse", "HEAD").stdout.strip()

    def run_each(self, printed):
        for command in printed:
            subprocess.run(command, shell=True, cwd=str(self.project), capture_output=True,
                           timeout=120)

    def test_Given_HeadOnUnmergedWork_When_ABranchCreationIsRefused_Then_EveryCommandItPrintsIsAllowed(self):
        # Arrange — a worktree on a branch main does not contain, with local main current, so the
        # refusal carries the first arm alone and the commands under test are that arm's.
        refusal = self.refusal_of(BRANCH_GUARD, f"git checkout -b {CREATED}", self.wt)
        printed = commands(refusal.stderr)

        # Act
        refused = self.refused_among(printed, self.wt)

        # Assert — the fired-and-read gates ride in the comparison: a guard that said nothing, or a
        # message nothing was read out of, would otherwise leave this case green having asked
        # nothing at all.
        self.assertEqual((refusal.returncode, bool(printed), refused), (REFUSED, True, []))

    def test_Given_AStaleLocalMain_When_ABranchCreationIsRefused_Then_EveryCommandItPrintsIsAllowed(self):
        # Arrange — HEAD on main and local main behind origin/main, which is the second arm alone.
        git(self.project, "reset", "-q", "--hard", self.second)
        refusal = self.refusal_of(BRANCH_GUARD, f"git checkout -b {CREATED}", self.project)
        printed = commands(refusal.stderr)

        # Act
        refused = self.refused_among(printed, self.project)

        # Assert
        self.assertEqual((refusal.returncode, bool(printed), refused), (REFUSED, True, []))

    def test_Given_AStaleMainNoWorktreeHolds_When_TheReportIsTaken_Then_EveryCommandItPrintsIsAllowed(self):
        # Arrange — main behind and checked out nowhere, which is the state its refspec remedy is
        # available in and the one the report prints that remedy for.
        git(self.project, "checkout", "-q", "-b", "parked")
        git(self.project, "branch", "-f", "main", self.second)
        report = self.report_of(self.project)
        printed = commands(report.stdout)

        # Act
        refused = self.refused_among(printed, self.project)

        # Assert
        self.assertEqual(("Local main is 1 commit behind" in report.stdout, bool(printed), refused),
                         (True, True, []))

    def test_Given_AStaleMainAWorktreeHolds_When_TheReportIsTaken_Then_EveryCommandItPrintsIsAllowed(self):
        # Arrange — the same staleness with main checked out, which git declines to fetch into, so
        # the report prints its other remedy and this case is about that one.
        git(self.project, "checkout", "-q", "-b", "parked")
        git(self.project, "branch", "-f", "main", self.second)
        held = self.root / "held"
        git(self.project, "worktree", "add", "-q", str(held), "main")
        report = self.report_of(self.project)
        printed = commands(report.stdout)

        # Act
        refused = self.refused_among(printed, self.project)

        # Assert — the gate is the holder's own path, which is in the report only where the holder
        # was read. Gating on the staleness alone would leave this case green on a report that
        # printed the other state's remedy, which is the state this one is here to separate.
        self.assertEqual((str(held) in report.stdout, bool(printed), refused), (True, True, []))

    def test_Given_AStaleMainAWorktreeHolds_When_TheReportsCommandsAreRun_Then_MainIsCurrent(self):
        # Arrange — the same state as the case above. Being allowed by the guards is half of being
        # runnable: git declines to fetch into a branch a worktree holds, so a remedy every guard
        # lets through can still leave main exactly as stale as it was.
        git(self.project, "checkout", "-q", "-b", "parked")
        git(self.project, "branch", "-f", "main", self.second)
        git(self.project, "worktree", "add", "-q", str(self.root / "held"), "main")
        before = self.distance()

        # Act — each command as printed, with what git said about it left to the distance below.
        for command in commands(self.report_of(self.project).stdout):
            subprocess.run(command, shell=True, cwd=str(self.project), capture_output=True,
                           timeout=120)

        # Assert
        self.assertEqual((before, self.distance()), (1, 0))

    # GREEN_ON_BASE(characterization): the git behaviour the two local-main remedies are split by.
    # It is a fact about git rather than about this repository, so it belongs in a case that fails
    # when git stops holding it, and `holder_of` names this one instead of restating it. The
    # fast-forward carries the flags the report prints, which a clean checkout does not refuse.
    def test_Given_AWorktreeHoldingMain_When_BothSpellingsAreRun_Then_OnlyTheFastForwardAnswers(self):
        # Arrange — main behind and checked out, which is where the refspec spelling stops working.
        git(self.project, "checkout", "-q", "-b", "parked")
        git(self.project, "branch", "-f", "main", self.second)
        held = self.root / "held"
        git(self.project, "worktree", "add", "-q", str(held), "main")

        # Act
        refspec = subprocess.run(["git", "-C", str(self.project), "fetch", "origin", "main:main"],
                                 capture_output=True, text=True, timeout=120)
        forward = subprocess.run(["git", "-C", str(held), "merge", "--ff-only",
                                  "--no-overwrite-ignore", "--no-autostash", "origin/main"],
                                 capture_output=True, text=True, timeout=120)

        # Assert
        self.assertEqual((refspec.returncode == 0, forward.returncode == 0, self.distance()),
                         (False, True, 0))

    def test_Given_AHeldMainIgnoringAPathOriginAdds_When_TheReportsCommandsAreRun_Then_TheFileThereIsKept(self):
        # Arrange
        git(self.project, "checkout", "-q", "-b", "parked")
        git(self.project, "branch", "-f", "main", self.second)
        self.land("local.json", "origin's\n")
        held = self.root / "held"
        git(self.project, "worktree", "add", "-q", str(held), "main")
        exclude = self.project / ".git" / "info" / "exclude"
        exclude.parent.mkdir(exist_ok=True)
        exclude.write_text("local.json\n", encoding="utf-8")
        (held / "local.json").write_text("mine\n", encoding="utf-8")
        printed = commands(self.report_of(self.project).stdout)

        # Act
        self.run_each(printed)

        # Assert — gated on a printed command naming the holder: without one, nothing writes into
        # that checkout and the file is kept whatever the report printed.
        self.assertEqual((any(str(held) in command for command in printed),
                          (held / "local.json").read_text(encoding="utf-8")), (True, "mine\n"))

    def test_Given_AHeldMainWithAnEditUnderAutostash_When_TheReportsCommandsAreRun_Then_TheEditIsKept(self):
        # Arrange
        git(self.project, "checkout", "-q", "-b", "parked")
        landed = self.land("notes.md", "before\n")
        self.land("notes.md", "after\n")
        git(self.project, "branch", "-f", "main", landed)
        held = self.root / "held"
        git(self.project, "worktree", "add", "-q", str(held), "main")
        git(self.project, "config", "merge.autostash", "true")
        (held / "notes.md").write_text("mine\n", encoding="utf-8")
        printed = commands(self.report_of(self.project).stdout)

        # Act
        self.run_each(printed)

        # Assert
        self.assertEqual((any(str(held) in command for command in printed),
                          (held / "notes.md").read_text(encoding="utf-8")), (True, "mine\n"))

    def test_Given_ADetachedHeadBehindOrigin_When_TheReportIsTaken_Then_EveryCommandItPrintsIsAllowed(self):
        # Arrange — local main current so the local-main half stays silent and the commands read
        # are the detached half's own.
        git(self.wt, "checkout", "-q", "--detach", "HEAD")
        report = self.report_of(self.wt)
        printed = commands(report.stdout)

        # Act
        refused = self.refused_among(printed, self.wt)

        # Assert
        self.assertEqual(("HEAD is detached at" in report.stdout, bool(printed), refused),
                         (True, True, []))


class MergeRemedyTests(GuardSet, unittest.TestCase):
    """The merge guard's remedy for a branch the primary checkout holds, where the branch git records
    as the default is not called `main`.

    Its remedy is the one move `shared_git_state.py` lets out, so the two have to name the same
    branch. The default here is `trunk` with an ordinary `main` beside it, and neither half is what
    makes this case pass — simplify either away and it still does, while a remedy spelling `main`
    out stops being told apart from one that reads git's record.
    """

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-merge-remedy-"))
        remote = self.root / "origin.git"
        subprocess.run(["git", "init", "-q", "--bare", "-b", TRUNK, str(remote)], check=True,
                       timeout=60)
        seed = self.root / "seed"
        subprocess.run(["git", "init", "-q", "-b", TRUNK, str(seed)], check=True, timeout=60)
        commit(seed, "root")
        git(seed, "remote", "add", "origin", str(remote))
        git(seed, "push", "-q", "origin", TRUNK, f"{TRUNK}:main")
        shutil.rmtree(seed)
        self.repo = self.root / "repo"
        subprocess.run(["git", "clone", "-q", str(remote), str(self.repo)], check=True, timeout=60)
        git(self.repo, "branch", "main", "origin/main")
        git(self.repo, "checkout", "-q", "-b", PRIMARY)

        self.stub = self.root / "bin"
        self.stub.mkdir()
        (self.stub / "gh").write_text(STUB_MERGE_GH, encoding="utf-8")
        (self.stub / "gh").chmod(0o755)

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def test_Given_ThePrimaryCheckoutHoldsTheBranch_When_ItsSanctionedCommandsAreRun_Then_TheMergeIsLetThrough(self):
        # Arrange — the primary on the branch under merge, the one kind of holder no removal reaches.
        # The backticked shape is left out: this guard spells the command it is refusing that way,
        # and that is not a way out of the refusal.
        first = self.refusal_of(MERGE_GUARD, MERGE, self.repo)
        printed = commands(first.stderr, quoted=False)
        refused = self.refused_among(printed, self.repo)

        # Act — only what the guard set let through, since a remedy this repository refuses is one
        # no reader can run. What git says about each is left to the verdict below.
        for command in [line for line in printed if line not in {c for c, _, _ in refused}]:
            subprocess.run(command, shell=True, cwd=str(self.repo), capture_output=True,
                           timeout=120)

        # Assert — the first code and the refusals ride in the comparison so that a guard which
        # printed nothing, and one whose remedy was refused rather than ineffective, are each told
        # apart from a remedy that ran and freed the branch.
        self.assertEqual(
            (first.returncode, refused,
             self.refusal_of(MERGE_GUARD, MERGE, self.repo).returncode),
            (REFUSED, [], ALLOWED))


if __name__ == "__main__":
    unittest.main(verbosity=2)
