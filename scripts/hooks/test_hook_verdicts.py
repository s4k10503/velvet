#!/usr/bin/env python3
"""Every hook posed an event it decides against and one it lets through, as the harness poses them.

The other harnesses here each ask one dimension of the guards -- an unreadable reading, a base, a
tree -- and `test_hook_coverage.py` asks only that some case names each hook. None of them asks each
hook the question it exists for: given the event it was written to stop, does it stop it, and given
the event beside it, does it stay out of the way.

So each hook under `refuse/` and `report/` has a fixture below holding a pair: an event the hook
decides against and a near miss it lets through, each run through `unreadable_state_check.run_hook`
-- the subprocess and environment pins that harness poses with, and its way of stubbing a program --
and a refusal read the way that harness reads one. A `report/` hook stops nothing, so its pair is a
state it reports and one it stays silent over. A refusal or a report is held to a phrase of its own
message as well, because a hook that spoke about this event for some other reason -- a reading that
failed, say -- has not shown it decides about the subject at all. `EveryHookIsPosedTests` is the
floor: a hook added without a pair here fails it.

No case reaches the real `gh`: one that arranges none is handed a `gh` that fails, and three of the
merge guards are posed inside `pull_request_base_check.py`'s worlds, with its stub.
`report/wedged_processes.py` reads the process table on Darwin alone, so both of its cases run it
with `platform.system` answering Darwin and a stubbed `ps`.

Run: python3 scripts/hooks/test_hook_verdicts.py
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import pull_request_base_check as worlds  # noqa: E402
import test_hook_coverage as coverage  # noqa: E402
import unreadable_state_check as harness  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOKS = REPO_ROOT / ".claude/hooks"

REFUSED, ALLOWED = "refused", "allowed"
REPORTED, SILENT = "reported", "silent"

# The suffix each verdict's case name ends on, which is how the floor below finds a pair.
VERDICT_SUFFIXES = {
    "refuse": ("_Then_ItRefuses", "_Then_ItAllows"),
    "report": ("_Then_ItReports", "_Then_ItStaysSilent"),
}

# What git is told to commit as, since HOME is a scratch directory with no identity in it.
IDENTITY = ("-c", "user.email=verdicts@velvet", "-c", "user.name=verdicts")

# What a case that arranges no `gh` is handed, so no verdict depends on the network or on who is
# signed in to it.
UNARRANGED_GH = 'echo "gh: this case arranges no gh" >&2\nexit 1'


def git(cwd, *args):
    """A failed arrangement is not a verdict, so one must not reach the assertion as an answer."""
    return subprocess.run(["git", "-C", str(cwd), *args], check=True, capture_output=True,
                          text=True, timeout=60).stdout


class VerdictFixture(unittest.TestCase):
    """A scratch root, a HOME under it, and the way a case poses its hook."""

    HOOK = ""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-verdict-")).resolve()
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.home = self.root / "home"
        self.home.mkdir()

    def repository(self, name="tree"):
        """A repository on `main` holding one commit of a README."""
        tree = self.root / name
        tree.mkdir()
        git(tree, "init", "-q", "-b", "main")
        (tree / "README.md").write_text("readme\n", encoding="utf-8")
        self.commit(tree, "README.md")
        return tree

    def commit(self, tree, *paths, message="change"):
        git(tree, "add", "--", *paths)
        git(tree, *IDENTITY, "commit", "-q", "-m", message)

    def with_origin(self, tree):
        """A bare `origin` for `tree`, with `main` pushed to it."""
        origin = self.root / (tree.name + "-origin.git")
        subprocess.run(["git", "init", "-q", "--bare", str(origin)], check=True,
                       capture_output=True, timeout=60)
        git(tree, "remote", "add", "origin", str(origin))
        git(tree, "push", "-q", "-u", "origin", "main")
        return origin

    def heartbeat(self):
        """A heartbeat the watcher would write now, naming a process that is running: this one."""
        (self.home / ".velvet-pr-watch.heartbeat").write_text(
            f"{int(time.time())} {os.getpid()}\n", encoding="utf-8")

    def run_hook(self, event, cwd, stubs=None, environment=None):
        """(exit code, stdout, stderr) of the hook posed `event` from `cwd`."""
        code, out, err, _ = harness.run_hook(HOOKS / self.HOOK, json.dumps(event), cwd, self.home,
                                             {"gh": UNARRANGED_GH, **(stubs or {})}, environment)
        return code, out, err

    def pose(self, event, cwd, phrase="", stubs=None, environment=None):
        """(verdict, whether its message carries `phrase`) for a `refuse/` hook.

        An exit that is neither the allowing code nor a refusal is reported as itself, so a hook
        that raised reads as neither verdict.
        """
        code, out, err = self.run_hook(event, cwd, stubs, environment)
        if harness.refused(code, out):
            verdict = REFUSED
        elif code == 0:
            verdict = ALLOWED
        else:
            verdict = f"exit {code}: {err.strip()[-400:]}"
        return verdict, phrase in out + err

    def report(self, event, cwd, phrase="", stubs=None, environment=None):
        """(verdict, whether its output carries `phrase`) for a `report/` hook."""
        code, out, err = self.run_hook(event, cwd, stubs, environment)
        if code != 0:
            return f"exit {code}: {err.strip()[-400:]}", False
        return (REPORTED if out.strip() else SILENT), phrase in out


def bash(command, cwd, background=False):
    tool_input = {"command": command}
    if background:
        tool_input["run_in_background"] = True
    return {"hook_event_name": "PreToolUse", "tool_name": "Bash", "cwd": str(cwd),
            "tool_input": tool_input}


def edit(path, old, new, cwd):
    return {"hook_event_name": "PreToolUse", "tool_name": "Edit", "cwd": str(cwd),
            "tool_input": {"file_path": str(path), "old_string": old, "new_string": new}}


def write(path, content, cwd):
    return {"hook_event_name": "PreToolUse", "tool_name": "Write", "cwd": str(cwd),
            "tool_input": {"file_path": str(path), "content": content}}


# ---------------------------------------------------------------------------------------------
# refuse/


class AmendOfPublishedCommitVerdicts(VerdictFixture):
    HOOK = "refuse/amend_of_published_commit.py"
    COMMAND = "git commit --amend --no-edit"

    # GREEN_ON_BASE(characterization): the base already refuses amending a commit origin holds.
    def test_Given_HeadAlreadyOnTheRemote_When_ItIsAmended_Then_ItRefuses(self):
        # Arrange
        tree = self.repository()
        self.with_origin(tree)

        # Act
        answer = self.pose(bash(self.COMMAND, tree), tree, "--amend")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already lets an unpushed commit be amended.
    def test_Given_HeadNotYetPushed_When_ItIsAmended_Then_ItAllows(self):
        # Arrange
        tree = self.repository()
        self.with_origin(tree)
        (tree / "notes.md").write_text("notes\n", encoding="utf-8")
        self.commit(tree, "notes.md")

        # Act
        verdict, _ = self.pose(bash(self.COMMAND, tree), tree)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class BackgroundRelativePathVerdicts(VerdictFixture):
    HOOK = "refuse/background_relative_path.py"
    COMMAND = "python3 scripts/pr/settle.py watch"

    def arrange(self):
        tree = self.repository()
        (tree / "scripts" / "pr").mkdir(parents=True)
        return tree

    # GREEN_ON_BASE(characterization): the base already refuses a relative background command.
    def test_Given_ABackgroundCommandNamingARepoPathRelatively_When_Posed_Then_ItRefuses(self):
        # Arrange
        tree = self.arrange()

        # Act
        answer = self.pose(bash(self.COMMAND, tree, background=True), tree, "scripts/pr/settle.py")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows one that names where it runs.
    def test_Given_TheSameCommandOpeningWithAMoveIntoTheTree_When_Posed_Then_ItAllows(self):
        # Arrange
        tree = self.arrange()

        # Act
        verdict, _ = self.pose(bash(f"cd {tree} && {self.COMMAND}", tree, background=True), tree)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class BlindGitAddVerdicts(VerdictFixture):
    HOOK = "refuse/blind_git_add.py"

    # GREEN_ON_BASE(characterization): the base already denies staging the whole tree.
    def test_Given_AStagingOfEverything_When_Posed_Then_ItRefuses(self):
        # Arrange
        tree = self.repository()
        (tree / "stray.txt").write_text("stray\n", encoding="utf-8")

        # Act
        answer = self.pose(bash("git add -A", tree), tree, "stray.txt")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows staging a path by name.
    def test_Given_AStagingOfANamedPath_When_Posed_Then_ItAllows(self):
        # Arrange
        tree = self.repository()
        (tree / "stray.txt").write_text("stray\n", encoding="utf-8")

        # Act
        verdict, _ = self.pose(bash("git add README.md", tree), tree)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class BranchFromUnmergedVerdicts(VerdictFixture):
    HOOK = "refuse/branch_from_unmerged.py"
    COMMAND = "git switch -c next"

    # GREEN_ON_BASE(characterization): the base already refuses branching from an unmerged tip.
    def test_Given_HeadOnABranchMainDoesNotContain_When_ABranchIsCutFromIt_Then_ItRefuses(self):
        # Arrange
        tree = self.repository()
        git(tree, "switch", "-q", "-c", "feature")
        (tree / "feature.md").write_text("feature\n", encoding="utf-8")
        self.commit(tree, "feature.md")

        # Act
        answer = self.pose(bash(self.COMMAND, tree), tree, "not main and not a commit main contains")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows branching from a current main.
    def test_Given_HeadOnMain_When_ABranchIsCutFromIt_Then_ItAllows(self):
        # Arrange
        tree = self.repository()

        # Act
        verdict, _ = self.pose(bash(self.COMMAND, tree), tree)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class ChangelogIntoClosedVersionVerdicts(VerdictFixture):
    HOOK = "refuse/changelog_into_closed_version.py"
    CHANGELOG = ("# Changelog\n\n## [Unreleased]\n\n## [1.0.0] - 2026-01-01\n\n### Highlights\n\n"
                 "Shipped.\n\n### Added\n\n- The first entry.\n")

    def arrange(self):
        tree = self.repository()
        (tree / "CHANGELOG.md").write_text(self.CHANGELOG, encoding="utf-8")
        self.commit(tree, "CHANGELOG.md")
        return tree

    # GREEN_ON_BASE(characterization): the base already refuses an entry under a dated heading.
    def test_Given_AnEntryFiledUnderAReleasedVersion_When_TheEditIsPosed_Then_ItRefuses(self):
        # Arrange
        tree = self.arrange()
        event = edit(tree / "CHANGELOG.md", "- The first entry.\n",
                     "- The first entry.\n- A late entry.\n", tree)

        # Act
        answer = self.pose(event, tree, "1.0.0 comes out of it carrying a line")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows an entry under Unreleased.
    def test_Given_TheSameEntryFiledUnderUnreleased_When_TheEditIsPosed_Then_ItAllows(self):
        # Arrange
        tree = self.arrange()
        event = edit(tree / "CHANGELOG.md", "## [Unreleased]\n",
                     "## [Unreleased]\n\n### Added\n\n- A late entry.\n", tree)

        # Act
        verdict, _ = self.pose(event, tree)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class CommitFailingFastChecksVerdicts(VerdictFixture):
    HOOK = "refuse/commit_failing_fast_checks.py"
    COMMAND = "git commit -m 'Add the settings'"

    def staged(self, content):
        tree = self.repository()
        (tree / "settings.json").write_text(content, encoding="utf-8")
        git(tree, "add", "--", "settings.json")
        return tree

    # GREEN_ON_BASE(characterization): the base already refuses committing a malformed JSON blob.
    def test_Given_AStagedJsonFileThatDoesNotParse_When_ItIsCommitted_Then_ItRefuses(self):
        # Arrange
        tree = self.staged('{"key": \n')

        # Act
        answer = self.pose(bash(self.COMMAND, tree), tree, "settings.json failed a fast check")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows committing a JSON blob that parses.
    def test_Given_AStagedJsonFileThatParses_When_ItIsCommitted_Then_ItAllows(self):
        # Arrange
        tree = self.staged('{"key": 1}\n')

        # Act
        verdict, _ = self.pose(bash(self.COMMAND, tree), tree)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class DeclarationFirstLineFragmentVerdicts(VerdictFixture):
    HOOK = "refuse/declaration_first_line_fragment.py"
    MARKER = "GREEN_ON_BASE(characterization):"

    def written(self, first_line):
        content = (f"class SampleTests:\n    # {self.MARKER} {first_line}\n"
                   "    # question this case pins.\n    def test_one(self):\n        pass\n")
        return write(self.root / "test_sample.py", content, self.root)

    # GREEN_ON_BASE(characterization): the base already refuses a first line ending on an article.
    def test_Given_ADeclarationWhoseFirstLineEndsOnAnArticle_When_Written_Then_ItRefuses(self):
        # Arrange
        event = self.written("the base already answers the")

        # Act
        answer = self.pose(event, self.root, "ends on 'the'")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows a first line that is a claim.
    def test_Given_ADeclarationWhoseFirstLineIsAClaim_When_Written_Then_ItAllows(self):
        # Arrange
        event = self.written("the base already answers this the same way.")

        # Act
        verdict, _ = self.pose(event, self.root)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class EditWhileAReadyPrSitsVerdicts(VerdictFixture):
    HOOK = "refuse/edit_while_a_ready_pr_sits.py"

    def edited(self, ready_for):
        """An edit posed while a live watcher records one pull request ready for `ready_for` s."""
        self.heartbeat()
        (self.home / ".velvet-pr-ready").write_text(f"42 {int(time.time()) - ready_for}\n",
                                                    encoding="utf-8")
        tree = self.repository()
        return edit(tree / "README.md", "readme\n", "read me\n", tree), tree

    # GREEN_ON_BASE(characterization): the base already refuses an edit past the grace period.
    def test_Given_APullRequestGreenForHalfAnHour_When_AnEditIsPosed_Then_ItRefuses(self):
        # Arrange
        event, tree = self.edited(ready_for=1800)

        # Act
        answer = self.pose(event, tree, "#42")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows an edit inside the grace period.
    def test_Given_APullRequestGreenForAMinute_When_AnEditIsPosed_Then_ItAllows(self):
        # Arrange
        event, tree = self.edited(ready_for=60)

        # Act
        verdict, _ = self.pose(event, tree)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class HandRolledPrPollerVerdicts(VerdictFixture):
    HOOK = "refuse/hand_rolled_pr_poller.py"
    COMMAND = "until gh pr checks 42; do sleep 60; done"

    # GREEN_ON_BASE(characterization): the base already refuses a second poller beside a watcher.
    def test_Given_ALiveWatcher_When_ABackgroundLoopPollsGh_Then_ItRefuses(self):
        # Arrange
        self.heartbeat()

        # Act
        answer = self.pose(bash(self.COMMAND, self.root, background=True), self.root,
                           "A watcher is live")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows the loop with nothing watching.
    def test_Given_NoWatcher_When_TheSameLoopIsPosed_Then_ItAllows(self):
        # Arrange — the HOME holds no heartbeat.

        # Act
        verdict, _ = self.pose(bash(self.COMMAND, self.root, background=True), self.root)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class LibrarySeedWithoutRoomVerdicts(VerdictFixture):
    HOOK = "refuse/library_seed_without_room.py"

    def seeded(self, size):
        """A seed of a Library holding one sparse file of `size` bytes, so no room is spent on it."""
        source = self.root / "other" / "Library"
        source.mkdir(parents=True)
        with open(source / "ArtifactDB", "wb") as handle:
            handle.truncate(size)
        worktree = self.root / "worktree"
        worktree.mkdir()
        return bash(f"rsync -a {source}/ Library/", worktree), worktree

    # GREEN_ON_BASE(characterization): the base already refuses a copy larger than half the room.
    def test_Given_ASourceAsLargeAsTheFreeSpace_When_ItIsSeeded_Then_ItRefuses(self):
        # Arrange
        event, worktree = self.seeded(shutil.disk_usage(self.root).free)

        # Act
        answer = self.pose(event, worktree, "the disk has no room for it")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows a copy the disk has room for.
    def test_Given_ASmallSource_When_ItIsSeeded_Then_ItAllows(self):
        # Arrange
        event, worktree = self.seeded(1024)

        # Act
        verdict, _ = self.pose(event, worktree)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class MergeBranchHeldByWorktreeVerdicts(VerdictFixture):
    HOOK = "refuse/merge_branch_held_by_worktree.py"
    COMMAND = "gh pr merge 7 --squash --delete-branch"
    # The head of pull request 7, which is all this guard asks gh.
    STUBS = {"gh": 'case "$*" in *pulls/7*) echo topic ;; *) exit 1 ;; esac'}

    # GREEN_ON_BASE(characterization): the base already refuses deleting a branch a worktree holds.
    def test_Given_AWorktreeHoldingTheHeadBranch_When_ItIsMerged_Then_ItRefuses(self):
        # Arrange
        tree = self.repository()
        git(tree, "worktree", "add", "-q", "-b", "topic", str(self.root / "held"))

        # Act
        answer = self.pose(bash(self.COMMAND, tree), tree, "held by", self.STUBS)

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows the merge once nothing holds it.
    def test_Given_TheHeadBranchCheckedOutNowhere_When_ItIsMerged_Then_ItAllows(self):
        # Arrange
        tree = self.repository()
        git(tree, "branch", "topic")

        # Act
        verdict, _ = self.pose(bash(self.COMMAND, tree), tree, stubs=self.STUBS)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class MergeWorldFixture(VerdictFixture):
    """A merge posed inside one of `pull_request_base_check.py`'s worlds, with its stub gh."""

    @classmethod
    def setUpClass(cls):
        cls.worlds = Path(tempfile.mkdtemp(prefix="velvet-verdict-worlds-")).resolve()
        cls.stub = cls.worlds / "stub_gh.py"
        cls.stub.write_text(worlds.STUB_GH, encoding="utf-8")
        cls.current = worlds.build_world(cls.worlds / "current", unpublished_on_maintenance=False)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.worlds, ignore_errors=True)

    def merge(self, project, head, base, red=("main",), checks=None, phrase=""):
        unmodelled = self.root / "unmodelled"
        unmodelled.write_text("", encoding="utf-8")
        environment = {
            "VELVET_BASE_CHECK_PULLS": json.dumps({"1": worlds.at(project, head, base)}),
            "VELVET_BASE_CHECK_RUNS": json.dumps(worlds.push_runs(project, red=red)),
            worlds.UNMODELLED: str(unmodelled),
        }
        if checks is not None:
            environment["VELVET_BASE_CHECK_CHECKS"] = json.dumps(checks)
        stubs = {"gh": f'exec "{sys.executable}" "{self.stub}" "$@"'}
        return self.pose(bash(worlds.COMMAND, project), project, phrase, stubs, environment)


class MergeOntoUnpublishedReleaseVerdicts(MergeWorldFixture):
    HOOK = "refuse/merge_onto_unpublished_release.py"

    @classmethod
    def setUpClass(cls):
        super().setUpClass()
        cls.outstanding = worlds.build_world(cls.worlds / "outstanding",
                                             unpublished_on_maintenance=True)

    # GREEN_ON_BASE(characterization): the base already refuses a merge onto an unpublished release.
    def test_Given_ABaseHoldingAClosedVersionNobodyPublished_When_Merged_Then_ItRefuses(self):
        # Arrange / Act
        answer = self.merge(self.outstanding, "topic", "2.x", phrase="holds an unpublished release")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows a merge onto a published base.
    def test_Given_ABaseThatPublishedEveryVersionItClosed_When_Merged_Then_ItAllows(self):
        # Arrange / Act
        verdict, _ = self.merge(self.current, "topic", "2.x")

        # Assert
        self.assertEqual(verdict, ALLOWED)


class MergeUncheckedAgainstBaseVerdicts(MergeWorldFixture):
    HOOK = "refuse/merge_unchecked_against_base.py"

    # GREEN_ON_BASE(characterization): the base already refuses a head behind a red base push.
    def test_Given_ABaseWhosePushRunsFailedAtACommitTheHeadLacks_When_Merged_Then_ItRefuses(self):
        # Arrange / Act
        answer = self.merge(self.current, "behind", "2.x", red=("main", "2.x"),
                            phrase="whose last push verdict is a failure")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows the same head behind a green base.
    def test_Given_ABaseWhosePushRunsPassed_When_TheSameHeadIsMerged_Then_ItAllows(self):
        # Arrange / Act
        verdict, _ = self.merge(self.current, "behind", "2.x")

        # Assert
        self.assertEqual(verdict, ALLOWED)


class MergeUnprovenHeadVerdicts(MergeWorldFixture):
    HOOK = "refuse/merge_unproven_head.py"
    CHECK = "Required checks (Unity)"

    # GREEN_ON_BASE(characterization): the base already refuses a head whose own check failed.
    def test_Given_AHeadWhoseOwnCheckFailed_When_Merged_Then_ItRefuses(self):
        # Arrange / Act
        answer = self.merge(self.current, "topic", "2.x",
                            checks=[{"name": self.CHECK, "bucket": "fail"}],
                            phrase="not passing at")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows a head whose own check passed.
    def test_Given_AHeadWhoseOwnCheckPassed_When_Merged_Then_ItAllows(self):
        # Arrange / Act
        verdict, _ = self.merge(self.current, "topic", "2.x",
                                checks=[{"name": self.CHECK, "bucket": "pass"}])

        # Assert
        self.assertEqual(verdict, ALLOWED)


class MergeWithoutBranchDeletionVerdicts(VerdictFixture):
    HOOK = "refuse/merge_without_branch_deletion.py"

    # GREEN_ON_BASE(characterization): the base already refuses a merge that keeps the branch.
    def test_Given_AMergeWithoutDeleteBranch_When_Posed_Then_ItRefuses(self):
        # Arrange / Act
        answer = self.pose(bash("gh pr merge 42 --squash", self.root), self.root, "#42")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows a merge that deletes the branch.
    def test_Given_TheSameMergeWithDeleteBranch_When_Posed_Then_ItAllows(self):
        # Arrange / Act
        verdict, _ = self.pose(bash("gh pr merge 42 --squash --delete-branch", self.root),
                               self.root)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class MessageOutsideItsWorktreeVerdicts(VerdictFixture):
    HOOK = "refuse/message_outside_its_worktree.py"

    def message_at(self, directory):
        directory.mkdir(parents=True, exist_ok=True)
        message = directory / "message.txt"
        message.write_text("fix(velvet): a change\n", encoding="utf-8")
        return message

    # GREEN_ON_BASE(characterization): the base already refuses a message from outside the tree.
    def test_Given_AMessageFileOutsideTheWorktree_When_Committed_Then_ItRefuses(self):
        # Arrange
        tree = self.repository()
        message = self.message_at(self.root / "scratch")

        # Act
        answer = self.pose(bash(f"git commit -F {message}", tree), tree, str(message))

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows a message inside the tree.
    def test_Given_AMessageFileInsideTheWorktree_When_Committed_Then_ItAllows(self):
        # Arrange
        tree = self.repository()
        message = self.message_at(tree / "Logs" / "work")

        # Act
        verdict, _ = self.pose(bash(f"git commit -F {message}", tree), tree)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class MetadataLessCreateVerdicts(VerdictFixture):
    HOOK = "refuse/metadata_less_create.py"
    # The label listing the refusal offers.
    STUBS = {"gh": "echo bug"}
    COMMAND = "gh issue create --title 'A flaky case' --body 'Seen twice.' --assignee @me"

    # GREEN_ON_BASE(characterization): the base already refuses an issue created with no label.
    def test_Given_AnIssueCreatedWithNoLabel_When_Posed_Then_ItRefuses(self):
        # Arrange / Act
        answer = self.pose(bash(self.COMMAND, self.root), self.root, "it sets no --label",
                           self.STUBS)

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows the issue once it carries a label.
    def test_Given_TheSameIssueWithALabel_When_Posed_Then_ItAllows(self):
        # Arrange / Act
        verdict, _ = self.pose(bash(self.COMMAND + " --label bug", self.root), self.root,
                               stubs=self.STUBS)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class PrBodyOfAnotherBranchVerdicts(VerdictFixture):
    HOOK = "refuse/pr_body_of_another_branch.py"

    def created(self, body):
        tree = self.repository()
        path = tree / "Logs" / "work" / "body.md"
        path.parent.mkdir(parents=True)
        path.write_text(body, encoding="utf-8")
        return bash(f"gh pr create --title 'A change' --body-file {path}", tree), tree

    # GREEN_ON_BASE(characterization): the base already refuses a body naming no issue.
    def test_Given_ABodyNamingNoIssue_When_ThePullRequestIsCreated_Then_ItRefuses(self):
        # Arrange
        event, tree = self.created("Tightens a reading.\n")

        # Act
        answer = self.pose(event, tree, "the body names no issue")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows a body closing an issue.
    def test_Given_ABodyClosingAnIssue_When_ThePullRequestIsCreated_Then_ItAllows(self):
        # Arrange
        event, tree = self.created("Closes #12.\n\nTightens a reading.\n")

        # Act
        verdict, _ = self.pose(event, tree)

        # Assert
        self.assertEqual(verdict, ALLOWED)


class SharedGitStateVerdicts(VerdictFixture):
    HOOK = "refuse/shared_git_state.py"

    def arrange(self):
        tree = self.repository()
        git(tree, "branch", "topic")
        return tree

    # GREEN_ON_BASE(characterization): the base already refuses checking out another branch.
    def test_Given_ACheckoutOfABranch_When_Posed_Then_ItRefuses(self):
        # Arrange
        tree = self.arrange()

        # Act
        answer = self.pose(bash("git checkout topic", tree), tree, "of a branch moves state")

        # Assert
        self.assertEqual(answer, (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base already allows a checkout restoring a file.
    def test_Given_ACheckoutRestoringAFile_When_Posed_Then_ItAllows(self):
        # Arrange
        tree = self.arrange()

        # Act
        verdict, _ = self.pose(bash("git checkout -- README.md", tree), tree)

        # Assert
        self.assertEqual(verdict, ALLOWED)


# ---------------------------------------------------------------------------------------------
# report/


class GitignoredWriteVerdicts(VerdictFixture):
    HOOK = "report/gitignored_write.py"

    def written(self, relative):
        tree = self.repository()
        (tree / ".gitignore").write_text("generated/\n", encoding="utf-8")
        self.commit(tree, ".gitignore")
        path = tree / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("class Thing {}\n", encoding="utf-8")
        return {"hook_event_name": "PostToolUse", "tool_name": "Write", "cwd": str(tree),
                "tool_input": {"file_path": str(path), "content": "class Thing {}\n"}}, tree

    # GREEN_ON_BASE(characterization): the base already reports a write under an ignored path.
    def test_Given_ASourceWrittenUnderAnIgnoredDirectory_When_Reported_Then_ItReports(self):
        # Arrange
        event, tree = self.written("generated/Thing.cs")

        # Act
        answer = self.report(event, tree, "generated/")

        # Assert
        self.assertEqual(answer, (REPORTED, True))

    # GREEN_ON_BASE(characterization): the base already stays silent over a source git will track.
    def test_Given_ASourceWrittenWhereGitTracksIt_When_Reported_Then_ItStaysSilent(self):
        # Arrange
        event, tree = self.written("src/Thing.cs")

        # Act
        verdict, _ = self.report(event, tree)

        # Assert
        self.assertEqual(verdict, SILENT)


class RepositoryLitterVerdicts(VerdictFixture):
    HOOK = "report/repository_litter.py"
    EVENT = {"hook_event_name": "SessionStart", "source": "startup"}
    # Set so far up that this machine's own project clones cannot reach it.
    ENVIRONMENT = {"VELVET_LITTER_CLONES": "1000000"}

    # GREEN_ON_BASE(characterization): the base already reports more branches than its limit.
    def test_Given_MoreLocalBranchesThanTheLimit_When_ASessionStarts_Then_ItReports(self):
        # Arrange
        tree = self.repository()
        for number in range(21):
            git(tree, "branch", f"spent/{number}")

        # Act
        answer = self.report(dict(self.EVENT, cwd=str(tree)), tree, "21 local branches",
                             environment=self.ENVIRONMENT)

        # Assert
        self.assertEqual(answer, (REPORTED, True))

    # GREEN_ON_BASE(characterization): the base already stays silent over a tidy checkout.
    def test_Given_OnlyMain_When_ASessionStarts_Then_ItStaysSilent(self):
        # Arrange
        tree = self.repository()

        # Act
        verdict, _ = self.report(dict(self.EVENT, cwd=str(tree)), tree,
                                 environment=self.ENVIRONMENT)

        # Assert
        self.assertEqual(verdict, SILENT)


class StaleMainVerdicts(VerdictFixture):
    HOOK = "report/stale_main.py"
    EVENT = {"hook_event_name": "SessionStart", "source": "startup"}

    def published(self):
        """A checkout on `main` whose origin holds one more commit than it was pushed with."""
        tree = self.repository()
        self.with_origin(tree)
        (tree / "later.md").write_text("later\n", encoding="utf-8")
        self.commit(tree, "later.md")
        git(tree, "push", "-q", "origin", "main")
        return tree

    # GREEN_ON_BASE(characterization): the base already reports a local main behind origin/main.
    def test_Given_LocalMainBehindOrigin_When_ASessionStarts_Then_ItReports(self):
        # Arrange
        tree = self.published()
        git(tree, "reset", "-q", "--hard", "HEAD~1")

        # Act
        answer = self.report(dict(self.EVENT, cwd=str(tree)), tree,
                             "Local main is 1 commit behind origin/main")

        # Assert
        self.assertEqual(answer, (REPORTED, True))

    # GREEN_ON_BASE(characterization): the base already stays silent over a current main.
    def test_Given_LocalMainLevelWithOrigin_When_ASessionStarts_Then_ItStaysSilent(self):
        # Arrange
        tree = self.published()

        # Act
        verdict, _ = self.report(dict(self.EVENT, cwd=str(tree)), tree)

        # Assert
        self.assertEqual(verdict, SILENT)


class UnreleasedMaintenanceLineVerdicts(VerdictFixture):
    HOOK = "report/unreleased_maintenance_line.py"
    EVENT = {"hook_event_name": "SessionStart", "source": "startup"}
    CHANGELOG = "Packages/com.velvet.core/CHANGELOG.md"
    RELEASED = "## [1.0.0] - 2026-01-01\n\n### Highlights\n\nShipped.\n"

    def line(self, unreleased):
        """A repository whose `2.x` on origin carries `unreleased` under its open section."""
        tree = self.repository()
        path = tree / self.CHANGELOG
        path.parent.mkdir(parents=True)
        path.write_text(f"# Changelog\n\n## [Unreleased]\n\n{self.RELEASED}", encoding="utf-8")
        self.commit(tree, self.CHANGELOG)
        self.with_origin(tree)
        git(tree, "switch", "-q", "-c", "2.x")
        path.write_text(f"# Changelog\n\n## [Unreleased]\n\n{unreleased}{self.RELEASED}",
                        encoding="utf-8")
        git(tree, "add", "--", self.CHANGELOG)
        git(tree, *IDENTITY, "commit", "-q", "--allow-empty", "-m", "backport")
        git(tree, "push", "-q", "origin", "2.x")
        git(tree, "switch", "-q", "main")
        return tree

    # GREEN_ON_BASE(characterization): the base already reports a backport nobody shipped.
    def test_Given_ALineHoldingAnUnreleasedEntry_When_ASessionStarts_Then_ItReports(self):
        # Arrange
        tree = self.line("- A backported fix.\n\n")

        # Act
        answer = self.report(dict(self.EVENT, cwd=str(tree)), tree,
                             "origin/2.x holds 1 unreleased CHANGELOG entry")

        # Assert
        self.assertEqual(answer, (REPORTED, True))

    # GREEN_ON_BASE(characterization): the base already stays silent over an empty open section.
    def test_Given_ALineWithNothingUnreleased_When_ASessionStarts_Then_ItStaysSilent(self):
        # Arrange
        tree = self.line("")

        # Act
        verdict, _ = self.report(dict(self.EVENT, cwd=str(tree)), tree)

        # Assert
        self.assertEqual(verdict, SILENT)


class UntrackedScratchVerdicts(VerdictFixture):
    HOOK = "report/untracked_scratch.py"

    # GREEN_ON_BASE(characterization): the base already reports a file left untracked.
    def test_Given_AFileLeftUntracked_When_ASubagentStops_Then_ItReports(self):
        # Arrange
        tree = self.repository()
        (tree / "Probe.cs").write_text("class Probe {}\n", encoding="utf-8")

        # Act
        answer = self.report({"hook_event_name": "SubagentStop", "cwd": str(tree)}, tree,
                             "Probe.cs")

        # Assert
        self.assertEqual(answer, (REPORTED, True))

    # GREEN_ON_BASE(characterization): the base already stays silent over a clean tree.
    def test_Given_ACleanTree_When_ASubagentStops_Then_ItStaysSilent(self):
        # Arrange
        tree = self.repository()

        # Act
        verdict, _ = self.report({"hook_event_name": "SubagentStop", "cwd": str(tree)}, tree)

        # Assert
        self.assertEqual(verdict, SILENT)


class WedgedProcessesVerdicts(VerdictFixture):
    HOOK = "report/wedged_processes.py"
    EVENT = {"hook_event_name": "SessionStart", "source": "startup"}

    def table(self, kilobytes):
        """The stubs and environment for a Darwin process table holding one wedged process."""
        shim = self.root / "darwin"
        shim.mkdir()
        (shim / "sitecustomize.py").write_text(
            "import platform\nplatform.system = lambda: 'Darwin'\n", encoding="utf-8")
        stubs = {"ps": f"printf 'UE {kilobytes} /usr/libexec/stuck\\nS 10 /usr/bin/probe\\n'"}
        return stubs, {"PYTHONPATH": str(shim), "VELVET_WEDGE_REPORT_MB": "1"}

    # GREEN_ON_BASE(characterization): the base already reports wedged processes past the gate.
    def test_Given_AWedgedProcessHoldingMoreThanTheGate_When_ASessionStarts_Then_ItReports(self):
        # Arrange
        stubs, environment = self.table(1024000)

        # Act
        answer = self.report(self.EVENT, self.root, "stuck", stubs, environment)

        # Assert
        self.assertEqual(answer, (REPORTED, True))

    # GREEN_ON_BASE(characterization): the base already stays silent below the memory gate.
    def test_Given_AWedgedProcessHoldingLessThanTheGate_When_ASessionStarts_Then_ItStaysSilent(self):
        # Arrange
        stubs, environment = self.table(100)

        # Act
        verdict, _ = self.report(self.EVENT, self.root, stubs=stubs, environment=environment)

        # Assert
        self.assertEqual(verdict, SILENT)


# ---------------------------------------------------------------------------------------------


def posed_pairs():
    """Each hook a fixture here poses, mapped to the verdict suffixes its case names end on."""
    found = {}
    for value in globals().values():
        if not (isinstance(value, type) and issubclass(value, VerdictFixture) and value.HOOK):
            continue
        names = [name for name in dir(value) if name.startswith("test_")]
        found.setdefault(value.HOOK, set()).update(
            suffix for suffix in sum(VERDICT_SUFFIXES.values(), ())
            if any(name.endswith(suffix) for name in names))
    return found


class EveryHookIsPosedTests(unittest.TestCase):
    """The floor: every hook the harness registers from has a pair above, and only those."""

    # GREEN_ON_BASE(construction): both sides are this file and the hook directories. Delete
    # `class BlindGitAddVerdicts` and this reddens.
    def test_Given_EveryHookUnderTheHarness_When_TheFixturesAreRead_Then_EachHasACasePerVerdict(self):
        # Arrange — the hook count rides in the comparison, because a scan finding no hook leaves
        # nothing unposed.
        hooks = coverage.hooks()
        posed = posed_pairs()

        # Act
        unposed = [coverage.named(hook) for hook in hooks
                   if set(VERDICT_SUFFIXES.get(hook.parent.name, ("unregistered directory",)))
                   - posed.get(f"{hook.parent.name}/{hook.name}", set())]
        stray = sorted(set(posed) - {f"{hook.parent.name}/{hook.name}" for hook in hooks})

        # Assert
        self.assertEqual((len(hooks) >= coverage.HOOK_FLOOR, unposed, stray), (True, [], []))


if __name__ == "__main__":
    unittest.main()
