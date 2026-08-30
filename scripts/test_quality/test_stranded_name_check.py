#!/usr/bin/env python3
"""Unit tests for stranded_name_check.py, plus its reading of the commit it was written for.

The cases are built as real commits in a temporary repository, because what the guard reads is a
diff and a tree at a revision -- a fixture that hands it text instead would be exercising the
regular expressions and nothing else. The last case holds it against this repository's own history:
the commit that stranded the names the check exists for, and one whose firing is a false positive
that has to stay visible rather than be excluded by a rule nobody measured.

Run: python3 scripts/test_quality/test_stranded_name_check.py
"""

import contextlib
import importlib.util
import io
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


def load_module(name):
    """Imports a sibling script by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        name, Path(__file__).resolve().with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


check = load_module("stranded_name_check")

BEFORE = """namespace Velvet
{
    internal static class Probe
    {
        internal static int RebaseSlots(int at) => at;

        internal static int Read(int at) => RebaseSlots(at);
    }
}
"""


class Repository:
    """Two commits in a scratch repository: a tree, then a change to it."""

    def __init__(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-stranded-"))
        self.source = self.root / "Probe.cs"
        self.git("init", "-q", "-b", "main")
        self.git("config", "user.email", "probe@example.com")
        self.git("config", "user.name", "Probe")
        self.source.write_text(BEFORE, encoding="utf-8")
        self.commit("before")

    def git(self, *arguments):
        return subprocess.run(["git", "-C", str(self.root)] + list(arguments),
                              capture_output=True, text=True, check=True).stdout

    def commit(self, message):
        self.git("add", "-A")
        self.git("commit", "-q", "-m", message)
        return self.git("rev-parse", "HEAD").strip()

    def changed_to(self, text):
        self.source.write_text(text, encoding="utf-8")
        return self.commit("after")

    def verdict(self):
        """(exit code, everything it said) for one reading of the second commit."""
        captured = io.StringIO()
        with contextlib.redirect_stderr(captured), contextlib.redirect_stdout(captured):
            code = check.main(["--base", "HEAD~", "--head", "HEAD"])
        return code, captured.getvalue()

    def close(self):
        shutil.rmtree(self.root, ignore_errors=True)


@contextlib.contextmanager
def repository():
    made = Repository()
    original = Path.cwd()
    import os
    os.chdir(made.root)
    try:
        yield made
    finally:
        os.chdir(original)
        made.close()


class StrandedTests(unittest.TestCase):
    def test_Given_ARemovalWhoseNameACommentStillCarries_When_TheChangeIsRead_Then_ItIsRefused(self):
        # Arrange
        with repository() as tree:
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
        // The caller rebases first (see RebaseSlots), so the index is already absolute here.
        internal static int Read(int at) => at;
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 1)

    def test_Given_ARemovalWhoseNameACommentStillCarries_When_TheChangeIsRead_Then_TheNameIsSaid(self):
        # Arrange
        with repository() as tree:
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
        // The caller rebases first (see RebaseSlots), so the index is already absolute here.
        internal static int Read(int at) => at;
    }
}
""")

            # Act
            _, said = tree.verdict()

            # Assert
            self.assertIn("RebaseSlots", said)

    def test_Given_ARemovalWhoseNameABlockCommentCarries_When_TheChangeIsRead_Then_ItIsRefused(self):
        # Arrange -- the same sentence in the other comment form, which a line-anchored reader misses.
        with repository() as tree:
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
        /* The caller rebases first (RebaseSlots), so the index is absolute. */
        internal static int Read(int at) => at;
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 1)

    def test_Given_ARemovalWhoseNameNothingElseSpells_When_TheChangeIsRead_Then_ItIsNotRefused(self):
        # Arrange -- a clean removal, which is most of them.
        with repository() as tree:
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
        internal static int Read(int at) => at;
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)

    def test_Given_AMethodMovedRatherThanRemoved_When_TheChangeIsRead_Then_ItIsNotRefused(self):
        # Arrange -- the declaration leaves one place and is written in another, spelled differently
        # so the diff reports the removal rather than matching the line as context. The tree answers
        # for it, and that answer is the code-side reading: without it this refuses a live name.
        with repository() as tree:
            tree.changed_to("""namespace Velvet
{
    internal static class Slots
    {
        internal static int RebaseSlots(int at)
        {
            return at;
        }
    }

    internal static class Probe
    {
        // The caller rebases first (see RebaseSlots).
        internal static int Read(int at) => Slots.RebaseSlots(at);
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)

    def test_Given_ARemovedNameSurvivingOnlyInAStringLiteral_When_TheChangeIsRead_Then_ItIsNotRefused(self):
        # Arrange -- a string is content rather than a reference to the declaration.
        with repository() as tree:
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
        internal static string Name() => "RebaseSlots";
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)

    def test_Given_ADoubleSlashInsideAStringLiteral_When_TheRestOfTheLineNamesIt_Then_ItIsNotAComment(self):
        # Arrange -- the declaration goes and its one surviving call sits after a string holding `//`.
        # Truncating the line there hides the call, and the name then reads as stranded when it is not.
        with repository() as tree:
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
        internal static int Read(int at) => Url("http://x") + RebaseSlots(at);

        internal static int Url(string held) => held.Length;
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)

    def test_Given_AnEscapedApostropheLiteral_When_ACallFollowsIt_Then_ItIsStillCode(self):
        # Arrange -- the shape three files in this repository carry: a char literal holding a quote
        # beside one holding an escaped apostrophe. Read as "everything to the next apostrophe", the
        # two pair wrongly and the call after them leaves the code stream, so the comment above is
        # all that is left naming it.
        with repository() as tree:
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
        // The caller rebases first (see RebaseSlots).
        internal static bool Read(char c) => c == '"' || c == '\\'' || RebaseSlots(c) > 0 || c == 'x';
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)

    def test_Given_AnInterpolationHoleHoldingAString_When_ItCarriesTheOnlyCall_Then_ItIsStillCode(self):
        # Arrange -- read as an ordinary literal, the outer string ends at the quote inside the hole
        # and everything to the next one leaves both streams, taking the call with it.
        with repository() as tree:
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
        // The caller rebases first (see RebaseSlots).
        internal static string Read(int[] xs) => $"a {string.Join(", ", xs)} b {RebaseSlots(1)}";
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)


    def test_Given_ARegionLabelSpellingTheRemovedName_When_TheChangeIsRead_Then_ItIsStillFound(self):
        # Arrange -- a directive line declares nothing. Routed to the code stream, one #region label
        # spelling the name answers for it everywhere and the strand below goes unreported.
        with repository() as tree:
            (tree.root / "Other.cs").write_text("""namespace Velvet
{
    internal static class Other
    {
        #region RebaseSlots and friends
        internal static int Keep() => 1;
        #endregion
    }
}
""", encoding="utf-8")
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
        // The caller rebases first (see RebaseSlots).
        internal static int Read(int at) => at;
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 1)

    def test_Given_AnInterpolatedStringHoldingTheOnlyCall_When_TheChangeIsRead_Then_ItIsNotRefused(self):
        # Arrange -- the holes in an interpolated string are code, and separating them needs a
        # brace-matching pass this does not do. Its text goes to the code stream instead. Dropped
        # like an ordinary string, the only call goes with it and the comment below reads as a
        # strand.
        with repository() as tree:
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
        // The caller rebases first (see RebaseSlots).
        internal static string Read(int at) => $"slot {RebaseSlots(at)}";
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)

    def test_Given_AVerbatimStringEndingInABackslash_When_TheCallFollowsIt_Then_ItIsStillCode(self):
        # Arrange -- a verbatim string takes no escapes, so its closing quote is the one after the
        # backslash. Read as an ordinary literal that quote is escaped, the string runs on past the
        # call, and the comment above is all that is left naming it.
        with repository() as tree:
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
        // The caller rebases first (see RebaseSlots).
        internal static int Read(int at) => Url(@"a directory\\") + RebaseSlots(at);

        internal static int Url(string held) => held.Length;
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)


    def test_Given_ADirectiveLineCarryingAComment_When_TheCommentNamesIt_Then_ItIsStillRead(self):
        # Arrange -- a #pragma is dropped, but the comment after it on the same line is a comment.
        with repository() as tree:
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
#pragma warning disable CS8524 // the caller rebases first (see RebaseSlots)
        internal static int Read(int at) => at;
#pragma warning restore CS8524
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 1)

    def test_Given_ASourceWhoseNameHoldsASpace_When_ItCarriesTheStrand_Then_ItIsStillRead(self):
        # Arrange -- git grep -l reports one path per line, and splitting that on whitespace makes
        # two paths of this one, neither of which any revision holds.
        with repository() as tree:
            (tree.root / "Route Link.cs").write_text("""namespace Velvet
{
    internal static class Other
    {
        // The caller rebases first (see RebaseSlots).
        internal static int Keep() => 1;
    }
}
""", encoding="utf-8")
            tree.changed_to("""namespace Velvet
{
    internal static class Probe
    {
        internal static int Read(int at) => at;
    }
}
""")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 1)


class RepositoryHistoryTests(unittest.TestCase):
    """Held against real commits, since a guard that fires only on invented text is untested."""

    def read(self, revision):
        captured = io.StringIO()
        import os
        original = Path.cwd()
        os.chdir(REPO_ROOT)
        try:
            with contextlib.redirect_stderr(captured), contextlib.redirect_stdout(captured):
                code = check.main(["--base", revision + "~", "--head", revision])
        finally:
            os.chdir(original)
        return code, captured.getvalue()

    def test_Given_TheCommitThatStrandedTheNames_When_ItIsRead_Then_AllThreeAreNamed(self):
        # Arrange -- ba9d9b31 removed the logical-slot rebase methods and left three comments
        # naming them, which shipped and were found by grepping rather than by any check.
        code, said = self.read("ba9d9b31")

        # Assert
        self.assertEqual(
            (code,
             "RebasePendingSlotStartIfTargeting" in said,
             "RebaseParkedSlotsForContainerChange" in said,
             "LeadingOffset" in said),
            (1, True, True, True))

    def test_Given_ACommitNamingTheBaseTreesMethodOnPurpose_When_ItIsRead_Then_ItStillFires(self):
        # Arrange -- c8d5b151d's GREEN_ON_BASE(refactor) declaration names a method the base has and
        # this tree does not. It is one of the two firings in the 320 commits measured and the only false positive
        # among them: pinned so that excluding its shape is a decision somebody takes deliberately,
        # against a case that fails when they do.
        code, said = self.read("c8d5b151d")

        # Assert
        self.assertEqual((code, "InvokeRefCallback" in said), (1, True))


if __name__ == "__main__":
    unittest.main(verbosity=2)
