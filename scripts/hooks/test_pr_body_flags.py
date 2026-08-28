#!/usr/bin/env python3
"""Holds `pr_body`'s record of which gh options take a value against gh's own option table.

The parse in `.claude/hooks/lib/pr_body.py` has to know which options carry a value in order to tell
a body flag from another option's value — without it, `gh pr create --title -F x` reads a body file
of `x`, and the file asked about is not the one gh will post. That record is a mirror of somebody
else's table, and an earlier revision of the guard rejected building one for exactly that reason.
This is what makes it not drift.

Both directions end with the posted description unexamined, and only one of them reaches that by
leaving a body unfound. Measured on `--assignee`, for which the table read below declares a value:

- deleted from the mirror, `gh pr create --title x --assignee --body-file b.md` still resolves a
  body file of `b.md`. The guard opens that file and answers about it — either way round — while
  it is `--body-file` that stands where `--assignee`'s value goes;
- added to the mirror where gh prints a boolean, `gh pr create --title x --draft --body-file b.md`
  resolves no body file at all: the parse spends `--body-file` as `--draft`'s value, and a guard
  that found no body exits 0, which is what it exits having read one and been satisfied.

gh is asked rather than snapshotted, so the answer is the one on the machine the guard will run on.
`--help` reaches neither the network nor a credential, so this needs no token.

Run: python3 scripts/hooks/test_pr_body_flags.py
"""

import importlib.util
import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parents[2]

# `-x, ` is optional, the long name always present, and a metavar between the name and the two
# spaces before the description is what marks an option as taking a value.
OPTION = re.compile(r"^\s{2,}(?:-(\w), )?--([a-z][\w-]*)(\s+\S+)?(\s\s|$)")
BLOCK = re.compile(r"^(?:INHERITED )?FLAGS\n(.*?)\n\n", re.S | re.M)

# The subcommands the guard claims. `new` is create under another name and prints create's help, so
# asking it a third time would compare create's table with itself.
SUBCOMMANDS = ("create", "edit")


def load_pr_body():
    """Imports .claude/hooks/lib/pr_body.py by path, since .claude holds no packages."""
    path = REPO_ROOT / ".claude/hooks/lib/pr_body.py"
    spec = importlib.util.spec_from_file_location("pr_body_lib", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


pr_body = load_pr_body()


def option_table(subcommand):
    """(value-taking, boolean) flag names gh prints for `gh pr <subcommand>`, both spellings of each.

    Raised rather than returned empty: a table that parsed to nothing agrees with any mirror at all
    in the covering direction, which is the reading this exists to refuse.
    """
    if shutil.which("gh") is None:
        raise RuntimeError(
            "gh is not on PATH, so the option table these guards parse against cannot be read. "
            "The guards gate gh commands, so a checkout without gh cannot show that their parse "
            "is still right.")
    printed = subprocess.run(["gh", "pr", subcommand, "--help"],
                             capture_output=True, text=True, check=True).stdout
    blocks = BLOCK.findall(printed)
    if not blocks:
        raise RuntimeError(f"gh pr {subcommand} --help printed no FLAGS block this can read.")
    value_taking, boolean = set(), set()
    for block in blocks:
        for line in block.splitlines():
            found = OPTION.match(line)
            if not found:
                continue
            names = {"--" + found.group(2)}
            if found.group(1):
                names.add("-" + found.group(1))
            (value_taking if found.group(3) else boolean).update(names)
    return value_taking, boolean


def across_subcommands(index):
    found = set()
    for subcommand in SUBCOMMANDS:
        found |= option_table(subcommand)[index]
    return found


class BodyPathReadingTests(unittest.TestCase):
    """Which spellings of a path the body reader resolves.

    `~` is a selector the shell rewrites, like `$VAR` — and unlike `$VAR` it names one path this can
    resolve, so the file is read rather than the reading refused. Measured before: `--body-file ~/x.md`
    over a file that exists and answers was refused as "does not exist", with a next action about a
    write that did not run, while the absolute spelling of the same file was read.
    """

    def setUp(self):
        self.home = Path(tempfile.mkdtemp(prefix="body-home-"))
        self.addCleanup(shutil.rmtree, self.home, ignore_errors=True)

    def read(self, spelling):
        with mock.patch.dict(os.environ, {"HOME": str(self.home)}):
            return pr_body.read_body_file(spelling, str(self.home), after_a_move=False)

    def test_Given_ATildeSpelledBody_When_Read_Then_ItIsTheFileThatIsThere(self):
        # Arrange
        (self.home / "body.md").write_text("a body that answers")

        # Act
        text, obstruction = self.read("~/body.md")

        # Assert
        self.assertEqual((text, obstruction), ("a body that answers", None))

    def test_Given_ATildeSpelledBodyThatIsNotThere_When_Read_Then_ItIsStillMissing(self):
        # Arrange — the control: expanding the selector must not make an absent file readable.
        # Act
        _, obstruction = self.read("~/nothing-here.md")

        # Assert
        self.assertEqual(obstruction, pr_body.MISSING)

    def test_Given_AVariableSpelledBody_When_Read_Then_ItIsStillUnexpanded(self):
        # Arrange — the other selector, which names no path this can resolve and is refused as before.
        # Act
        _, obstruction = self.read("$BODY")

        # Assert
        self.assertEqual(obstruction, pr_body.UNEXPANDED_PATH)


class ValueFlagMirrorTests(unittest.TestCase):
    def test_Given_ghsOwnOptionTable_When_TheValueTakingOptionsAreRead_Then_TheMirrorSpellsThoseExactly(self):
        # Arrange
        printed = across_subcommands(0)

        # Act
        disagreement = (sorted(pr_body.VALUE_FLAGS - printed), sorted(printed - pr_body.VALUE_FLAGS))

        # Assert
        self.assertEqual(disagreement, ([], []),
                         "VALUE_FLAGS and gh disagree: (mirrored but not gh's, gh's but unmirrored)")

    def test_Given_ghsOwnOptionTable_When_TheBooleanShorthandsAreRead_Then_TheClusterParseKnowsEachOne(self):
        # Arrange — a cluster is read one letter at a time, so a boolean shorthand the parse does not
        # know stops the walk and takes any body flag clustered behind it with it: `-wF b.md`.
        printed = {flag for flag in across_subcommands(1) if not flag.startswith("--")}

        # Act — the emptiness of the reading rides along, since an empty set is covered by anything.
        uncovered = (sorted(printed - pr_body.SHORT_BOOLEAN_FLAGS), bool(printed))

        # Assert
        self.assertEqual(uncovered, ([], True),
                         "gh prints a boolean shorthand SHORT_BOOLEAN_FLAGS does not carry")


if __name__ == "__main__":
    unittest.main(verbosity=2)
