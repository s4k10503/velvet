#!/usr/bin/env python3
"""The floor under the guard suites: every hook is named by a case, and named in its code.

A triage of this directory counted a hook as covered because its name sat in another suite's
comment. A comment poses a guard nothing, and the issue that asked for these suites rules that
reading out by name, so the corpus here is read with its prose taken out and what remains is text a
case could be executing.

Named rather than executed is still the weaker half of the two: a suite that spells a hook and asks
it one question satisfies this. What it catches is the other end -- a guard no suite mentions at
all, which is where the three hooks this file was written beside sat.

A hook whose decision lives in a module of its own names that module's suite in `DECISION_TESTS`
instead, since a suite over the module has no reason to spell the hook that calls it.

Run: python3 scripts/hooks/test_hook_coverage.py
"""

import ast
import io
import re
import subprocess
import tokenize
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOKS = REPO_ROOT / ".claude/hooks"

# The directories the settings register a hook from. `lib/` is left out because a library is reached
# through the guard that imports it, where each of these is invoked directly.
HOOK_DIRECTORIES = ("refuse", "report", "stop")

# A Python module run as a suite, or a C# fixture. Both name a hook inside a string literal, which
# is why the reading below drops prose and keeps string literals.
CORPUS = re.compile(r"(^|/)(test_[^/]*\.py|[^/]*Tests\.cs)$")

DECLARATION = "DECISION_TESTS"

# Raised with the tree, the way the guard fixtures' floors are: a scan that found nothing leaves
# nothing uncovered and would otherwise satisfy every reading below.
HOOK_FLOOR = 24
CORPUS_FLOOR = 350
DECLARATION_FLOOR = 1

COMMENTS = re.compile(r"//[^\n]*|/\*.*?\*/", re.S)


def code_of(relative, text):
    """`text` with its prose removed: comments, and a docstring wherever one opens a Python body.

    Python goes through `tokenize`, which tells a `#` inside a string from one that opens a comment.
    C# does not: `//` and `/* */` are cut wherever they fall, inside a string literal included, so a
    literal can lose its tail. That is the safe direction -- a name cut out of code reports a hook
    uncovered, which fails loudly here, where a name kept out of a comment reports one covered by
    prose, which is the reading this file exists to refuse.
    """
    if not relative.endswith(".py"):
        return COMMENTS.sub("", text)
    kept, opening = [], True
    for token in tokenize.generate_tokens(io.StringIO(text).readline):
        if token.type == tokenize.COMMENT:
            continue
        if token.type == tokenize.STRING and opening:
            continue
        kept.append(token.string)
        opening = token.type in (tokenize.NEWLINE, tokenize.NL, tokenize.INDENT, tokenize.DEDENT)
    return "\n".join(kept)


def hooks():
    """Every hook under the directories the harness registers from, ordered by directory."""
    return sorted((path for directory in HOOK_DIRECTORIES
                   for path in (HOOKS / directory).glob("*.py")),
                  key=lambda path: (path.parent.name, path.name))


def named(hook):
    """`directory/stem`, which is how a hook is reported here."""
    return f"{hook.parent.name}/{hook.stem}"


def corpus():
    """The test sources the repository tracks, as git lists them.

    Tracked rather than walked, for the reason `base_red_check.canary_fixtures` gives about the same
    choice: a walk from the root reaches `Library/PackageCache`, and a file nobody committed poses
    nothing in CI whatever it holds.
    """
    listed = subprocess.run(["git", "-C", str(REPO_ROOT), "ls-files"], capture_output=True,
                            text=True, check=True, timeout=60).stdout.splitlines()
    return [relative for relative in listed if CORPUS.search(relative)]


def declaration_of(hook):
    """What a hook assigns to `DECISION_TESTS`, or None where it assigns nothing."""
    for node in ast.parse(hook.read_text(encoding="utf-8")).body:
        if isinstance(node, ast.Assign) and any(isinstance(target, ast.Name)
                                                and target.id == DECLARATION
                                                for target in node.targets):
            try:
                return ast.literal_eval(node.value)
            except ValueError:
                return ()
    return None


def declares(hook):
    """Whether a hook names its own decision tests in a form this can resolve."""
    declared = declaration_of(hook)
    return isinstance(declared, (list, tuple)) and bool(declared)


# The name each sample below carries, and one sample per form the reading treats separately. It has
# to drop the prose rows and keep the code rows; a stripper that emptied its input satisfies the
# first of those alone.
#
# A name no hook has, because the code rows below are code of this very file: spelled as a real
# hook, they name it, and the scan above then reports that hook covered by the samples that were
# meant to exercise the reading. The gate in `HookCoverageTests` is what fails if this file ever
# names one, by whatever route.
SAMPLE = "guard_named_only_here"

PROSE = (
    ("test_sample.py", "# refuse/guard_named_only_here.py\n", "a Python comment"),
    ("test_sample.py", '"""refuse/guard_named_only_here.py"""\n', "a Python docstring"),
    ("SampleTests.cs", "// refuse/guard_named_only_here.py\n", "a C# line comment"),
    ("SampleTests.cs", "/* refuse/guard_named_only_here.py */\n", "a C# block comment"),
)

CODE = (
    ("test_sample.py", 'HOOK = ROOT / "refuse/guard_named_only_here.py"\n', "a Python string"),
    ("SampleTests.cs", 'const string Hook = "refuse/guard_named_only_here.py";\n', "a C# string"),
)


class SourceReadingTests(unittest.TestCase):
    """What `code_of` keeps of a source, form by form."""

    # GREEN_ON_BASE(construction): the reading and its samples are both this file's own.
    # The base answers over the same text, so what shows the case can fail is a perturbation:
    # delete the `tokenize.COMMENT` arm in `code_of` and this reddens.
    def test_Given_AHookNameInEachForm_When_TheSourceIsReadAsCode_Then_OnlyTheProseIsDropped(self):
        # Arrange / Act
        carried = [form for relative, text, form in PROSE + CODE
                   if SAMPLE in code_of(relative, text)]

        # Assert — the code rows ride in the comparison because a reading that returned nothing at
        # all drops every prose row too, and would pass over the prose rows alone.
        self.assertEqual(carried, [form for _, _, form in CODE])


class HookCoverageTests(unittest.TestCase):
    """Every hook against the repository's own test sources."""

    def setUp(self):
        self.hooks = hooks()
        self.corpus = corpus()
        self.raw = {relative: (REPO_ROOT / relative).read_text(encoding="utf-8",
                                                               errors="surrogateescape")
                    for relative in self.corpus}
        self.code = {relative: code_of(relative, text) for relative, text in self.raw.items()}

    def test_Given_EveryHookUnderTheHarness_When_TheTestSourcesAreReadAsCode_Then_EachIsNamedByOne(self):
        # Arrange — four gates ride in the comparison below, because each leaves the uncovered
        # list empty for a reason that is not coverage: no hook found, no source found, a corpus
        # handed on with its prose still in it, or a hook named by this file, which is the floor
        # counting itself as the coverage it is there to measure.
        own = str(Path(__file__).resolve().relative_to(REPO_ROOT))
        own_code = code_of(own, Path(__file__).read_text(encoding="utf-8"))
        gates = (len(self.hooks) >= HOOK_FLOOR, len(self.corpus) >= CORPUS_FLOOR,
                 any(hook.stem in self.raw[relative] and hook.stem not in self.code[relative]
                     for relative in self.corpus for hook in self.hooks),
                 all(hook.stem not in own_code for hook in self.hooks))

        # Act
        uncovered = [named(hook) for hook in self.hooks
                     if not declares(hook)
                     and not any(hook.stem in text for text in self.code.values())]

        # Assert
        self.assertEqual((*gates, uncovered), (True, True, True, True, []))

    def test_Given_AHookNamingItsOwnDecisionTests_When_TheNamesAreResolved_Then_TheScanReadsEach(self):
        # Arrange — a declaration is the way out of the case above, so one naming a file the scan
        # never opens is an exemption backed by nothing.
        declared = [(hook, declaration_of(hook)) for hook in self.hooks
                    if declaration_of(hook) is not None]
        readable = set(self.corpus)

        # Act
        stray = [f"{named(hook)} names {names!r}, which is not a list of paths"
                 for hook, names in declared
                 if not isinstance(names, (list, tuple)) or not names]
        stray += [f"{named(hook)} names {name}, which the scan does not read"
                  for hook, names in declared if isinstance(names, (list, tuple))
                  for name in names if name not in readable]

        # Assert — the count rides in the comparison because a repository where nothing declares
        # anything leaves nothing to resolve and reports the same empty list.
        self.assertEqual((len(declared) >= DECLARATION_FLOOR, stray), (True, []))


if __name__ == "__main__":
    unittest.main()
