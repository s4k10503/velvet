#!/usr/bin/env python3
"""Unit tests for scripts/pr/diagnostics/open_backlog.py's verdict on an assigned backlog.

The harness that poses every guard an unreadable state holds this one to what it says when gh
cannot answer. Nothing poses it an issue list: in the one mode there where gh answers anything, an
open pull request comes back and this guard hands the turn to its sibling before the backlog is
read. So the decision it exists to make -- an assigned issue holds the session, one carrying an
excluding label does not -- goes unasked.

Each of the two excluding labels is posed on its own, so a deletion from the set says which of them
stopped counting rather than that one of them did.

Run: python3 scripts/hooks/test_open_backlog.py
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
HOOK = REPO_ROOT / "scripts/pr/diagnostics/open_backlog.py"

HELD = 2
LET_GO = 0

STOP_PAYLOAD = json.dumps({"hook_event_name": "Stop", "stop_hook_active": False})

ISSUE = 4242
TITLE = "an assigned issue nothing is carrying"

# The three readings the guard takes in order, each answered without a network. The open pull request
# list is empty, since a non-empty one is the state this guard hands to its sibling and returns.
STUB_GH = """#!/bin/sh
printf '%s\\n' "$1 $2" >> "$VELVET_LOG"
case "$1 $2" in
  "pr list") exit 0 ;;
  "api user") printf '%s\\n' "$VELVET_LOGIN"; exit 0 ;;
  "issue list") printf '%s' "$VELVET_ISSUES"; exit 0 ;;
esac
exit 0
"""


def listing(*labels):
    """One assigned issue carrying `labels`, as `gh issue list --json` answers it."""
    return json.dumps([{"number": ISSUE, "title": TITLE,
                        "labels": [{"name": name} for name in labels]}])


class AssignedBacklogTests(unittest.TestCase):
    """Ending a turn against a gh that answers with the issue list each case arranges."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-open-backlog-"))
        self.binaries = self.root / "bin"
        self.binaries.mkdir()
        (self.binaries / "gh").write_text(STUB_GH, encoding="utf-8")
        (self.binaries / "gh").chmod(0o755)
        self.log = self.root / "asked"
        # A HOME of its own, because the deferrals file lives there and a developer's own would
        # otherwise decide whether this backlog is held.
        self.home = self.root / "home"
        self.home.mkdir()

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def ask(self, issues):
        """What the guard does at the end of a turn, given that issue listing."""
        environment = dict(os.environ)
        environment["PATH"] = str(self.binaries) + os.pathsep + environment.get("PATH", "")
        environment["HOME"] = str(self.home)
        environment.update(VELVET_LOG=str(self.log), VELVET_LOGIN="someone",
                           VELVET_ISSUES=issues)
        return subprocess.run([sys.executable, "-B", str(HOOK)], input=STOP_PAYLOAD,
                              capture_output=True, text=True, env=environment, timeout=120)

    def read_the_backlog(self):
        """Whether the guard got as far as asking for the issue list."""
        return "issue list" in (self.log.read_text() if self.log.exists() else "")

    # GREEN_ON_BASE(characterization): the base holds an unlabelled issue too.
    # It is the verdict both exclusions below are read against.
    def test_Given_AnAssignedIssueCarryingNoLabel_When_TheTurnEnds_Then_TheSessionIsHeld(self):
        # Arrange / Act
        result = self.ask(listing())

        # Assert — the issue rides in the comparison because this guard blocks for reasons that
        # are not a backlog at all, an unread pull request list among them, and a block naming no
        # issue is not this backlog being reported.
        self.assertEqual((result.returncode, f"#{ISSUE} {TITLE}" in result.stderr), (HELD, True))

    # GREEN_ON_BASE(characterization): the base excludes `blocked` too.
    # It is the half the case below cannot speak for.
    def test_Given_TheOnlyAssignedIssueLabelledBlocked_When_TheTurnEnds_Then_TheSessionIsLetGo(self):
        # Arrange / Act
        result = self.ask(listing("blocked"))

        # Assert — the reading rides in the comparison because a guard that found no gh at all, or
        # stopped at the pull request list, lets the turn end without having read any of this.
        self.assertEqual((result.returncode, self.read_the_backlog()), (LET_GO, True))

    # GREEN_ON_BASE(characterization): the base excludes `needs-decision` too.
    # It is the half the case above cannot speak for.
    def test_Given_TheOnlyAssignedIssueLabelledNeedsDecision_When_TheTurnEnds_Then_TheSessionIsLetGo(self):
        # Arrange / Act
        result = self.ask(listing("needs-decision"))

        # Assert — the reading rides along for the reason the case above states.
        self.assertEqual((result.returncode, self.read_the_backlog()), (LET_GO, True))


if __name__ == "__main__":
    unittest.main()
