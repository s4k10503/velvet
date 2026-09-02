#!/usr/bin/env python3
"""Unit tests for .claude/hooks/lib/tracked_writes.py and the two guards it puts on Bash.

The reading is an under-approximation by construction, so a case that only asks what it names would
pass for one that names everything and refuses the session's every command. Several of the cases
below are therefore the other direction — a scratch write, an operand the shell has yet to expand, a
directory the command moved out of, a file name that is markdown inside a heredoc — because a guard
sized wrongly there does not fail, it fires.

The other half a passing suite would still hide: whether either guard's verdict is the one its
subject asks for. A shell write onto a tracked file has to reach the refusal the editing tools
reach, the command that clears that refusal has to keep running, and the message has to say what the
reading did not see rather than implying it saw everything.

Run: python3 scripts/hooks/test_tracked_writes.py
"""

import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
READY_PR_GUARD = REPO_ROOT / ".claude/hooks/refuse/edit_while_a_ready_pr_sits.py"
CLOSED_VERSION_GUARD = REPO_ROOT / ".claude/hooks/refuse/changelog_into_closed_version.py"
LIBRARY = REPO_ROOT / ".claude/hooks/lib/tracked_writes.py"

CHANGELOG_REL = "Packages/com.velvet.core/CHANGELOG.md"
RELEASED = """# Changelog

## [Unreleased]

### Fixed

- Something not yet shipped.

## [2.0.0] - 2026-08-02

### Fixed

- A thing that shipped.
"""


def load_module():
    """Imports tracked_writes by path, since .claude holds no packages."""
    sys.path.insert(0, str(LIBRARY.parent))
    spec = importlib.util.spec_from_file_location("tracked_writes", LIBRARY)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


tracked_writes = load_module()


def scratch(prefix):
    return Path(tempfile.mkdtemp(prefix="velvet-" + prefix + "-"))


class ReadingTests(unittest.TestCase):
    """What the command reader names, before any question of a repository."""

    def setUp(self):
        self.root = scratch("write-reading")
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def named(self, command):
        return tracked_writes.literal_write_targets(command, str(self.root))

    def under(self, *parts):
        return str(self.root.joinpath(*parts))

    def test_Given_ASedRewritingAFileInPlace_When_TheCommandIsRead_Then_ThatFileIsNamed(self):
        # Arrange / Act — the BSD spelling, whose empty extension is an operand of its own.
        found = self.named("sed -i '' -e s/a/b/ notes.md")

        # Assert
        self.assertEqual(found, [self.under("notes.md")])

    def test_Given_ASedNotRewritingInPlace_When_TheCommandIsRead_Then_NoFileIsNamed(self):
        # Arrange / Act — the same command without `-i` writes its result to stdout.
        found = self.named("sed -e s/a/b/ notes.md")

        # Assert
        self.assertEqual(found, [])

    def test_Given_ASedWhoseAttachedScriptCarriesAnI_When_TheCommandIsRead_Then_NoFileIsNamed(self):
        # Arrange / Act — the script rides on its own option rather than in a token of its own, so
        # a reading that took `-e…` for a cluster of short options would find `-i` inside it.
        found = self.named("sed -e's/i/x/' notes.md")

        # Assert
        self.assertEqual(found, [])

    def test_Given_AFileNamedInsideAHeredocBody_When_TheCommandIsRead_Then_OnlyTheRealTargetIsNamed(self):
        # Arrange — markdown a pull-request body carries: a blockquote, and a line naming a command.
        command = "cat > body.md <<'EOF'\n> notes.md is where that lives\ncp a notes.md\nEOF\n"

        # Act
        found = self.named(command)

        # Assert
        self.assertEqual(found, [self.under("body.md")])

    def test_Given_AWriteAfterTheCommandHasMovedAgain_When_TheCommandIsRead_Then_NoFileIsNamed(self):
        # Arrange — the shape a session builds a throwaway repository with, which lands its write
        # under the temporary directory rather than under the one the tool call started in.
        command = "S=/tmp/elsewhere\nmkdir -p $S\ncd $S\nprintf 'x\\n' > notes.md"

        # Act
        found = self.named(command)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AnOperandTheShellHasYetToExpand_When_TheCommandIsRead_Then_NoFileIsNamed(self):
        # Arrange / Act
        found = self.named('sed -i \'\' -e s/a/b/ "$NOTES"')

        # Assert
        self.assertEqual(found, [])

    def test_Given_AMoveNamingADestination_When_TheCommandIsRead_Then_TheSourceUnderItIsNamed(self):
        # Arrange / Act — where the source lands if the destination is a directory. Whether it is
        # one is not arranged here: the reading offers both paths and the filesystem drops the
        # wrong one, which is the sibling case below.
        found = self.named("mv notes.md sub")

        # Assert
        self.assertIn(self.under("sub", "notes.md"), found)

    def test_Given_AMoveIntoADirectory_When_TheCommandIsRead_Then_TheDirectoryItselfIsNotNamed(self):
        # Arrange — a tracked file under the directory the move names, so git is asked about that
        # directory in the state the reading would ask about it.
        (self.root / "sub").mkdir()
        (self.root / "sub" / "kept.md").write_text("kept\n")
        subprocess.run(["git", "-C", str(self.root), "init", "--quiet"],
                       check=True, capture_output=True)
        subprocess.run(["git", "-C", str(self.root), "add", "sub/kept.md"],
                       check=True, capture_output=True)

        # Act
        found = self.named("mv notes.md sub")

        # Assert — git's own answer rides in the comparison, because it is what makes dropping the
        # directory necessary: `ls-files --error-unmatch` succeeds for one holding a tracked file,
        # so a directory reaching the tracked test would be refused over a path nothing writes. A
        # git that stops answering so reddens this rather than leaving the filter unexplained.
        self.assertEqual(
            (subprocess.run(["git", "-C", str(self.root), "ls-files", "--error-unmatch", "--",
                             self.under("sub")], capture_output=True).returncode,
             self.under("sub") in found),
            (0, False))

    def test_Given_ACopyOntoAFile_When_TheCommandIsRead_Then_ThatFileIsNamed(self):
        # Arrange / Act
        found = self.named("cp /etc/hosts notes.md")

        # Assert
        self.assertIn(self.under("notes.md"), found)


class TrackingTests(unittest.TestCase):
    """Which of the names the reader gives back git places in a repository."""

    def setUp(self):
        self.root = scratch("write-tracking")
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def test_Given_APathInNoRepositoryAtAll_When_TheTargetIsRead_Then_ItIsNotNamed(self):
        # Arrange — the deferral this repository's refusals ask a session to write is this shape:
        # an append onto a path under HOME, which no repository holds.
        command = "echo '1 held 0 s' >> " + str(self.root / ".velvet-pr-deferrals")

        # Act
        found = tracked_writes.tracked_writes(command, str(self.root))

        # Assert
        self.assertEqual(found, [])

    def test_Given_AGitThatCannotBeStarted_When_TheTargetIsRead_Then_ItCountsAsTracked(self):
        # Arrange — a git failing every call, which is how it reports both a directory outside any
        # repository and its own absence. Read in a subprocess, since the reader asks once per run.
        stub = self.root / "bin"
        stub.mkdir()
        (stub / "git").write_text("#!/bin/sh\necho 'fatal: probe' >&2\nexit 128\n")
        (stub / "git").chmod(0o755)
        program = (f"import sys; sys.path.insert(0, {str(LIBRARY.parent)!r});"
                   " import tracked_writes;"
                   f" print(tracked_writes.tracked_writes('printf x > notes.md', {str(self.root)!r}))")

        # Act
        done = subprocess.run([sys.executable, "-B", "-c", program], capture_output=True, text=True,
                              timeout=60, env=dict(os.environ, PATH=str(stub) + os.pathsep
                                                   + os.environ.get("PATH", "")))

        # Assert — the count rides along, because a reader that named nothing would print an empty
        # list whatever it decided about a git it could not start.
        self.assertEqual((done.returncode, done.stdout.strip()),
                         (0, "[" + repr(str(self.root / "notes.md")) + "]"))


class GuardTests(unittest.TestCase):
    """What the two guards do with a Bash command that writes a file the repository tracks."""

    def setUp(self):
        self.root = scratch("write-guard")
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.home = self.root / "home"
        self.home.mkdir()
        subprocess.run(["git", "-C", str(self.root), "init", "--quiet"],
                       check=True, capture_output=True)
        changelog = self.root / CHANGELOG_REL
        changelog.parent.mkdir(parents=True)
        changelog.write_text(RELEASED)
        (self.root / "CONTRIBUTING.md").write_text("How to contribute.\n")
        subprocess.run(["git", "-C", str(self.root), "add", "-A"], check=True, capture_output=True)
        # A watcher answering, and a pull request ready an hour ago, so the sitting branch is what
        # the ready-pull-request guard reaches rather than the branch about nothing watching.
        now = int(time.time())
        (self.home / ".velvet-pr-watch.heartbeat").write_text(f"{now} {os.getpid()}\n")
        (self.home / ".velvet-pr-ready").write_text(f"1 {now - 3600}\n")

    def verdict(self, guard, command):
        """(exit code, stderr) for one guard on one Bash command in this repository."""
        event = {"tool_name": "Bash", "cwd": str(self.root), "tool_input": {"command": command}}
        done = subprocess.run([sys.executable, "-B", str(guard)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=90,
                              env=dict(os.environ, HOME=str(self.home),
                                       CLAUDE_PROJECT_DIR=str(self.root)))
        return done.returncode, done.stderr

    def test_Given_AShellWriteOntoATrackedFile_When_APullRequestHasSat_Then_ItIsRefused(self):
        # Arrange / Act
        code, _ = self.verdict(READY_PR_GUARD, "printf 'x\\n' > CONTRIBUTING.md")

        # Assert
        self.assertEqual(code, 2)

    def test_Given_TheCommandThatClearsThatRefusal_When_APullRequestHasSat_Then_ItStillRuns(self):
        # Arrange / Act — the refusal names this, so a guard refusing it leaves no way out at all.
        code, _ = self.verdict(READY_PR_GUARD, "python3 scripts/pr/settle.py merge 1")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_ARefusalOfAShellWrite_When_ItsTextIsRead_Then_ItStatesWhatWasNotRead(self):
        # Arrange / Act
        _, said = self.verdict(READY_PR_GUARD, "printf 'x\\n' > CONTRIBUTING.md")

        # Assert
        self.assertIn(tracked_writes.LIMITS, said)

    def test_Given_AShellWriteOntoATrackedChangelog_When_TheClosedVersionGuardReadsIt_Then_ItIsRefused(self):
        # Arrange / Act — a reword of an entry, which the Edit path refuses where the entry is in a
        # released section and allows where it is not. Neither is decidable from the command.
        code, _ = self.verdict(CLOSED_VERSION_GUARD,
                               "sed -i '' -e s/shipped/landed/ " + CHANGELOG_REL)

        # Assert
        self.assertEqual(code, 2)

    def test_Given_AShellWriteOntoAnotherTrackedFile_When_TheClosedVersionGuardReadsIt_Then_ItRuns(self):
        # Arrange / Act
        code, _ = self.verdict(CLOSED_VERSION_GUARD, "sed -i '' -e s/a/b/ CONTRIBUTING.md")

        # Assert
        self.assertEqual(code, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
