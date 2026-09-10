#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/merge_branch_held_by_worktree.py.

The guard is fail-closed by design: a worktree list it could not read is refused rather than taken
for an empty one. One input class walked past that branch. A listing holding a byte UTF-8 cannot
decode raised out of the reading instead of returning None, and a PreToolUse hook that raises
exits 1, which the harness does not treat as a refusal — so the merge ran, in the case the
fail-closed branch exists for.

Two cases hold what such a listing does to the verdict, one on each side of it: a branch a worktree
holds is still named as held, and a branch no worktree holds is still let through, so a reading that
survives the byte by refusing whatever it is asked is not what makes them pass. The third holds the
fail-closed branch itself, which those two ride on and neither would miss.

Run: python3 scripts/hooks/test_merge_branch_held_by_worktree.py
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
HOOK = REPO_ROOT / ".claude/hooks/refuse/merge_branch_held_by_worktree.py"

REFUSED = 2
ALLOWED = 0

MERGE = "gh pr merge 7 --squash --delete-branch"

# The `gh api` read `branch_of` takes, answered without a network.
STUB_GH = '#!/bin/sh\nprintf "%s\\n" "$VELVET_MERGE_HEAD_REF"\n'

# A byte no UTF-8 sequence starts with, so a strict decode of a listing carrying it stops there.
STRAY = b"\xff"


def git(cwd, *args):
    """A failed arrangement is not a verdict, so one must not reach the assertion as an answer."""
    return subprocess.run(["git", "-C", str(cwd), *args], check=True, capture_output=True,
                          timeout=60)


class HeldBranchTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-merge-held-"))
        self.repo = self.root / "repo"
        subprocess.run(["git", "init", "-q", "-b", "main", str(self.repo)], check=True, timeout=60)
        (self.repo / "a").write_text("x", encoding="utf-8")
        git(self.repo, "add", "a")
        git(self.repo, "-c", "user.email=t@velvet", "-c", "user.name=t", "commit", "-qm", "initial")
        git(self.repo, "worktree", "add", "-q", "-b", "feature", str(self.root / "held"))
        git(self.repo, "worktree", "add", "-q", "-b", "sibling", str(self.root / "bytes"))

        # git records a linked worktree's path as bytes and prints it back, so the stray one reaches
        # the listing through the record rather than through a name on disk.
        recorded = self.repo / ".git" / "worktrees" / "bytes" / "gitdir"
        recorded.write_bytes(recorded.read_bytes().replace(b"/bytes/", b"/by" + STRAY + b"tes/"))

        self.stub = self.root / "bin"
        self.stub.mkdir()
        (self.stub / "gh").write_text(STUB_GH, encoding="utf-8")
        (self.stub / "gh").chmod(0o755)

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def listing_is_undecodable(self):
        """Whether the arrangement reached the guard's own input, read as bytes rather than through
        the decode under test."""
        raw = subprocess.run(["git", "-C", str(self.repo), "worktree", "list", "--porcelain"],
                             capture_output=True, timeout=60).stdout
        try:
            raw.decode("utf-8")
        except UnicodeDecodeError:
            return True
        return False

    def ask(self, head_ref, path=None):
        """What the guard does with `MERGE`, against a `gh` naming `head_ref` as the head."""
        environment = dict(os.environ)
        inherited = environment.get("PATH", "")
        environment["PATH"] = (str(self.stub) + os.pathsep + inherited) if path is None else path
        environment["VELVET_MERGE_HEAD_REF"] = head_ref
        event = {"tool_name": "Bash", "cwd": str(self.repo), "tool_input": {"command": MERGE}}
        return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(event),
                              capture_output=True, text=True, env=environment, timeout=120)

    def test_Given_AListingHoldingAStrayByte_When_AHeldBranchIsMerged_Then_ItIsNamedAsHeld(self):
        # Arrange — the stray byte goes into a second worktree's recorded path, so the branch the
        # guard has to name is an ordinary one on an ordinary path. Putting it in the branch name
        # instead would make the assertion about how a surrogate is spelled rather than about the
        # verdict. The gate rides in the comparison because a listing that decoded would leave the
        # guard nothing to fail on, and this case green having posed nothing.
        arranged = self.listing_is_undecodable()

        # Act
        result = self.ask("feature")

        # Assert
        self.assertEqual((arranged, result.returncode, "  feature  held by " in result.stderr),
                         (True, REFUSED, True))

    def test_Given_AListingHoldingAStrayByte_When_AnUnheldBranchIsMerged_Then_TheMergeIsLetThrough(self):
        # Arrange — the other side of the same listing. A reading that named every branch as held
        # would satisfy the case above, whose branch is held either way, so what the listing decides
        # is asked here as well as what it survives.
        arranged = self.listing_is_undecodable()

        # Act
        result = self.ask("nothing-holds-this")

        # Assert
        self.assertEqual((arranged, result.returncode), (True, ALLOWED))

    # GREEN_ON_BASE(characterization): the fail-closed branch is what the two cases above are read
    # against, and neither of them reaches it — both arrange a git that answers.
    def test_Given_AGitThatCannotRun_When_AMergeIsAsked_Then_TheUnreadListIsRefused(self):
        # Arrange — PATH holding no git at all, which is the reading failure the guard's None was
        # written for and the one it names in what it prints.
        empty = self.root / "empty"
        empty.mkdir()

        # Act
        result = self.ask("feature", path=str(empty))

        # Assert
        self.assertEqual((result.returncode, "git did not list the worktrees" in result.stderr),
                         (REFUSED, True))


if __name__ == "__main__":
    unittest.main()
