#!/usr/bin/env python3
"""A deferral suppresses a guard only for the session that wrote it.

One file holds every deferral both Stop guards read, and an entry is a line nothing signs. So a
process with no view of what a key is about can append as readily as the one holding it: measured, a
subagent blocked by a guard naming six pull requests it neither owned nor could merge deferred all
six. That was the only route forward it had, and the effect was that the guard holding the merge
queue went quiet for every entry in it.

Who wrote a line is readable, and not from what a hook is handed: a PreToolUse payload carries cwd,
tool_name and tool_input and nothing else, while the environment of a tool call carries
CLAUDE_CODE_SESSION_ID. The line records that, `deferred` honours only its own, and `disowned`
reports the rest.

A reader with no such variable cannot attribute anything, and that is the one state where this falls
back to what it did before -- reporting nothing rather than refusing everything, since a guard that
suppressed nothing would be a guard nobody could hold anything past.

Run: python3 scripts/hooks/test_deferral_writer.py
"""

import importlib.util
import os
import shutil
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
MODULE = REPO_ROOT / ".claude" / "hooks" / "lib" / "deferrals.py"

_spec = importlib.util.spec_from_file_location("deferrals", MODULE)
deferrals = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(deferrals)

# Reached through getattr so a tree without it answers "nothing reported" rather than raising: a
# case that raises carries no reading either way, and what this one has to say is that the base
# reports nothing where this reports the line.
def disowned(key, now=None):
    return getattr(deferrals, "disowned", lambda *a, **k: [])(key, now)

NOW = 1_000_000
MINE = "mine-0001"


class WhoseDeferral(unittest.TestCase):
    def setUp(self):
        root = Path(tempfile.mkdtemp(prefix="deferral-"))
        self.addCleanup(shutil.rmtree, root, ignore_errors=True)
        self.file = root / "deferrals"
        self.addCleanup(setattr, deferrals, "DEFERRALS", deferrals.DEFERRALS)
        deferrals.DEFERRALS = self.file
        before = os.environ.get("CLAUDE_CODE_SESSION_ID")
        self.addCleanup(self.restore, before)
        os.environ["CLAUDE_CODE_SESSION_ID"] = MINE

    def restore(self, before):
        if before is None:
            os.environ.pop("CLAUDE_CODE_SESSION_ID", None)
        else:
            os.environ["CLAUDE_CODE_SESSION_ID"] = before

    def wrote(self, line):
        self.file.write_text(line + "\n", encoding="utf-8")

    def test_Given_ALineThisSessionWrote_When_TheKeyIsRead_Then_ItSuppresses(self):
        # Arrange -- the ordinary case, and what must keep working.
        self.wrote("377 waiting on review {} {}".format(NOW, MINE))

        # Act / Assert
        self.assertEqual(deferrals.deferred("377", NOW + 60), ("waiting on review", 1))

    # GREEN_ON_BASE(characterization): the base honours this line and this case says it does not
    # suppress -- which the base also satisfies, because on the base `deferred` reads no session at
    # all and this line's trailing field is not an epoch, so it answers None for the wrong reason.
    # The case beside it, which reports the line, is what separates the two.
    def test_Given_ALineAnotherSessionWrote_When_TheKeyIsRead_Then_ItSuppressesNothing(self):
        # Arrange -- the shape that silenced six pull requests at once.
        self.wrote("377 waiting on review {} other-0002".format(NOW))

        # Act / Assert
        self.assertIsNone(deferrals.deferred("377", NOW + 60))

    def test_Given_ALineAnotherSessionWrote_When_TheKeyIsRead_Then_ItIsReported(self):
        # Arrange -- not suppressing is half of it; the reader has to be told it exists.
        self.wrote("377 waiting on review {} other-0002".format(NOW))

        # Act / Assert
        self.assertEqual(disowned("377", NOW + 60),
                         [("waiting on review", "other-0002")])

    def test_Given_AnotherSessionWroteLater_When_TheKeyIsRead_Then_ThisSessionsLineStillSuppresses(self):
        # Arrange -- both sessions hold the same pull request, and the other one wrote second. One
        # file, one key, and appending is the only thing a writer does.
        self.file.write_text(f"377 waiting on review {NOW} {MINE}\n"
                             f"377 waiting on something else {NOW} other-0002\n",
                             encoding="utf-8")

        # Act / Assert
        self.assertEqual(deferrals.deferred("377", NOW + 60), ("waiting on review", 1))

    def test_Given_AnotherSessionWroteAMalformedLineLater_When_TheKeyIsRead_Then_ItIsNotReportedAsThisOnes(self):
        # Arrange -- what `unusable` says goes to the session that wrote a deferral and saw nothing
        # happen, so a stamp somebody else fumbled is not an answer to that.
        self.file.write_text(f"377 waiting on review {NOW} {MINE}\n"
                             "377 waiting on review tomorrow other-0002\n",
                             encoding="utf-8")

        # Act / Assert
        self.assertIsNone(deferrals.unusable("377", NOW + 60))

    def test_Given_ALineSigningNothing_When_TheKeyIsRead_Then_ItSuppressesNothing(self):
        # Arrange -- every line written before this was recorded. Grandfathering them would keep the
        # hole open for exactly as long as the file lives.
        self.wrote("377 waiting on review {}".format(NOW))

        # Act / Assert
        self.assertIsNone(deferrals.deferred("377", NOW + 60))

    def test_Given_AReaderThatCannotAttribute_When_TheKeyIsRead_Then_ItSuppressesAsBefore(self):
        # Arrange -- the one state this falls back on: with nothing to compare against, refusing
        # every line would leave nothing holdable past a guard at all.
        self.wrote("377 waiting on review {} other-0002".format(NOW))
        os.environ.pop("CLAUDE_CODE_SESSION_ID", None)

        # Act / Assert
        self.assertEqual(deferrals.deferred("377", NOW + 60), ("waiting on review", 1))

    # GREEN_ON_BASE(characterization): the expiry is not what this changes, and a control over it
    # is green on both sides -- one that reddened would mean signing a line had moved when it dies.
    def test_Given_AnExpiredLineOfThisSession_When_TheKeyIsRead_Then_TheExpiryStillDecides(self):
        # Arrange -- the control on the other side: signing a line does not make it immortal.
        self.wrote("377 waiting on review {} {}".format(NOW, MINE))

        # Act / Assert
        self.assertIsNone(deferrals.deferred("377", NOW + deferrals.TTL))


class SignedLineIsNotUnusable(unittest.TestCase):
    """`deferred` and `unusable` read one line, so they read it the same way.

    Updated in one and not the other, a signed line was honoured and reported unusable in the same
    breath -- the guard printed it under "Held on purpose" and under "Deferrals that were ignored".
    """

    def setUp(self):
        root = Path(tempfile.mkdtemp(prefix="deferral-"))
        self.addCleanup(shutil.rmtree, root, ignore_errors=True)
        self.file = root / "deferrals"
        self.addCleanup(setattr, deferrals, "DEFERRALS", deferrals.DEFERRALS)
        deferrals.DEFERRALS = self.file
        before = os.environ.get("CLAUDE_CODE_SESSION_ID")
        self.addCleanup(
            lambda: os.environ.__setitem__("CLAUDE_CODE_SESSION_ID", before) if before
            else os.environ.pop("CLAUDE_CODE_SESSION_ID", None))
        os.environ["CLAUDE_CODE_SESSION_ID"] = MINE

    def test_Given_ALineThisSessionSigned_When_ItIsReadForRejection_Then_NothingIsWrongWithIt(self):
        # Arrange
        self.file.write_text("377 waiting on review {} {}\n".format(NOW, MINE), encoding="utf-8")

        # Act / Assert
        self.assertIsNone(deferrals.unusable("377", NOW + 60))

    # GREEN_ON_BASE(characterization): the control, and the base names a malformed line too -- that
    # reading is what this widens rather than replaces. One that reddened would mean the widening had
    # taken the rejection with it.
    def test_Given_ALineWhoseStampIsMissing_When_ItIsReadForRejection_Then_ItIsStillNamed(self):
        # Arrange -- the control: the reading that catches a malformed line has to keep catching it.
        self.file.write_text("377 waiting on review later {}\n".format(MINE), encoding="utf-8")

        # Act / Assert
        self.assertIsNotNone(deferrals.unusable("377", NOW + 60))


if __name__ == "__main__":
    unittest.main(verbosity=2)
