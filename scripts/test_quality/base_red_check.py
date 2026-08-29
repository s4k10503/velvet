#!/usr/bin/env python3
"""Run this branch's changed test cases against the merge base and report every one that passes there.

A test is written to separate a behaviour from its absence. The only reading that shows it does is the
one taken before the change it pins: green on the branch and green on the base together mean the case
would have passed whatever the branch did to production code. Nothing in this repository asked that
question, so it stayed a matter of attentiveness. Three of the four branches this was first run over
had a case that passed on the base and had been reported as pinning something.

What runs is the branch's test file over the base's production code: the base commit is checked out,
the branch's test-side files are copied onto it, and the changed cases are executed there. Which cases
those are is settled by each case's own code against the base's, not by which lines a diff calls
changed: over a large rewrite the aligner describes untouched text as re-added, and a comment edited
inside a case body is a changed line there. `authored` keeps both out of a gate their authors never
asked for, and `kept_out` is what stops that being silent -- otherwise a branch that changed no test
file and one that rewrote a fixture's remarks end to end leave the same empty plan.

**Success here means every changed case was measured on a base tree that demonstrably answers.** Not
that nothing failed: most of what can go wrong with a run like this ends in a reading nobody took,
and a reading nobody took must never be a pass. That is one sentence and it decides the shape of
everything below, because the verdict that carries the evidence -- a case the base could not build --
is indistinguishable, from the results file alone, from a base that built nothing, ran nothing, or
was never asked. So each of those is closed separately:

*The tree is read by cases the branch did not carry.* `canary_fixtures` picks fixtures of the base's
own for a platform and `python_canaries` picks cases of the base's own for that lane. At least one has
to exist and pass. A tree where nothing built reports every case as uncompilable,
which is not a failure here, so without that reading such a run comes back green having measured
nothing at all. The Python lane needs the same reading in its own spelling: a case that stopped before
it disagreed is not a failure here either, so a lane in which none of them answered exits green
without one. Separately and before both, a run that left no results file withdraws every platform
outright: that is the ordinary shape of a licence failure, an editor crash or a timeout, and it is the
case the canary exists for rather than a case to disarm it in.

*A case is measured under the name the runner reports it by.* A name nothing answers to yields no
reading, and no reading falls through to the same passing verdict. So the type a case is written in
is read off the code rather than off the raw line -- `class` occurs in prose and in assertion
messages -- a type leaves the stack when its body closes, and a case written in an abstract fixture
is named for each concrete heir. `test_base_red_check.py` holds every C# case in this repository to
a name the tree declares in full: every owner segment, nested as the name nests them.

*A surface that exists only on the branch is evidence, not an error.* C# reports that at compile
time. Python reports it while loading or running the case, so its spelling is accepted only when the
trace names a repository file, module, top-level name or callee's parameter that static comparison
finds absent on the base and present on the branch. That reading holds only where the same file
passes on the branch, which is the condition running this after the branch's own suite satisfies.
For C#, a round that reports no case of a fixture is one where something the branch carried did not
build: that file is withdrawn and the run repeated, so the rest is still measured rather than lost
behind it. Which file is picked off the editor log, over every carried file rather than only the
ones holding cases, since a shared helper takes its whole assembly down with it. A runner that hands
back a results file and nothing else takes one round, and a compile failure there is an empty
directory -- which is what an editor that never started leaves as well. So the Python lane's
comparison is taken statically before that round: `unbuildable_on_base` withdraws a carried file
spelling a name the base has not got and a production file the branch changed does have, ahead of
the run rather than behind an error list that round will not produce. That withdrawal is a static
approximation of what a compiler would say, so it does not outlive the round it stands beside: one
that wrote no results file takes its platform down, withdrawals included. What the
comparison cannot reach -- a signature the branch changed under a name both trees
spell -- leaves a run that measured nothing, which fails, and the refusal names the loop that
separates it.

*A case that stopped before it disagreed answered nothing.* Red on the base means the base ran the
case and the case said no. Except for the statically proven branch-only surface above, a case that
dies before comparing said nothing at all. That includes a C# fixture that compiles on the base and
throws while reflecting for private production state, and every Python exception whose missing
surface is not present on the branch. A case reported
Inconclusive or Skipped there did not run to a verdict either, nor did one the runner refused as
non-runnable or one whose run was cancelled. Counting any of those as red hands back the evidence the
gate exists to demand, in the exact shape it was written to refuse: the branch adds a helper, every
case in the module that reaches for it dies there, and the run reports them all as pinning something.
So they take a verdict of their own, which says the base could not answer, fails the gate, and leaves
the red count to the cases that were answered.

*An exception is not that reading by itself.* A branch that fixes a crash leaves the base throwing
inside the production code the fix repairs, which is the base disagreeing in the plainest way there
is -- and it arrives under the same label as the case that died in its own scaffolding. What
separates them is the first frame of the throw that names a file of this tree: production code, or
the test side. Read over the throw the *body* left, since a case carries what its scaffolding threw
as well as its own -- a setup, a teardown or a test action, each reaching what the case never asked
about. A throw naming no such file keeps the non-answer, so what this reads as red is bounded by
what the results file carries.

*And a scaffolding throw does not always say so in the trace.* One carrying a result state of its
own -- which is what Unity's end-of-scope log check raises, and so what a fixture whose teardown
disposes a base tree's crashing mount produces -- replaces the case's trace outright, marker and
body's frames together, and arrives under the status a failed assertion carries with no label beside
it. The section survives at the head of the message, which is built after that replacement. This reads
it only where the trace does not lead back to the case method, so an assertion beginning with those
words remains a body's disagreement. Behind a body's own message it is not read: a case that disagreed
did so whatever its scaffolding went on to do.

*A case that belongs on the base says so*, above itself, with a reason:

    // GREEN_ON_BASE(characterization): the keyed-reorder order this refactor must not change.

A declaration is per case and answers for the change under it, so it is read three ways: one over a
case that goes red on the base is stale and fails, one whose category or reason is malformed fails,
and one the branch did not itself write is a declaration for a change the base already carries and
does not answer for this one. A declaration therefore cannot outlive what it describes.

Run: python3 scripts/test_quality/base_red_check.py --base origin/main
"""

import argparse
import ast
import importlib.util
import io
import json
import re
import shutil
import subprocess
import sys
import tempfile
import time
import tokenize
import xml.etree.ElementTree as ET
from pathlib import Path

DEFAULT_UNITY = "/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity"
# Anchored at the editor binary so that a shell waiting on this pattern does not match itself and
# report a busy machine forever on an idle one.
UNITY_RUNNING = "^/Applications/.*/MacOS/Unity -runTests"

PASSED_ON_BASE = "passed on the base"
RED_ON_BASE = "red on the base"
COULD_NOT_COMPILE = "could not compile there"
COULD_NOT_LOAD = "could not load there"
COULD_NOT_ANSWER = "could not answer there"
DECLARED_KEPT = "declared, and green as declared"
DECLARED_STALE = "declared, and not as declared"
NOT_REPORTED = "no result was written"
BASE_UNSOUND = "the base tree cannot answer"

FAILING_VERDICTS = (PASSED_ON_BASE, COULD_NOT_ANSWER, DECLARED_STALE, NOT_REPORTED, BASE_UNSOUND)

# The one reading below that no runner reports: a case its scaffolding failed arrives under a status
# and a label that name a disagreement, so the reading has to be taken rather than read off.
SCAFFOLDED = "Scaffolded"

# What a case that stopped before it disagreed with anything comes back as, and what to print for
# each. `reading_of` and `python_outcome` each name a non-answer out of this one dict, so a reading
# is worded the same whichever lane took it.
NOT_AN_ANSWER = {
    "Error": "it raised there rather than failing an assertion",
    "Inconclusive": "an assumption of its own was false there",
    "Skipped": "it was skipped there",
    "Invalid": "the runner would not run it there",
    "Cancelled": "its run was cancelled there",
    SCAFFOLDED: "its scaffolding failed it there, so its body reached no verdict",
    "Unavailable": "it reached a Python surface only the branch provides",
}

# Which of those a results file spells as a `label` beside the result rather than as the result
# itself. Kept apart from the readings so that there is a set to hold against NUnit's own, which the
# dict above is not: its keys are results and labels together. A label missing from here reads as the
# result beside it, which for all three is `Failed` -- a disagreement, and over a declaration a
# correct declaration told to delete itself. `ResultStateVocabularyTests` fails when this stops
# matching the labels NUnit pairs with a failing status.
NOT_A_VERDICT_LABEL = ("Cancelled", "Error", "Invalid")

CATEGORIES = ("characterization", "refactor", "construction")

# `construction` is for a case the base answers green because both sides of what it compares are the
# base's own content -- a guard reading this repository's markdown against its scripts, or a generated
# table against the stylesheets it derives from. Nothing about the base's behaviour is pinned and the
# change is not behaviour-preserving, so neither of the other two fits; what shows the case can fail is
# a perturbation, which no base run can perform.
#
# So its reason has to name that perturbation, and naming is the part a script can hold: a backticked
# span, which is how this repository writes an identifier or a command in prose. The rule is only on
# this category. Measured with this reader over the 286 declarations on `main` when it was added, 263
# carry no backtick at all -- asking the same of `characterization` or `refactor` would rewrite the
# corpus rather than check it.
NAMES_A_PERTURBATION = ("construction",)

BACKTICKED = re.compile(r"`[^`]+`")

# The reason has to say something a reviewer can disagree with, and a word count is the only part of
# that a script can hold. Four words rules out "n/a" and "see above" without pretending to judge prose.
MINIMUM_REASON_WORDS = 4

DECLARATION = re.compile(r"GREEN_ON_BASE\(([A-Za-z]*)\)\s*:\s*(.*)")

# The Python lane has no -testPlatform to be named by, and the soundness plumbing keys on one.
PYTHON_LANE = "python"

# unittest's closing line, which is where it separates an assertion that disagreed from an exception
# that stopped the case.
UNITTEST_SUMMARY = re.compile(r"^(OK|FAILED)(?:\s*\((.*)\))?\s*$", re.MULTILINE)


def speak_under_a_pipe(stream=None):
    """Puts stdout where a reader watching a phase can see it, which `PipeOutputTests` holds it to.

    Everything below prints across phases that take minutes -- a worktree, a Library copy, a wait for
    another editor, an editor run -- so a reader that cannot see a line until the process ends cannot
    tell a run in progress from a wedged one, and both readings have been acted on here. Line
    buffering rather than a flush per call, so a print written after this one is covered by having
    been written.
    """
    stream = sys.stdout if stream is None else stream
    if hasattr(stream, "reconfigure"):
        stream.reconfigure(line_buffering=True)


def _sibling(name):
    """Imports a sibling script by path, since scripts/test_quality is not a package."""
    path = Path(__file__).resolve().with_name(name + ".py")
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


# Which offsets the C# compiler sees as code is one question with one answer, and mutation_check.py
# owns it. Everything here that reads C# structure -- braces, types, namespaces, attributes -- reads
# it through that mask, so a brace or a `class` inside a string or a comment names nothing here.
# How a declaration's reason is read, and which lines one may be read off at all, come from there for
# the same reason: CONTRIBUTING.md says the two markers are read one way, which a second copy of
# either here would be free to stop being.
_mutation_check = _sibling("mutation_check")
code_mask = _mutation_check.code_mask
line_spans = _mutation_check.line_spans
folded_reason = _mutation_check.folded_reason
csharp_comment_spans = _mutation_check.comment_spans
declared_lines = _mutation_check.declared_lines
comment_lines = _mutation_check.comment_lines


class Declaration:
    def __init__(self, category, reason, claim=None, written_here=True, line=None, through=None):
        self.category = category
        self.reason = reason
        self.claim = reason if claim is None else claim
        # A declaration says a case belongs on the base *because of the change under it*. One the
        # branch did not write was written for a change the base already carries, and it answers for
        # that one. Without this the first branch to declare a case green silences every later branch
        # that edits it, however unlike the reason the declaration gives their change is.
        self.written_here = written_here
        self.line = line
        self.through = line if through is None else through

    def written_in(self, lines):
        """Whether the branch wrote any line of the span `folded_reason` reads the reason over."""
        return any(number in lines for number in range(self.line, self.through + 1))

    @property
    def complaint(self):
        if self.category not in CATEGORIES:
            return "category {!r} is not one of {}".format(self.category, ", ".join(CATEGORIES))
        if len(self.claim.split()) < MINIMUM_REASON_WORDS:
            return "the reason's first line is under {} words".format(MINIMUM_REASON_WORDS)
        if self.category in NAMES_A_PERTURBATION and not BACKTICKED.search(self.reason):
            return ("a {} reason names the perturbation that would fail the case, in backticks; "
                    "this one names nothing".format(self.category))
        return None


class Case:
    """One test case, and where in the file it starts and ends."""

    def __init__(self, name, path, first_line, last_line, declaration=None):
        self.name = name
        self.path = path
        self.first_line = first_line
        self.last_line = last_line
        self.declaration = declaration
        self.abstract_owner = None
        self.verdict = None
        self.detail = ""

    @property
    def key(self):
        """How the run reports this case. A Python module is not qualified by anything else."""
        return self.name if kind_of(self.path) == "csharp" else "{}::{}".format(self.path, self.name)

    @property
    def fixture(self):
        """The class, as the run names it -- the unit -testFilter takes and the control is read over."""
        return self.key.rsplit(".", 1)[0]

    def __repr__(self):
        return "Case({!r}, {}-{})".format(self.name, self.first_line, self.last_line)


# --------------------------------------------------------------------------------------------------
# Reading a test file
# --------------------------------------------------------------------------------------------------

# A dot before `Tests` as readily as a slash: the package splits its assemblies as `<Area>/Tests/
# Editor`, and the sample splits its own as `<Name>.Tests/Editor`. Anchoring on the slash alone reads
# the second spelling as production, which leaves the fixtures under it carried onto no base tree and
# their cases in scope of nothing. `RepositoryTests` fails when a fixture Unity compiles reads that
# way, and `assume_gate_check.py` takes its lane reading from here.
CSHARP_TEST_DIR = re.compile(r"[./]Tests/(Editor|PlayMode)/")
CSHARP_ATTRIBUTE = re.compile(r"^\s*\[")
CSHARP_CASE_ATTRIBUTE = re.compile(r"\[\s*(Test|UnityTest|TestCase|TestCaseSource|Theory)\b")
CSHARP_METHOD = re.compile(r"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(")
# `record` takes an optional `class` or `struct` after it, so the name sits one token further along
# than the other two spellings put it.
CSHARP_DECLARES = r"\b(?:class|struct|record(?:\s+(?:class|struct))?)\s+"
CSHARP_TYPE = re.compile(CSHARP_DECLARES + r"([A-Za-z_][A-Za-z0-9_]*)")
CSHARP_ABSTRACT = re.compile(r"\babstract\s+(?:partial\s+)?(?:class|record)\b")
CSHARP_BASES = re.compile(CSHARP_DECLARES + r"[A-Za-z_][A-Za-z0-9_]*\s*(?:<[^>]*>)?\s*:\s*([^{]+)")
CSHARP_IDENTIFIER = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")
CSHARP_NAMESPACE = re.compile(r"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)")


def platform_of(relative):
    """The -testPlatform value for a file, from the directory this repository splits asmdefs by.

    Tests/Editor holds the EditMode assembly: the directory is named for the platform the asmdef is
    restricted to, and the runner takes the name of the mode.
    """
    match = CSHARP_TEST_DIR.search("/" + relative.strip("/"))
    if not match:
        return None
    return "EditMode" if match.group(1) == "Editor" else "PlayMode"


def kind_of(relative):
    """Which lane a repository-relative path belongs to, or None when it holds no test case."""
    if relative.endswith(".cs") and platform_of(relative):
        return "csharp"
    if relative.endswith(".py") and Path(relative).name.startswith("test_"):
        return "python"
    return None


def measured_by(relative):
    """Which run a case's verdict comes back from -- its C# test platform, or the Python lane.

    The soundness reading is per run and not per language: each of them can come back having answered
    nothing, and the canary that says so is chosen and read the same way for both.
    """
    return PYTHON_LANE if kind_of(relative) == "python" else platform_of(relative)


def is_test_side(relative):
    """Whether a file is the branch's test material rather than the production code under test.

    This is what gets carried onto the base tree. TestUtilities is in because a changed helper is
    part of the case that calls it, and a base tree without it compiles neither.
    """
    return kind_of(relative) is not None or "/TestUtilities/" in "/" + relative


def masked_lines(text, mask):
    """Each line with every offset the mask reads as blank spaced out.

    Blanked rather than removed, so a masked line is as long as the raw one and the two can be
    indexed together -- which `csharp_cases` and `assume_gate_check.py` do, a declaration living in a
    comment and everything else not, and `MaskedLineTests` fails when one stops matching. Stripping
    the terminator off the end instead of cutting at the raw line's length does not hold that: a mask
    that swallows the terminator -- a line comment on a CRLF file, or a verbatim string or block
    comment crossing to the next line -- leaves a space in its place, which no strip removes and
    which moves every offset after it.
    """
    return ["".join(text[start + offset] if mask[start + offset] else " "
                    for offset in range(len(raw)))
            for raw, (start, _) in zip(text.splitlines(), line_spans(text))]


def code_lines(text):
    """Each line with every comment, string and character literal blanked to spaces."""
    return masked_lines(text, code_mask(text))


def brace_profile(text):
    """(depth entering, deepest within, depth leaving) per line, counting only what the compiler sees.

    The peak is what separates a type whose body opens on the line below from one that opens and
    closes on its own line: both leave the depth exactly where they found it.
    """
    mask = code_mask(text)
    profile = []
    depth = 0
    for start, end in line_spans(text):
        entering = depth
        peak = depth
        for offset in range(start, end):
            if not mask[offset]:
                continue
            if text[offset] == "{":
                depth += 1
                peak = max(peak, depth)
            elif text[offset] == "}":
                depth -= 1
        profile.append((entering, peak, depth))
    return profile


def comment_block_start(lines, index, prose):
    """The first line of the contiguous comment block directly above `index`, or `index` itself.

    Directly above and nothing between: a blank line or any code ends the block. A comment further up
    belongs to whatever sits under it, and reaching past the gap would let one case's prose cover a
    neighbour nobody wrote it for. The block counts as part of the case, so editing the declaration
    below re-poses the question the declaration answers.

    `prose` is which lines a comment opens, not which lines begin with an opener. Reaching over a
    string literal whose line begins with `//` -- a C# fixture this repository holds as Python data --
    widens the case to cover the field holding it, and what a case covers is what `outside` subtracts:
    the run then stops reporting that the file's other cases are no longer the base's own text.
    """
    probe = index
    while probe > 0 and probe in prose:
        probe -= 1
    return probe


def leading_declaration(lines, index, declared, prose):
    """The declaration in the comment block `comment_block_start` delimits, if there is one.

    `declared` is the same reading `orphaned_declarations` counts the written side over, so a marker
    the one accepts is a marker the other counts.
    """
    for probe in range(comment_block_start(lines, index, prose), index):
        match = declared.get(probe + 1)
        if match:
            claim, reason = folded_reason(match.group(2), lines[probe + 1:index])
            return Declaration(match.group(1), reason, claim=claim, line=probe + 1, through=index)
    return None


def python_comment_spans(text):
    """(start, end) for each span a Python comment covers.

    Tokenised rather than matched on a comment opener at the head of a line, which is a different
    reading and a wrong one here: a C# fixture inside a Python string has lines that open with `//`.
    """
    try:
        tokens = list(tokenize.generate_tokens(io.StringIO(text).readline))
    except (tokenize.TokenError, IndentationError, SyntaxError):
        return []
    starts = [start for start, _ in line_spans(text)]
    spans = []
    for token in tokens:
        if token.type == tokenize.COMMENT:
            opened = starts[token.start[0] - 1] + token.start[1]
            spans.append((opened, opened + len(token.string)))
    return spans


def comment_spans_of(relative, text):
    """Where a comment stands in a file, per its language -- what both readings above are taken from.

    A string literal is where that distinction earns its keep: this repository's test modules hold
    C# fixtures as Python text, markers and all, and a marker there is a fixture's material. The C#
    half is `mutation_check`'s, beside the mask everything else here reads structure through.
    """
    return python_comment_spans(text) if relative.endswith(".py") else csharp_comment_spans(text)


def orphaned_declarations(relative, text):
    """(declarations the file writes, declarations its cases carry). Equal, or one of them is lost.

    One written above a helper, one with a blank line between it and its case, one left in the block
    over the case before: each silences nothing and looks like it does. The case it was meant for
    then fails as green on the base, under advice to write the declaration already above it.

    Compared per file rather than over the tree. Summed, a file that writes one nothing carries is
    cancelled by a file that carries one nothing wrote, and two defects report as none.
    """
    written = declared_lines(text, DECLARATION, comment_spans_of(relative, text))
    return len(written), sum(1 for case in cases_in(relative, text) if case.declaration)


def member_end(lines, ends, signature):
    """The last line of the member whose signature line is `signature`.

    A body that opens a brace ends where the depth comes back; one that does not ends at its
    semicolon. Both spellings are in this repository's fixtures, the second as an expression-bodied
    single assertion.
    """
    outer = ends[signature - 1] if signature > 0 else 0
    opened = False
    for index in range(signature, len(lines)):
        if ends[index] > outer:
            opened = True
        elif opened:
            return index
        elif lines[index].rstrip().endswith(";"):
            return index
    return len(lines) - 1


def csharp_cases(text, path="?"):
    """Every test case in a C# fixture, named as Unity's -testFilter takes it.

    Read off the code lines rather than the raw ones. `class` occurs in this repository's assertion
    messages and in its prose, and a scan that cannot tell those apart from a declaration names the
    cases under a type the runner has never heard of -- which reports nothing, and no reading is one
    of the passing verdicts below.

    A type leaves the stack when the depth its body opened at comes back, not only when the next
    declaration displaces it. Without that a nested helper class declared after the last one, as an
    abstract args type or a fake, owns every case under it for the rest of the file.

    A declaration that opens no body never joins the stack: the unwinding above is keyed on a body
    closing, so one that never opens is never popped, and it holds down every type under it too.
    """
    lines = text.splitlines()
    code = code_lines(text)
    spans = csharp_comment_spans(text)
    declared = declared_lines(text, DECLARATION, spans)
    prose = comment_lines(text, spans)
    profile = brace_profile(text)
    ends = [leaving for _, _, leaving in profile]

    namespace = None
    types = []
    cases = []
    index = 0
    while index < len(lines):
        entering, peak, leaving = profile[index]
        line = code[index]
        while types and types[-1][3] and entering <= types[-1][1]:
            types.pop()
        found = CSHARP_NAMESPACE.search(line)
        if found:
            namespace = found.group(1)
        found = CSHARP_TYPE.search(line)
        if found and not (peak == entering and line.rstrip().endswith(";")):
            types.append([found.group(1), entering, bool(CSHARP_ABSTRACT.search(line)), False])
        if types and not types[-1][3] and peak > types[-1][1]:
            types[-1][3] = True
            if leaving <= types[-1][1]:
                types.pop()

        if CSHARP_ATTRIBUTE.match(line):
            block = index
            while index < len(lines) and (CSHARP_ATTRIBUTE.match(code[index])
                                          or not code[index].strip()):
                index += 1
            attributes = "\n".join(code[block:index])
            signature = code[index] if index < len(lines) else ""
            name = CSHARP_METHOD.search(signature)
            if CSHARP_CASE_ATTRIBUTE.search(attributes) and name:
                owner = ".".join(part for part, _, _, _ in types)
                qualified = ".".join(part for part in (namespace, owner, name.group(1)) if part)
                case = Case(qualified, path, comment_block_start(lines, block, prose) + 1,
                            member_end(code, ends, index) + 1,
                            leading_declaration(lines, block, declared, prose))
                case.abstract_owner = types[-1][0] if types and types[-1][2] else None
                cases.append(case)
            continue
        index += 1
    return cases


def opens_at(node):
    """Where a definition starts, which is its first decorator where it carries one.

    An argument written in a decorator is code of the definition it sits on, so a range over that
    definition opens there rather than at the keyword below it.
    """
    return min([node.lineno] + [one.lineno for one in getattr(node, "decorator_list", ())])


def python_cases(text, path="?"):
    """Every unittest case in a Python test module, named as `python3 -m unittest` takes it."""
    try:
        tree = ast.parse(text)
    except SyntaxError:
        return []
    lines = text.splitlines()
    spans = python_comment_spans(text)
    declared = declared_lines(text, DECLARATION, spans)
    prose = comment_lines(text, spans)
    cases = []
    for node in ast.walk(tree):
        if not isinstance(node, ast.ClassDef):
            continue
        for member in node.body:
            if not isinstance(member, (ast.FunctionDef, ast.AsyncFunctionDef)):
                continue
            if not member.name.startswith("test"):
                continue
            opens = opens_at(member)
            cases.append(Case("{}.{}".format(node.name, member.name), path,
                              comment_block_start(lines, opens - 1, prose) + 1, member.end_lineno,
                              leading_declaration(lines, opens - 1, declared, prose)))
    return cases


def concrete_heirs(corpus):
    """Simple class name -> the qualified names of the concrete classes that inherit it.

    A case written in an abstract fixture runs, and is reported, under each class deriving from it,
    never under the one it is written in. Filtering on the name in the file therefore matches nothing
    and the case reads as one the base could not build, which is not a failure -- so without this a
    branch could rewrite an inherited case and the run would come back green having asked nothing.
    """
    heirs = {}
    for relative, text in corpus.items():
        namespace = None
        for line in code_lines(text):
            found = CSHARP_NAMESPACE.search(line)
            if found:
                namespace = found.group(1)
            declared = CSHARP_TYPE.search(line)
            bases = CSHARP_BASES.search(line)
            if not declared or not bases or CSHARP_ABSTRACT.search(line):
                continue
            qualified = ".".join(part for part in (namespace, declared.group(1)) if part)
            for base in {match.group(0) for match in CSHARP_IDENTIFIER.finditer(bases.group(1))}:
                heirs.setdefault(base, set()).add(qualified)
    return heirs


def as_the_runner_names_them(cases, heirs):
    """Rewrites each case written in an abstract fixture into one case per concrete heir."""
    resolved = []
    for case in cases:
        if not case.abstract_owner:
            resolved.append(case)
            continue
        method = case.name.rsplit(".", 1)[-1]
        for owner in sorted(heirs.get(case.abstract_owner, ())):
            heir = Case(owner + "." + method, case.path, case.first_line, case.last_line,
                        case.declaration)
            resolved.append(heir)
    return resolved


def cases_in(relative, text):
    kind = kind_of(relative)
    if kind == "csharp":
        return csharp_cases(text, relative)
    if kind == "python":
        return python_cases(text, relative)
    return []


def touched(cases, changed_lines):
    """The cases the branch wrote: the ones a changed line lands inside. `None` is a whole new file.

    A case the branch left alone is not a claim about it, whatever else in the file moved. Whether it
    is worth anything as a control is a separate question `collect` answers, and `outside` is half of
    what that answer is read from.
    """
    if changed_lines is None:
        return list(cases)
    return [case for case in cases
            if changed_lines & set(range(case.first_line, case.last_line + 1))]


def case_text(text, case):
    return "\n".join(text.splitlines()[case.first_line - 1:case.last_line])


def outside_comments(relative, text):
    """Each line with its comments blanked to spaces and its literals left standing.

    `code_lines` blanks the literals along with them, and a case whose only change is the value it
    compares against would read there as untouched.
    """
    mask = [True] * len(text)
    for start, end in comment_spans_of(relative, text):
        for offset in range(start, end):
            mask[offset] = False
    return masked_lines(text, mask)


def spanned_code(lines, case):
    """A case's own lines out of a blanked file, empty ones dropped and trailing spaces cut.

    Both are what a blanked comment leaves behind: one taken off the end of a statement leaves the
    spaces it occupied, and one that grew by a line leaves a line that is only spaces. Keeping
    either makes a comment edit read as a changed case.
    """
    return "\n".join(line.rstrip() for line in lines[case.first_line - 1:case.last_line]
                     if line.strip())


def authored(relative, cases, before, after):
    """(the cases whose code this branch changed, the ones it left standing while editing their text).

    Two readings a line number does not carry. A changed line is a proxy for authorship, and which
    lines a diff calls changed is git's choice rather than the file's: over a large rewrite the
    aligner is free to describe an untouched tail as deleted and re-added, and every case in it then
    reads as this branch's. `AuthorshipTests` holds the smallest form of that. And a comment inside a
    case is not what its run decides on, so a branch that only rewrote one wrote nothing separating
    that case from its absence.

    The cost of believing either is a green-on-base verdict asking an author to sharpen a case they
    did not write. `touched` says a case the branch left alone is not a claim about it whatever else
    in the file moved; this is what holds that when what moved is the case, or only the text in it.

    A case whose text the branch left alone as well is in neither list. The second list is what an
    empty plan gets explained by, and a case nobody wrote over explains nothing.
    """
    if before is None:
        return list(cases), []
    held = {case.name: case for case in cases_in(relative, before)}
    was, here = outside_comments(relative, before), outside_comments(relative, after)
    written, aside = [], []
    for case in cases:
        earlier = held.get(case.name)
        if earlier is None or spanned_code(was, earlier) != spanned_code(here, case):
            written.append(case)
        elif case_text(before, earlier) != case_text(after, case):
            aside.append(case)
    return written, aside


def outside(cases, changed_lines):
    """The changed lines inside no case at all -- a SetUp, a field, a helper, a using, a doc comment.

    A change to what several cases share can make an untouched one stop separating anything, and no
    diff says which. Selecting the file whole on that ground was tried and puts every case a fixture
    has on trial for a line added to SetUp, so it is reported instead of acted on. What it does
    settle is that the file's untouched cases are no longer the base's text and cannot read the tree.
    """
    if changed_lines is None:
        return set()
    accounted = set()
    for case in cases:
        accounted |= set(range(case.first_line, case.last_line + 1))
    return changed_lines - accounted


# --------------------------------------------------------------------------------------------------
# Reading the branch
# --------------------------------------------------------------------------------------------------

def git(project, *arguments, check=True):
    return subprocess.run(["git", "-C", str(project), *arguments],
                          capture_output=True, text=True, check=check)


def merge_base(project, base):
    result = git(project, "merge-base", base, "HEAD", check=False)
    if result.returncode != 0:
        raise SystemExit("cannot resolve a merge base with {}: {}".format(base, result.stderr.strip()))
    return result.stdout.strip()


def changed_lines_by_file(project, since):
    """Repository-relative path -> the lines the branch wrote, or None for a file it added whole.

    Diffed against the working tree rather than against HEAD, so a branch whose change is not
    committed yet is still measured -- which is the state a run before a commit is in.
    """
    diff = git(project, "diff", "--unified=0", "--diff-filter=d", since).stdout
    changed = {}
    current = None
    for line in diff.splitlines():
        if line.startswith("+++ b/"):
            current = line[6:]
        elif line.startswith("+++ ") or line.startswith("diff --git"):
            current = None
        elif line.startswith("@@") and current is not None:
            match = re.search(r"\+(\d+)(?:,(\d+))?", line)
            if match:
                start = int(match.group(1))
                count = int(match.group(2) or 1)
                changed.setdefault(current, set()).update(range(start, start + count))
    for name in git(project, "diff", "--name-only", "--diff-filter=A", since).stdout.splitlines():
        changed[name] = None
    for name in git(project, "ls-files", "--others", "--exclude-standard").stdout.splitlines():
        changed[name] = None
    return changed


def asked_about_nothing(derived, wanted):
    """Whether `--platform` names none of the platforms the branch's own cases are on.

    Nothing asked and nothing able to answer are opposite states, and the run cannot tell them apart:
    a platform with no case writes no result, which `decide` reads as a base that could not be read —
    the verdict reserved for a run that happened and produced nothing usable. So the empty
    intersection is answered before the run, where the reason is still in hand.

    Both halves are required. Without `wanted` this fires on every run that names no platform, and
    without `derived` it fires on a branch with no C# case at all, where the Python lane is the
    answer and this would talk over it.
    """
    return bool(wanted) and bool(derived) and not [name for name in derived if name in wanted]


def deleted_files(project, since):
    """The paths the base holds and the branch does not, so the base tree stops holding them either.

    A rename is one of them. Git reports a rename as R rather than as A+D whenever it can pair the
    two halves, so a filter that reads only D leaves the base's copy in place beside the carried one
    -- two files declaring one fixture class, which either refuses to build or reports the same name
    twice for the results to collapse into one.
    """
    gone = git(project, "diff", "--name-only", "--diff-filter=D", since).stdout.splitlines()
    for line in git(project, "diff", "--name-status", "--diff-filter=R", since).stdout.splitlines():
        fields = line.split("\t")
        if len(fields) >= 3:
            gone.append(fields[1])
    return gone


# --------------------------------------------------------------------------------------------------
# Building the base tree
# --------------------------------------------------------------------------------------------------

# What a warm Library must not carry into the base tree. The rest of it is import state -- an artifact
# database, a shader cache, a package cache -- which the base tree would rebuild identically. This one
# holds the assemblies the BRANCH compiled, and a base run that reuses one reports the branch's
# behaviour as the base's.
#
# The two directions are not equally exposed. A leaked assembly that makes a case PASS on the base is
# reported as an undeclared pass and fails the run loudly. One that makes a case FAIL hands the branch
# exactly the verdict it wanted, for its own code rather than the base's, and `red on the base` is the
# answer the lane exists to produce -- so a wrong one looks like a right one and nothing says so.
CARRIES_NOTHING = "ScriptAssemblies"


def clone_tree(source, destination):
    """Copies a Library into the base tree, minus what the branch compiled, sharing blocks if it can.

    The base tree is a checkout the machine has never imported, and the import is most of what a base
    run costs. A byte copy of a Library this size is its own kind of slow, so `cp -c` is tried first
    and the plain copy is what happens when it is refused.

    Excluding here rather than asking each caller to remember a flag: CLAUDE.md already says to leave
    that directory behind when seeding a Library, and CONTRIBUTING.md's own example passed it. The two
    stopped disagreeing when the reading moved into the copy. Measured, the exclusion costs almost
    nothing -- 166 files of 34452, 27 MB of 2.7 GB -- so what `--warm-library` is for is untouched.
    """
    if sys.platform == "darwin":
        result = subprocess.run(["cp", "-Rc", str(source), str(destination)],
                                capture_output=True, text=True)
        if result.returncode == 0:
            shutil.rmtree(Path(destination) / CARRIES_NOTHING, ignore_errors=True)
            return
    shutil.copytree(str(source), str(destination), symlinks=True, dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns(CARRIES_NOTHING))


def build_base_tree(project, commit, destination, carry, drop, warm_library=None):
    git(project, "worktree", "add", "--detach", str(destination), commit)
    for relative in drop:
        for name in (relative, relative + ".meta"):
            target = destination / name
            if target.exists():
                target.unlink()
    for relative in carry:
        for name in (relative, relative + ".meta"):
            origin = project / name
            if not origin.exists():
                continue
            target = destination / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(str(origin), str(target))
    if warm_library and Path(warm_library).is_dir():
        clone_tree(Path(warm_library), destination / "Library")


def withdraw(base_tree, relative):
    """Puts one carried file back to what the base commit holds, or removes one the base never had.

    A file whose fixtures the base named nothing of is withdrawn so the rest of its assembly builds.
    Its own cases are then decided by that silence rather than by the run which follows.
    """
    for name in (relative, relative + ".meta"):
        target = base_tree / name
        held = git(base_tree, "show", "HEAD:" + name, check=False)
        if held.returncode == 0:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(held.stdout)
        elif target.exists():
            target.unlink()


# --------------------------------------------------------------------------------------------------
# Running
# --------------------------------------------------------------------------------------------------

def unity_busy():
    result = subprocess.run(["ps", "-Ao", "command="], capture_output=True, text=True)
    return sum(1 for line in result.stdout.splitlines() if re.match(UNITY_RUNNING, line))


def wait_for_quiet(seconds):
    """Waits rather than sharing the machine, for the reason mutation_check.py waits.

    Announced the way `neuter_check.wait_for_quiet` announces it: once, and only where there is
    something to wait for.
    """
    deadline = time.time() + seconds
    announced = False
    while unity_busy():
        if time.time() > deadline:
            return False
        if not announced:
            print("  another Unity run is in flight; waiting for the machine", flush=True)
            announced = True
        time.sleep(5)
    return True


COMPILE_ERROR = re.compile(r"^(?:.*?[\\/])?((?:Assets|Packages)[\\/][^(]+)\(\d+,\d+\): error CS")


def compile_error_files(log_text):
    """The repository-relative sources a Unity log blames a compile error on."""
    named = []
    for line in log_text.splitlines():
        match = COMPILE_ERROR.match(line.strip())
        if match:
            relative = match.group(1).replace("\\", "/")
            if relative not in named:
                named.append(relative)
    return named


def next_to_withdraw(log_text, carry, withdrawn, silent):
    """Which carried file to put back before asking the base again, after a round reported nothing.

    Every carried file is a candidate, not only the ones holding cases. This repository's conventions
    put a second fixture's shared reach into TestUtilities, that compiles into the same assembly as
    the fixtures, and one the base cannot build takes every one of them down with it -- so a choice
    made only over case-bearing files withdraws innocent fixtures one per round until there are none
    left, and the run ends having measured nothing with nothing saying so.
    """
    return next((name for name in compile_error_files(log_text)
                 if name in carry and name not in withdrawn), silent[0])


def run_unity(unity, tree, platform, fixtures, results, log, timeout):
    command = [
        unity, "-runTests", "-batchmode", "-projectPath", str(tree),
        "-testPlatform", platform, "-testResults", str(results), "-logFile", str(log),
        "-testFilter", ";".join(sorted(fixtures)),
    ]
    started = time.time()
    try:
        subprocess.run(command, timeout=timeout)
    except subprocess.TimeoutExpired:
        pass
    return time.time() - started


# The source a stack-trace frame names, matched under the two roots a Unity project's own code sits
# under so that what comes back is the repository-relative path `is_test_side` reads.
STACK_FRAME_SOURCE = re.compile(r"\bin\s+\.?/?((?:Assets|Packages)/[^\s:]+):\d+")

# The sections a runner opens around one test case. Both readings below are built from this tuple, so
# a name cannot reach one and miss the other. `ScaffoldingSectionRecordingTests` holds it against the
# prefixes the runner's own wrapping commands are constructed with, and owns which commands those are.
SCAFFOLD_SECTIONS = ("AfterTest", "BeforeTest", "SetUp", "TearDown")

# Where a case's own trace stops and a section its scaffolding left begins. A throw out of one is
# recorded onto the case rather than beside it, under the label a throw from the body would have
# carried and without a site naming which of them threw.
#
# Named one by one rather than matched as any `--word`, since an opener of that shape can belong to
# the throw itself and cutting there would lose the body's own trace.
SCAFFOLD_SECTION = re.compile(
    r"^--(?:{})\b".format("|".join(SCAFFOLD_SECTIONS)), re.MULTILINE)

# The same section at the head of the message, which is the reading that survives where the marker
# does not. A `ResultStateException` out of a scaffold -- what Unity's end-of-scope log check raises,
# and what a fixture disposing a base tree's crashing mount produces -- takes a branch that replaces
# the trace whole, marker and the body's own frames together, and the message is built after that
# replacement. `ScaffoldingSectionRecordingTests` holds that, and holds the result and label such a
# case arrives under against the ones a body that disagreed arrives under.
#
# At the head and nowhere else -- a body that disagreed leaves its own message in front, and that case
# disagreed whatever its scaffolding went on to do. `scaffolded` anchors it, and an `^` here as well
# would leave neither able to fail on its own.
SCAFFOLD_MESSAGE = re.compile(r"(?:{}) : ".format("|".join(SCAFFOLD_SECTIONS)))


def threw_in_production(case):
    """Whether the throw that stopped this case came from production code rather than its own body.

    Two shapes arrive under one label and only one of them answered nothing. A fixture reflecting
    from the test assembly for private production state the base has not got gets null back and
    throws where it would have compared: that reached for what the branch adds. A branch that fixes
    a crash leaves the base throwing inside the production code the fix repairs: that is the base
    disagreeing.

    The separator is the first frame that names a file of this tree, over the section the body left.
    A teardown or a test action runs around the body and reaches what the case never asked about, so
    reading past the first marker credits the branch with a disagreement the case never had --
    hardest where the body passed and a marker is all there is. A setup lands on the same side from
    the other direction: its section stands where the body's would, and a case whose setup crashed
    in production never ran the body a verdict would be about.

    Frames naming no file of this tree are skipped rather than decided on, since they place the
    throw on neither side. A throw naming none at all keeps the non-answer, because the verdicts
    that fail a run are not ones to take from a reading nobody could complete.
    """
    trace = case.find("./failure/stack-trace")
    text = (trace.text or "") if trace is not None else ""
    for match in STACK_FRAME_SOURCE.finditer(SCAFFOLD_SECTION.split(text, maxsplit=1)[0]):
        return not is_test_side(match.group(1))
    return False


def scaffolded(case):
    """Whether a runner recorded this case's failure out of its scaffolding rather than its body.

    The message names the section after its marker was replaced. A trace leading back to the case
    method says the body supplied those words itself; the replacement trace does not carry that frame.
    """
    message = case.find("./failure/message")
    match = (SCAFFOLD_MESSAGE.match((message.text or "").strip())
             if message is not None else None)
    if match is None:
        return False
    trace = case.find("./failure/stack-trace")
    name = (case.get("fullname") or case.get("name") or "").split("(")[0].rsplit(".", 1)[-1]
    body = re.search(r"(?:\.|<){}(?:\s*\(|>)".format(re.escape(name)),
                     (trace.text or "") if trace is not None else "")
    return body is None


def reading_of(case):
    """One reported case's reading: its label where that names one, and its result otherwise.

    The label is preferred so that a case which never reached a verdict is not read as the base
    disagreeing with it -- the same line `python_outcome` draws off a unittest trailer. Which throws
    those are is `threw_in_production`'s question.

    A scaffolding failure is read ahead of both, since the shape no label distinguishes is also the
    shape whose trace has been replaced, leaving the message to carry the reading.

    Never over a passing case: nothing read here can reach `passed on the base`, so reading anything
    there could only ever turn that verdict into silence.
    """
    result = case.get("result")
    label = case.get("label")
    if result == "Passed":
        return result
    if scaffolded(case):
        return SCAFFOLDED
    if label not in NOT_A_VERDICT_LABEL:
        return result
    if label == "Error" and threw_in_production(case):
        return result
    return label


def unity_results(results):
    """Reported case name -> the reading, in the one vocabulary `decide` takes for either lane."""
    if not results.exists():
        return {}
    try:
        root = ET.parse(str(results)).getroot()
    except ET.ParseError:
        return {}
    return {case.get("fullname") or case.get("name"): reading_of(case)
            for case in root.iter("test-case")}


def python_outcome(output):
    """One unittest invocation's verdict, in the words the results file uses for the other lane.

    Read off the trailer rather than the exit status, which is one bit over three readings: an
    exception and a failed assertion both exit non-zero and only one of them is the base disagreeing,
    and a skip exits zero and is not the base agreeing.

    No trailer at all is the same reading as an exception rather than one of its own. A module whose
    own top level raises something the loader does not wrap prints none, which is the shape of a case
    reaching for what the branch added; `UnittestTrailerTests` holds which deaths print one. A lane
    where nothing printed a trailer is the canary's question, not this one's.
    """
    summary = None
    for match in UNITTEST_SUMMARY.finditer(output):
        summary = match
    if summary is None:
        return "Error"
    counts = summary.group(2) or ""
    if summary.group(1) == "OK":
        return "Skipped" if "skipped=" in counts else "Passed"
    if "failures=" in counts:
        return "Failed"
    if "errors=" in counts:
        return "Error"
    # A count neither of those names -- an unexpected success is the one unittest can print -- is a
    # trailer this cannot read. Falling back to either neighbour picks a side of the very line this
    # exists to draw, and the two sides are not symmetric: one of them exits zero.
    return "Unreadable"


MISSING_MODULE = re.compile(r"ModuleNotFoundError: No module named ['\"]([^'\"]+)['\"]")
MISSING_FILE = re.compile(
    r"FileNotFoundError: \[Errno 2\] No such file or directory: ['\"]([^'\"]+)['\"]")
MISSING_ATTRIBUTE = re.compile(
    r"AttributeError: module ['\"]([^'\"]+)['\"] has no attribute ['\"]([^'\"]+)['\"]")
# The quote has to follow `from` directly, which is what leaves a circular import out of this
# reading. `PythonNamedSurfaceTests` pins that against the message Python prints for one.
MISSING_MEMBER = re.compile(
    r"ImportError: cannot import name ['\"]([^'\"]+)['\"] from ['\"]([^'\"]+)['\"]")
# What `mock.patch` raises for a name it was asked to replace and did not find. It writes the module
# as a repr rather than as a bare name, which is why this is a pattern of its own rather than a
# widening of the one above.
MISSING_PATCH_TARGET = re.compile(
    r"AttributeError: <module ['\"]([^'\"]+)['\"][^>]*> does not have the attribute "
    r"['\"]([^'\"]+)['\"]")
MISSING_KEYWORD = re.compile(
    r"TypeError: (\w+)\(\) got an unexpected keyword argument ['\"]([^'\"]+)['\"]")


def top_level_names(text):
    """Names a Python module binds without executing it."""
    try:
        tree = ast.parse(text)
    except SyntaxError:
        return set()

    def targets(node):
        if isinstance(node, ast.Name):
            return {node.id}
        if isinstance(node, (ast.Tuple, ast.List)):
            return set().union(*(targets(item) for item in node.elts)) if node.elts else set()
        return set()

    names = set()
    for node in tree.body:
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            names.add(node.name)
        elif isinstance(node, ast.Assign):
            for target in node.targets:
                names.update(targets(target))
        elif isinstance(node, ast.AnnAssign):
            names.update(targets(node.target))
        elif isinstance(node, (ast.Import, ast.ImportFrom)):
            names.update(alias.asname or alias.name.split(".")[0] for alias in node.names)
    return names


def climbed(path, levels):
    """A climb past the repository root is a fold that failed, rather than the root itself."""
    for _ in range(levels):
        if path == Path("."):
            return None
        path = path.parent
    return path


def path_bindings(parsed):
    """Name -> the expressions a module assigns to it, over the whole file rather than its top level.

    An insert can sit inside a case body, and the path it hands over can be bound there or above it,
    so neither the statement nor its name is reliably at the top level.
    """
    found = {}
    for node in ast.walk(parsed):
        if isinstance(node, ast.Assign) and len(node.targets) == 1 and isinstance(
                node.targets[0], ast.Name):
            found.setdefault(node.targets[0].id, []).append(node.value)
    return found


def folded_path(node, home, bound, seen=frozenset()):
    """The repository-relative paths an expression rooted at `Path(__file__)` folds to.

    `home` is the file the expression was read out of, so `__file__` is that path and each `.parent`
    or `.parents[n]` climbs from it. Anything the fold does not recognise contributes nothing, so an
    insert it cannot read leaves that directory out rather than guessing at one.
    """
    if isinstance(node, ast.Name):
        if node.id == "__file__":
            return {home}
        if node.id in seen:
            return set()
        return set().union(*(folded_path(value, home, bound, seen | {node.id})
                             for value in bound.get(node.id, ())))
    if isinstance(node, ast.Call):
        if isinstance(node.func, ast.Name) and node.func.id in ("str", "Path", "PurePosixPath"):
            return folded_path(node.args[0], home, bound, seen) if node.args else set()
        if isinstance(node.func, ast.Attribute) and node.func.attr == "resolve":
            return folded_path(node.func.value, home, bound, seen)
        return set()
    if isinstance(node, ast.Attribute) and node.attr == "parent":
        return {up for up in (climbed(path, 1)
                              for path in folded_path(node.value, home, bound, seen)) if up}
    if isinstance(node, ast.Subscript):
        owner, index = node.value, node.slice
        if (isinstance(owner, ast.Attribute) and owner.attr == "parents"
                and isinstance(index, ast.Constant) and isinstance(index.value, int)):
            return {up for up in (climbed(path, index.value + 1)
                                  for path in folded_path(owner.value, home, bound, seen)) if up}
        return set()
    if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Div):
        if not (isinstance(node.right, ast.Constant) and isinstance(node.right.value, str)):
            return set()
        return {path / node.right.value
                for path in folded_path(node.left, home, bound, seen)}
    return set()


def import_directories(case, tree):
    """The repository directories an import in the case is looked for under, its own first.

    Only the case's own file is read, so an insert a module it imports performs reaches nothing here.
    `scripts/hooks/test_amend_of_published_commit.py` is the file this is written for: it puts
    `.claude/hooks/lib` on the path itself, and a module named through that sits beside no case.
    """
    home = Path(case.path)
    found = [home.parent]
    source = tree / case.path
    try:
        parsed = ast.parse(source.read_text(encoding="utf-8", errors="replace")
                           if source.is_file() else "")
    except SyntaxError:
        return found
    bound = path_bindings(parsed)
    for node in ast.walk(parsed):
        if not (isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute)
                and node.func.attr in ("insert", "append") and node.args):
            continue
        owner = node.func.value
        if not (isinstance(owner, ast.Attribute) and owner.attr == "path"
                and isinstance(owner.value, ast.Name) and owner.value.id == "sys"):
            continue
        for directory in sorted(folded_path(node.args[-1], home, bound)):
            if directory not in found:
                found.append(directory)
    return found


def module_relatives(case, module, tree):
    """The files the module named in a traceback could be, one per directory the run searches."""
    parts = module.split(".")
    return [directory.joinpath(*parts).with_suffix(".py")
            for directory in import_directories(case, tree)]


def added_top_level_name(base, branch, name):
    if not base.is_file() or not branch.is_file():
        return False
    return (name not in top_level_names(base.read_text(encoding="utf-8", errors="replace"))
            and name in top_level_names(branch.read_text(encoding="utf-8", errors="replace")))


def takes_keyword(text, function, keyword):
    """Whether a module's definitions of `function` accept `keyword`.

    A `**kwargs` catch-all accepts every keyword, so any definition carrying one answers yes.
    Recording the catch-all's own name in a set of accepted keywords instead reads a base that would
    have taken the call as one that could not, which credits the branch with a surface it did not
    add.
    """
    try:
        tree = ast.parse(text)
    except SyntaxError:
        return False
    for node in ast.walk(tree):
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and node.name == function:
            taken = node.args
            if taken.kwarg or any(argument.arg == keyword for argument in
                                  taken.posonlyargs + taken.args + taken.kwonlyargs):
                return True
    return False


def added_keyword(output, base_tree, branch_tree, case):
    """Whether the call that raised named a parameter the branch added to a module the case can reach.

    A parameter is a surface the same way a name is, and the exception is the only place Python says
    so -- the trace names the caller, never the callee's module, so which one holds the definition is
    read by looking, over the directories the run imports from. One base-side definition accepting the
    keyword is enough to refuse: the reading has to be that the base could not have taken this call,
    not that some module somewhere could not.

    The name readings are gated on `reaches_surface` and this one is not, deliberately. A keyword is
    a single token: each of those compares a set of spellings that includes the module's own name, so
    a case naming the module qualifies however it spells the member, while a keyword appears at the
    call site and nowhere else. Cases here drive their subject through a
    helper class the fixture does not derive from -- `Polled`, `Workspace`, `StubbedCampaign` -- so
    the call site sits outside every line the case reaches, and gating this would refuse those cases
    with no declaration able to clear the verdict. What it costs is a call made at class scope
    answering for the whole file, which `KeywordAtClassScopeTests` records.
    """
    missing = MISSING_KEYWORD.search(output)
    if not missing:
        return False
    function, keyword = missing.group(1), missing.group(2)
    found = False
    for directory in import_directories(case, base_tree):
        for module in sorted((branch_tree / directory).glob("*.py")) if (
                branch_tree / directory).is_dir() else []:
            base = base_tree / directory / module.name
            if takes_keyword(base.read_text(encoding="utf-8", errors="replace") if base.is_file()
                             else "", function, keyword):
                return False
            found = found or takes_keyword(
                module.read_text(encoding="utf-8", errors="replace"), function, keyword)
    return found


def fixture_classes(parsed, owner):
    """`owner` and the classes it derives from, over the ones this file declares by bare name.

    A shared base class is where unittest puts scaffolding two fixtures need, so a `setUp` there is
    as much an heir's reach as one written in its own body. A dotted base is left alone because its
    last segment is not a spelling this file binds: resolving `helpers.Shared` to a `Shared` declared
    here picks a class the fixture never derived from.
    """
    declared = {node.name: node for node in ast.walk(parsed) if isinstance(node, ast.ClassDef)}
    found, pending = set(), [owner]
    while pending:
        name = pending.pop()
        if name in found or name not in declared:
            continue
        found.add(name)
        pending.extend(base.id for base in declared[name].bases if isinstance(base, ast.Name))
    return found


def reaching_lines(text, case_name):
    """The lines of a test module whose text one case could have reached.

    Module level, the classes its fixture is built out of outside their other cases, and the case
    itself. Import statements are left out: `from module import name` is evaluated once for the file,
    so a name spelled only there belongs to no case in particular. A string constant standing alone
    is left out too -- a docstring is that shape, and it looks up no name.
    """
    try:
        parsed = ast.parse(text)
    except SyntaxError:
        return set()
    owner, method = case_name.rsplit(".", 1) if "." in case_name else ("", case_name)
    owners = fixture_classes(parsed, owner)
    reaching = set(range(1, len(text.splitlines()) + 1))
    for node in ast.walk(parsed):
        if isinstance(node, (ast.Import, ast.ImportFrom)):
            reaching -= set(range(node.lineno, (node.end_lineno or node.lineno) + 1))
        elif (isinstance(node, ast.Expr) and isinstance(node.value, ast.Constant)
                and isinstance(node.value.value, str)):
            reaching -= set(range(node.lineno, (node.end_lineno or node.lineno) + 1))
        elif isinstance(node, ast.ClassDef) and node.name not in owners:
            reaching -= set(range(opens_at(node), node.end_lineno + 1))
        elif isinstance(node, ast.ClassDef):
            for member in node.body:
                if (isinstance(member, (ast.FunctionDef, ast.AsyncFunctionDef))
                        and member.name.startswith("test") and member.name != method):
                    reaching -= set(range(opens_at(member), member.end_lineno + 1))
    return reaching - comment_lines(text, python_comment_spans(text))


def imported_from(text, module):
    """Every alias the file binds out of `module`, as (the name the case spells, the module's own)."""
    try:
        parsed = ast.parse(text)
    except SyntaxError:
        return []
    bound = []
    for node in ast.walk(parsed):
        if isinstance(node, ast.ImportFrom) and node.module == module and not node.level:
            bound.extend((alias.asname or alias.name, alias.name) for alias in node.names)
        elif isinstance(node, ast.Import):
            bound.extend((alias.asname or alias.name.split(".")[0], alias.name)
                         for alias in node.names if alias.name == module)
    return bound


def surface_spellings(case, base_tree, branch_tree, module, name=None):
    """How the case's own file could be spelling the branch-only surface, not just how Python named it.

    A traceback names one missing name however many of them an import list holds, so a case reaching
    another of them would read as reaching nothing at all. `name` is the one it named; the rest are
    read off the import list and kept only where the same comparison holds. A module the base has not
    got is every name its imports bind, since none of them resolves there.
    """
    spellings = {name} if name else set()
    source = base_tree / case.path
    if not source.is_file():
        return spellings
    bound = imported_from(source.read_text(encoding="utf-8", errors="replace"), module)
    if name is None:
        return spellings | {spelled for spelled, _ in bound} | {module.split(".")[0]}
    for relative in module_relatives(case, module, base_tree):
        base, branch = base_tree / relative, branch_tree / relative
        spellings |= {spelled for spelled, attribute in bound
                      if added_top_level_name(base, branch, attribute)}
    return spellings


def reaches_surface(case, tree, name):
    """Whether this case, rather than the file it sits in, is the one that reached `name`.

    A module that will not load takes every case in it down together, so without this the tolerance
    below answers for cases that never touched the branch's surface -- and each of those is a reading
    nobody took being recorded as a pass, which is what this whole check exists to refuse.
    Scaffolding counts as the case's own reach: a `setUpClass` naming the surface fails for each case
    of its fixture, which is that dependence written once.
    """
    source = tree / case.path
    if not source.is_file():
        return False
    text = source.read_text(encoding="utf-8", errors="replace")
    lines = text.splitlines()
    return any(name in names_in(lines[number - 1])
               for number in reaching_lines(text, case.name) if number <= len(lines))


def added_python_surface(output, base_tree, branch_tree, case):
    """Whether the traceback names a file or module member present only on the branch.

    The branch's own suite is the precondition that the surface exists and works there. Both sides are
    inspected here so an arbitrary exception, missing environment file or misspelled member remains a
    non-answer and fails the gate.
    """
    missing = MISSING_FILE.search(output)
    if missing:
        path = Path(missing.group(1))
        try:
            relative = path.resolve().relative_to(base_tree.resolve())
        except ValueError:
            return False
        return not (base_tree / relative).exists() and (branch_tree / relative).is_file()

    missing = MISSING_MODULE.search(output)
    if missing:
        module = missing.group(1)
        return any(reaches_surface(case, base_tree, spelled) for spelled in
                   surface_spellings(case, base_tree, branch_tree, module)) and any(
            not (base_tree / relative).exists() and (branch_tree / relative).is_file()
            for relative in module_relatives(case, module, base_tree))

    missing = MISSING_ATTRIBUTE.search(output) or MISSING_PATCH_TARGET.search(output)
    if missing:
        module, name = missing.group(1), missing.group(2)
    else:
        missing = MISSING_MEMBER.search(output)
        if not missing:
            return added_keyword(output, base_tree, branch_tree, case)
        module, name = missing.group(2), missing.group(1)
    return any(reaches_surface(case, base_tree, spelled) for spelled in
               surface_spellings(case, base_tree, branch_tree, module, name)) and any(
        added_top_level_name(base_tree / relative, branch_tree / relative, name)
        for relative in module_relatives(case, module, base_tree))


IDENTIFIER = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")


def names_in(text):
    return set(IDENTIFIER.findall(text))


def csharp_names(tree, carried=()):
    """Every identifier the base commit spelled, over the C# files the base tree still holds.

    A test file the branch deleted is one the base tree stops holding, and reading that one back out
    of the commit is what would leave a case naming a production surface the branch adds -- where the
    base's only spelling of that name was the deleted file's -- in a run whose compile it takes down.

    `carried` names the files whose text on disk is the branch's, which is the text being judged:
    each is read back out of the commit instead. Leaving them out altogether asks a different
    question -- whether the branch spelled a name occurring in no *other* file -- and answers yes to
    an identifier the fixture declared before this branch, which the base builds perfectly well.

    Raw rather than masked, and that asymmetry is the point: on the base this decides that a name is
    *present*, so counting a comment's spelling suppresses a withdrawal that a masked read would have
    taken. Erring towards leaving a file in the run is the direction a wrong reading here is
    survivable in.
    """
    found = set()
    for relative in git(tree, "ls-files").stdout.splitlines():
        if not relative.endswith(".cs"):
            continue
        if relative in carried:
            held = held_at(tree, "HEAD", relative)
            if held is not None:
                found |= names_in(held)
            continue
        path = tree / relative
        if path.exists():
            found |= names_in(path.read_text(encoding="utf-8", errors="replace"))
    return found


def added_csharp_surface(text, base_names, added_names):
    """The name a carried C# file spells that the base has not got, or None.

    `added_python_surface` reads the same fact off a traceback, which C# leaves nowhere a single round
    can reach -- the module docstring owns why. What is left is the comparison itself, and
    `csharp_names` owns what "the base has not got" is read over. Requiring the branch to spell the
    name too, in a file it changed and does not carry, is what keeps the names a carried file
    resolves out of an assembly from reading as ones the branch added -- bar one the changed
    production file spells as well, which is among the approximations `unbuildable_on_base` records.
    """
    return next((name for name in sorted(names_in("\n".join(code_lines(text))) & added_names)
                 if name not in base_names), None)


def unbuildable_on_base(project, since, base_tree, carry):
    """Carried C# test file -> the name it spells that the base tree has not got.

    Read before the run rather than after, because after is a silence that names nothing.

    Both sides are spellings rather than what a compiler would resolve, and two of the ways they part
    company withdraw a file the base builds. `added` is what a changed production file spells rather
    than what it declares, so a type the branch first reaches for in production and in a carried case
    at once reads as one the base has not got. `base_names` is the base commit's text rather than the
    tree's -- which holds the branch's copy of every carried file -- so a name the branch's own test
    side declares reads that way too. Measured: each withdraws a file that compiles there.
    """
    changed = [name for name in changed_lines_by_file(project, since)
               if name.endswith(".cs") and name not in carry]
    added = set()
    for relative in changed:
        source = project / relative
        if source.exists():
            added |= names_in("\n".join(code_lines(
                source.read_text(encoding="utf-8", errors="replace"))))
    if not added:
        return {}
    base_names = csharp_names(base_tree, carried=carry)
    found = {}
    for relative in carry:
        if kind_of(relative) != "csharp":
            continue
        source = project / relative
        if not source.exists():
            continue
        name = added_csharp_surface(source.read_text(encoding="utf-8", errors="replace"),
                                    base_names, added)
        if name is not None:
            found[relative] = name
    return found


def run_python(tree, branch_tree, cases, transcript):
    """Runs one case at a time, so a module-level failure cannot report as some other case's verdict."""
    outcome = {}
    for case in cases:
        module = Path(case.path)
        identifier = "{}.{}".format(module.stem, case.name)
        result = subprocess.run([sys.executable, "-m", "unittest", "-v", identifier],
                                cwd=str(tree / module.parent), capture_output=True, text=True)
        printed = result.stdout + result.stderr
        transcript.append("$ python3 -m unittest {} (in {})\n{}".format(
            identifier, module.parent, printed))
        verdict = python_outcome(printed)
        if verdict in ("Error", "Unreadable") and added_python_surface(
                printed, tree, branch_tree, case):
            verdict = "Unavailable"
        outcome[case.key] = verdict
    return outcome


# --------------------------------------------------------------------------------------------------
# Deciding
# --------------------------------------------------------------------------------------------------

def outcome_for(name, reported):
    """What the base run said about one case, over however many entries the name covers.

    A [TestCase] method comes back as one entry per argument list, named `Method(1)`, so an exact
    lookup would find the method absent and fail closed on a case that ran perfectly well. The method
    passed on the base only where every one of its argument lists did.
    """
    results = [result for key, result in reported.items()
               if key == name or key.startswith(name + "(")]
    if not results:
        return None
    if all(result == "Passed" for result in results):
        return "Passed"
    # An argument list that disagreed outranks one that stopped before it could. The method is red
    # on the base as soon as any list ran there and said no, and taking whichever came first would
    # leave dict order to decide whether a declaration over such a method reads as stale -- a
    # failing verdict against a non-failing one, off the order two entries happen to sit in.
    unpassed = [result for result in results if result != "Passed"]
    return next((result for result in unpassed if answered(result)), unpassed[0])


def canary_fixtures(base_tree, platform, carry, wanted=3):
    """Fixtures of the base tree's own that the branch did not touch, in the order git lists them.

    Which ones is not the question: `canaries_for` owns why they run at all, and the bar is one of
    them passing.
    """
    found = []
    # Tracked, not walked. Walking took the first three fixtures out of Library/PackageCache, which
    # compile into Unity's own assemblies and answer to no filter this project can pose -- so the
    # canary found nothing passing and withdrew the very branch it was there to vouch for.
    for relative in git(base_tree, "ls-files").stdout.splitlines():
        if kind_of(relative) != "csharp" or platform_of(relative) != platform or relative in carry:
            continue
        path = base_tree / relative
        if not path.exists():
            continue
        cases = csharp_cases(path.read_text(encoding="utf-8", errors="replace"), relative)
        if cases:
            found.append(cases[0].fixture)
        if len(found) == wanted:
            break
    return found


def python_canaries(base_tree, carry, wanted=3):
    """Cases of the base tree's own Python modules that the branch did not carry, git's order.

    The C# lane's `canary_fixtures` owns why a lane needs these at all. One case per module rather than
    every case of one, so that one unimportable module is not the whole reading.
    """
    found = []
    for relative in git(base_tree, "ls-files").stdout.splitlines():
        if kind_of(relative) != "python" or relative in carry:
            continue
        path = base_tree / relative
        if not path.exists():
            continue
        cases = python_cases(path.read_text(encoding="utf-8", errors="replace"), relative)
        if cases:
            found.append(cases[0])
        if len(found) == wanted:
            break
    return found


def fixtures_that_ran(reported):
    """The fixtures the base run named at least one case of."""
    return {name.split("(")[0].rsplit(".", 1)[0] for name in reported}


def unsound_platforms(canaries, reported):
    """Platform -> why its canaries say the base tree answered nothing at all.

    One passing case is the whole bar. Which of them passes is not the question -- the question is
    whether anything on this platform built and ran, since a tree where nothing did reports every
    case as uncompilable, and uncompilable is not a failure here.
    """
    broken = {}
    for platform, fixtures in canaries.items():
        if not fixtures:
            broken[platform] = "no base-tree canary was available there"
            continue
        ran = [result for name, result in reported.items()
               if name.split("(")[0].rsplit(".", 1)[0] in fixtures]
        if not any(result == "Passed" for result in ran):
            broken[platform] = "none of {} passed there".format(
                ", ".join(re.split(r"[.:]", fixture)[-1] for fixture in fixtures))
    return broken


def unsound_fixtures(control, reported):
    """Fixture -> the control case that says the base tree is answering about itself.

    A control case is one the branch left alone, so the base tree holds exactly the text the base's
    own green run held. Anything but green from it is the environment.
    """
    broken = {}
    for case in control:
        result = outcome_for(case.key, reported)
        if result not in (None, "Passed") and case.fixture not in broken:
            broken[case.fixture] = "{} is {} there".format(case.name.rsplit(".", 1)[-1], result.lower())
    return broken


def answered(result):
    """Whether the base ran the case to a verdict about behaviour -- it agreed, or it disagreed.

    A case that raised, one that hit an Assume of its own, one that was skipped, one whose summary
    could not be read and one nothing reported at all each stopped before that, and the verdicts
    below say which.
    """
    return result is not None and result not in NOT_AN_ANSWER and result != "Unreadable"


def decide(case, result, fixture_ran):
    """One case's verdict from what the base run said about it.

    `fixture_ran` is what separates the two ways a case can be missing, and it is why nothing here
    reads the editor log. A fixture that reported nothing built nothing -- the branch's test names a
    symbol the base has not got, which is the evidence this is looking for. A fixture that reported
    its other cases and not this one resolved the name wrongly, and a name nobody answered to is a
    reading nobody took.
    """
    declaration = case.declaration
    if declaration is not None and not declaration.written_here:
        if result == "Passed":
            return PASSED_ON_BASE, "its declaration is the base's own; restate it for this change"
        declaration = None
    if declaration is not None:
        complaint = declaration.complaint
        if complaint:
            return DECLARED_STALE, complaint
        if result == "Passed":
            return DECLARED_KEPT, "{}: {}".format(declaration.category, declaration.reason)
        # Only a run that reached a verdict can call a declaration stale. "Remove the declaration"
        # is advice about a case the base measured, and a case it never measured falls through to
        # the reading that says so -- otherwise a declaration is refused for the environment
        # skipping the case, and the fix it asks for is to delete something correct.
        if answered(result):
            return DECLARED_STALE, "it is {} on the base; remove the declaration".format(
                result.lower())
    if result == "Unavailable":
        return COULD_NOT_LOAD, NOT_AN_ANSWER[result]
    if result == "Passed":
        return PASSED_ON_BASE, "nothing this branch changed decides it"
    if result in NOT_AN_ANSWER:
        return COULD_NOT_ANSWER, NOT_AN_ANSWER[result]
    if result == "Unreadable":
        return NOT_REPORTED, "its run printed a summary this could not read"
    if result is None and not fixture_ran:
        return COULD_NOT_COMPILE, "the base built none of this fixture"
    if result is None:
        return NOT_REPORTED, "its fixture ran and nothing answered to this name"
    return RED_ON_BASE, result.lower()


def canaries_for(base_tree, cases, carry, only=None):
    """Platform -> the base's own fixtures to read that platform's tree by.

    Every platform, not only the ones the branch left no control case on. A control answers for its
    own fixture, and a fixture the base named nothing of is legitimate evidence there -- so where a
    run comes back empty, the controls say uncompilable and nothing says the run happened at all.
    """
    chosen = {}
    for platform in sorted({platform_of(case.path) for case in cases
                            if kind_of(case.path) == "csharp"}):
        if not only or platform in only:
            chosen[platform] = canary_fixtures(base_tree, platform, carry)
    return chosen


def as_plan(since, cases, control, shared, canaries, withdrawn=None):
    """The reading, in a form a second invocation can decide from.

    A run split across two invocations is what lets the editor be somebody else's -- CI reaches Unity
    only through an action, so the tree is built and read here, the action runs, and the verdict is
    taken from the results file it leaves.
    """
    def entry(case):
        return {
            "name": case.name, "path": case.path, "key": case.key, "fixture": case.fixture,
            # Positional, in the constructor's order, and the claim rides along: the far side
            # decides, and a declaration reaching it without one has its floor measured over the fold.
            "declaration": (None if case.declaration is None
                            else [case.declaration.category, case.declaration.reason,
                                  case.declaration.claim, case.declaration.written_here]),
        }

    return {"since": since, "shared": shared, "canaries": canaries,
            "withdrawn": withdrawn or {},
            "cases": [entry(case) for case in cases],
            "control": [entry(case) for case in control]}


def from_plan(entries):
    cases = []
    for entry in entries:
        declaration = None if entry["declaration"] is None else Declaration(*entry["declaration"])
        case = Case(entry["name"], entry["path"], 0, 0, declaration)
        cases.append(case)
    return cases


def results_from(where):
    """(what the run reported, whether it wrote a results file at all).

    The second half is not the first being empty. A round that wrote nothing did not get as far as
    running, and this treats that as the base failing to build what was carried onto it -- which is
    evidence, and not the same as a round that ran and reported no case of some fixture.
    """
    path = Path(where)
    files = [file for file in (sorted(path.glob("*.xml")) if path.is_dir() else [path])
             if file.exists()]
    reported = {}
    for file in files:
        reported.update(unity_results(file))
    return reported, bool(files)


def report(cases, control, reported, canaries=None, wrote=True, unbuildable=None,
           single_round=None):
    """Prints each case's verdict and returns the ones that fail the run.

    `wrote` is whether the run produced a results file at all, and for a C# case it outranks
    everything below it, `unbuildable` included. COULD_NOT_COMPILE off the run says the base built
    the rest and not this, which takes a base that built something; COULD_NOT_COMPILE off
    `unbuildable` is a static approximation of a compiler question, accepted only beside a round
    that got as far as writing.
    A branch whose every changed case sits in a withdrawn file would otherwise pass on a licence
    failure, an editor crash or a timeout.

    `unbuildable` outranks the rest of them, for the reason `added_python_surface` is read before a
    Python case's outcome: the file was taken out before the run, so a verdict off what that run said
    under its fixture name is a verdict about the base's own text standing in its place.

    `single_round` is the merge base when this invocation decided from one round it did not itself
    run, and absent when the loop below it ran to a fixed point here -- which is what the remedy the
    first case prints would be sending its reader to do again.
    """
    unsound = unsound_fixtures(control, reported)
    withdrawn = unsound_platforms(canaries or {}, reported)
    unbuildable = unbuildable or {}
    unmeasured = {}
    if not wrote:
        unmeasured = {platform: "the run wrote no results file, so nothing was measured"
                      for platform in {platform_of(case.path) for case in cases
                                       if kind_of(case.path) == "csharp"}}
    ran = fixtures_that_ran(reported)
    for case in cases:
        if measured_by(case.path) in unmeasured:
            case.verdict, case.detail = BASE_UNSOUND, unmeasured[measured_by(case.path)]
        elif case.path in unbuildable:
            case.verdict, case.detail = COULD_NOT_COMPILE, "the base has no {}".format(
                unbuildable[case.path])
        elif case.fixture in unsound:
            case.verdict, case.detail = BASE_UNSOUND, unsound[case.fixture]
        elif measured_by(case.path) in withdrawn:
            case.verdict, case.detail = BASE_UNSOUND, withdrawn[measured_by(case.path)]
        else:
            case.verdict, case.detail = decide(case, outcome_for(case.key, reported),
                                               case.fixture in ran)
    print("\n--- what the base said ---", flush=True)
    for case in cases:
        print("{:<32} {}  ({})".format(case.verdict, case.name, case.detail), flush=True)
    # Three counts, never one. A case the base could not build names a symbol the branch adds, which
    # is the strongest pin this takes, and folding it into the readings nobody took would tell the
    # author of a correct test that the run measured nothing. A tolerated case is not a failure, so
    # a run that tolerated all of them exits zero and says so in silence unless this count speaks.
    tolerated = [case for case in cases if case.verdict == COULD_NOT_LOAD]
    if tolerated:
        print("\n{} of {} case(s) reached a surface only the branch provides, so the base could not "
              "load them\nand each is counted as depending on this change".format(
                  len(tolerated), len(cases)), flush=True)
    silent = [case for case in cases if case.verdict == COULD_NOT_ANSWER]
    if silent:
        print("\nthe base could not answer for {} of {} case(s), so they carry no reading either "
              "way".format(len(silent), len(cases)), flush=True)
    unbuilt = [case for case in cases if case.verdict == COULD_NOT_COMPILE]
    if unbuilt:
        print("\n{} of {} case(s) sit in a fixture the base built none of, so the reading is that "
              "fixture's rather than each case's".format(len(unbuilt), len(cases)), flush=True)
    offenders = [case for case in cases if case.verdict in FAILING_VERDICTS]
    # Split by whether a behavioural verdict exists, because one remedy does not cover both. Offering
    # a declaration where the run produced none sends the author to sharpen a case that may be perfectly
    # sharp -- a neighbour's Assume is enough to land one here.
    unanswered = [case for case in offenders
                  if case.verdict in (COULD_NOT_ANSWER, NOT_REPORTED, BASE_UNSOUND)]
    answered = [case for case in offenders if case not in unanswered]
    if answered:
        print("\n{} case(s) the base already answers. Green on both sides separates nothing: sharpen "
              "the\ncase until it goes red without this branch, or say why it belongs "
              "there:".format(len(answered)), flush=True)
        print("  // GREEN_ON_BASE({}): <why>".format("|".join(CATEGORIES)), flush=True)
    if unanswered:
        print("\n{} case(s) yielded no base verdict, so none of them carries a reading either way. A\n"
              "declaration does not answer for that -- the detail beside each says what happened."
              .format(len(unanswered)), flush=True)
    if single_round and not wrote and any(case.verdict == BASE_UNSOUND for case in unanswered):
        print(local_remedy(single_round, unanswered), flush=True)
    return offenders


def local_remedy(since, cases):
    """What to run when a single round came back silent and the reading has to be taken another way.

    A base that builds none of what was carried onto it and an editor that never started are the same
    empty directory, and the withdrawing loop is what separates them. Naming the command is the
    difference between a refusal an author can act on and one they can only look at.
    """
    platforms = sorted({platform_of(case.path) for case in cases
                        if kind_of(case.path) == "csharp"})
    return ("\nThe editor wrote nothing, which a single round cannot tell from an editor that never\n"
            "started. Take the reading where the loop runs -- it withdraws by the editor's own error\n"
            "list and asks again until something answers:\n"
            "  python3 scripts/test_quality/base_red_check.py --lane csharp{} --base {} \\\n"
            "    --warm-library Library".format(
                "".join(" --platform " + platform for platform in platforms), since or "origin/main"))


def exhausted_reason(spent, withdrawn, carried):
    """What a loop that ran out of rounds without ever compiling the base has to say for itself.

    The generic line is the one a single round writes, and a reader who ran the loop has already done
    the thing that line would tell them to do. What separates the two is the loop's own history: how
    many rounds it spent, how much of the carried set it put back, and that the budget is a flag.

    Naming the flag because the budget is what binds, not the shape of the change. Measured on the
    branch replacing UniTask with an in-tree awaitable: at the default of 8 the loop reported nothing
    measured, and at 60 it compiled the base on round 38 and gave every case a verdict. One round
    withdraws one file, so a change touching many test modules needs about as many rounds as modules.

    Whether the budget is what ended the run is the caller's to know, and it does not print this
    otherwise: a run that stopped short of its budget stopped for some other reason, and raising the
    budget is not its remedy.
    """
    if not spent:
        return ""
    put_back = "\n".join("    " + name for name in sorted(withdrawn)[:6])
    more = len(withdrawn) - 6
    if more > 0:
        put_back += "\n    and {} more".format(more)
    return ("\n{} round(s) compiled nothing, having put {} of the {} carried file(s) back to what the\n"
            "base holds. One round withdraws one file, so a change whose carried modules the base\n"
            "cannot build needs about as many rounds as there are of them:\n"
            "  --max-rounds {}\n"
            "{}".format(spent, len(withdrawn), carried, max(carried, spent * 4),
                        put_back if withdrawn else ""))


def held_at(project, commit, relative):
    """A file's text at a commit, or None where the commit has not got it."""
    result = git(project, "show", "{}:{}".format(commit, relative), check=False)
    return result.stdout if result.returncode == 0 else None


def corpus_of(project):
    """Every tracked C# test file's text, for the questions one file cannot answer about itself."""
    names = git(project, "ls-files").stdout.splitlines()
    return {relative: (project / relative).read_text(encoding="utf-8", errors="replace")
            for relative in names
            if kind_of(relative) == "csharp" and (project / relative).exists()}


def changed_test_files(project, by_file, lane):
    """(path, the lines the branch wrote there, its text) for each test file of the lane it changed.

    Shared with `kept_out` so the report and the plan do not come to answer over different files: a
    case named as kept out while the plan posed it would be advice to sharpen a case about to run.
    """
    for relative, lines in sorted(by_file.items()):
        if kind_of(relative) is None or (lane != "both" and kind_of(relative) != lane):
            continue
        source = project / relative
        if source.exists():
            yield relative, lines, source.read_text()


def kept_out(project, since, lane):
    """Path -> how many of its cases a line reading gave the branch and a reading of their code took.

    Without this, a run that plans nothing prints the same lines whether the branch changed no test
    file at all or rewrote a fixture's remarks end to end -- two states an author reads differently,
    and nothing else in the output separates them.
    """
    found = {}
    for relative, lines, text in changed_test_files(project,
                                                    changed_lines_by_file(project, since), lane):
        aside = authored(relative, touched(cases_in(relative, text), lines),
                         held_at(project, since, relative), text)[1]
        if aside:
            found[relative] = len(aside)
    return found


def collect(project, base, lane):
    """(merge base, changed cases, control cases, shared lines by file, carried helpers) for a branch."""
    since = merge_base(project, base)
    heirs = concrete_heirs(corpus_of(project))
    by_file = changed_lines_by_file(project, since)
    # A helper this branch rewrote is carried onto the base tree, so no case that calls it is running
    # the base's own text any more. Its verdict is still worth taking -- a case going red because the
    # branch sharpened what it shares is the strongest evidence this check can be handed -- but it is
    # no longer a reading of the tree, and `canary_fixtures` reads the tree by the base's own files.
    shared_helper = sorted(name for name in by_file
                           if kind_of(name) is None and is_test_side(name))
    changed, control, shared = [], [], {}
    for relative, lines, text in changed_test_files(project, by_file, lane):
        cases = cases_in(relative, text)
        for case in cases:
            if case.declaration is not None and lines is not None:
                case.declaration.written_here = case.declaration.written_in(lines)
        wanted = {case.name for case in authored(relative, touched(cases, lines),
                                                 held_at(project, since, relative), text)[0]}
        changed.extend(as_the_runner_names_them(
            [case for case in cases if case.name in wanted], heirs))
        loose = outside(cases, lines)
        if not loose and not shared_helper:
            control.extend(as_the_runner_names_them(
                [case for case in cases if case.name not in wanted], heirs))
        if loose:
            shared[relative] = len(loose)
    return since, changed, control, shared, shared_helper


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    parser.add_argument("--base", default="origin/main", help="what to take a merge base with")
    parser.add_argument("--lane", choices=["python", "csharp", "both"], default="both",
                        help="which test surface to measure (default: both)")
    parser.add_argument("--platform", choices=["EditMode", "PlayMode"], action="append",
                        help="restrict the C# lane to these platforms")
    parser.add_argument("--warm-library", help="a Library directory to clone into the base tree")
    parser.add_argument("--base-tree", help="build the base tree here and leave it behind")
    parser.add_argument("--unity", default=DEFAULT_UNITY, help="editor binary")
    parser.add_argument("--timeout", type=int, default=3600, help="seconds before a base run is killed")
    parser.add_argument("--busy-timeout", type=int, default=1800,
                        help="seconds to wait for another Unity run to finish")
    parser.add_argument("--max-rounds", type=int, default=8,
                        help="uncompilable files to withdraw before the C# lane gives up (default: 8)")
    parser.add_argument("--plan", action="store_true", help="print the cases and exit without running")
    parser.add_argument("--emit", help="build the base tree, write the reading here and stop, for a "
                                       "runner that reaches Unity through something other than the "
                                       "editor binary. Needs --base-tree")
    parser.add_argument("--verdict", help="decide from an --emit reading and a results file or "
                                          "directory, without building or running anything")
    parser.add_argument("--results", help="what --verdict reads the run from")
    parser.add_argument("--output", default="", help="directory for the base run's logs and results")
    args = parser.parse_args()
    speak_under_a_pipe()

    if args.verdict:
        plan = json.loads(Path(args.verdict).read_text())
        if not plan["cases"]:
            print("no changed test case in the reading", flush=True)
            return 0
        if not args.results:
            raise SystemExit("--verdict needs --results")
        reported, wrote = results_from(args.results)
        if not wrote:
            print("the base run wrote no result, so nothing it was asked was measured", flush=True)
        return 1 if report(from_plan(plan["cases"]), from_plan(plan["control"]), reported,
                           plan["canaries"], wrote, plan.get("withdrawn"),
                           plan.get("since")) else 0

    project = Path(args.project).resolve()
    since, cases, control, shared, shared_helper = collect(project, args.base, args.lane)
    print("merge base {}".format(since[:12]), flush=True)
    for relative, count in sorted(kept_out(project, since, args.lane).items()):
        print("  out of scope: {} case(s) of {} hold a line this branch changed and no code it "
              "changed".format(count, relative), flush=True)
    if not cases:
        print("no changed test case in scope of --lane {}".format(args.lane), flush=True)
        return 0

    for case in cases:
        print("  {}{}".format(case.name,
                              " [{}]".format(case.declaration.category)
                              if case.declaration and case.declaration.written_here else ""), flush=True)
    print("  ({} control case(s) alongside)".format(len(control)), flush=True)
    for relative, count in sorted(shared.items()):
        print("  no control: {} line(s) of {} sit in no case -- a SetUp, a field, a helper, a using. "
              "The\n              cases beside them are this branch's text too, so none of them reads "
              "the base tree.".format(count, relative), flush=True)
    for relative in shared_helper:
        print("  no control: {} is carried onto the base, so every case that calls it is this "
              "branch's\n              text there. The base's own fixtures are what read the tree."
              .format(relative), flush=True)
    for relative in sorted({case.path for case in cases}):
        source = project / relative
        if not source.exists():
            continue
        written, carried = orphaned_declarations(relative, source.read_text())
        if written > carried:
            print("  orphaned: {} of {} declaration(s) in {} sit above no case, so nothing reads "
                  "them".format(written - carried, written, relative), flush=True)
    if args.plan:
        return 0

    changed = changed_lines_by_file(project, since)
    carry = sorted(name for name in changed if is_test_side(name) and (project / name).exists())
    drop = sorted(name for name in deleted_files(project, since) if is_test_side(name))

    if args.emit:
        if not args.base_tree:
            raise SystemExit("--emit needs --base-tree to say where the base is built")
        base_tree = Path(args.base_tree).resolve()
        print("  building the base tree at {}".format(base_tree), flush=True)
        build_base_tree(project, since, base_tree, carry, drop, args.warm_library)
        # Before the run rather than after it, because there is no after: one round of a C# compile
        # failure is an empty artifacts directory, and every fixture in the tree is behind it.
        unbuildable = unbuildable_on_base(project, since, base_tree, carry)
        for relative, name in sorted(unbuildable.items()):
            withdraw(base_tree, relative)
            print("  withdrawn: {} spells {}, which the base has not got, so the base builds no "
                  "fixture\n             of its assembly and the run would report none of them"
                  .format(relative, name), flush=True)
        canaries = canaries_for(base_tree, cases, carry, args.platform)
        Path(args.emit).write_text(
            json.dumps(as_plan(since, cases, control, shared, canaries, unbuildable), indent=2))
        # The fixtures, not the cases: a run over the fixture brings the control cases with it, and
        # they are what says whether the tree it built can answer at all. A withdrawn file's is left
        # out because `report` decides its cases without reading any of them -- what stands in its
        # place is the base's own text under the same fixture name, so the round would be spent on a
        # result nothing consults.
        wanted = {case.fixture for case in cases + control if kind_of(case.path) == "csharp"
                  and case.path not in unbuildable
                  and (not args.platform or platform_of(case.path) in args.platform)}
        for chosen in canaries.values():
            wanted.update(chosen)
        print("fixtures={}".format(";".join(sorted(wanted))), flush=True)
        return 0

    holder = None
    if args.base_tree:
        base_tree = Path(args.base_tree).resolve()
    else:
        holder = tempfile.mkdtemp(prefix="base-red-")
        base_tree = Path(holder) / "tree"
    output = Path(args.output).resolve() if args.output else project / "Logs" / "base_red_check"
    output.mkdir(parents=True, exist_ok=True)

    reported = {}
    transcript = []
    canaries = {}
    ever_wrote = not any(kind_of(case.path) == "csharp" for case in cases)
    rounds_spent = 0
    put_back = set()
    try:
        print("  building the base tree at {}".format(base_tree), flush=True)
        build_base_tree(project, since, base_tree, carry, drop, args.warm_library)

        python_lane = [case for case in cases + control if kind_of(case.path) == "python"]
        if python_lane:
            guards = python_canaries(base_tree, carry)
            canaries[PYTHON_LANE] = [case.fixture for case in guards]
            print("  running {} Python case(s) there, one process each".format(
                len(python_lane) + len(guards)), flush=True)
            reported.update(run_python(base_tree, project, python_lane + guards, transcript))

        derived = sorted({platform_of(case.path) for case in cases
                          if kind_of(case.path) == "csharp"})
        platforms = [name for name in derived if name in args.platform] if args.platform else derived
        if asked_about_nothing(derived, args.platform):
            print("No changed case is on {}; the ones this branch wrote are {}.".format(
                ", ".join(sorted(args.platform)), ", ".join(derived)))
            print("Nothing was asked here, so nothing could answer. Run the lane those cases are on.")
            return 0
        if platforms and not wait_for_quiet(args.busy_timeout):
            raise SystemExit("another Unity test run is still in flight")
        canaries.update(canaries_for(base_tree, cases, carry, platforms))
        for platform in platforms:
            # Accumulated across platforms, unlike `withdrawn`: what the message below is evidence
            # for is that the base built none of the carried set, which both lanes are asking about.
            wanted = [case for case in cases + control
                      if kind_of(case.path) == "csharp" and platform_of(case.path) == platform]
            withdrawn = set()
            for attempt in range(1, args.max_rounds + 1):
                rounds_spent = max(rounds_spent, attempt)
                put_back |= withdrawn
                live = [case for case in wanted if case.path not in withdrawn]
                fixtures = sorted({case.fixture for case in live} | set(canaries[platform]))
                if not fixtures:
                    break
                results = output / "{}-{}.xml".format(platform, attempt)
                log = output / "{}-{}.log".format(platform, attempt)
                for stale in (results, log):
                    if stale.exists():
                        stale.unlink()
                wall = run_unity(args.unity, base_tree, platform, fixtures, results, log, args.timeout)
                seen, wrote = results_from(results)
                ever_wrote = ever_wrote or wrote
                reported.update(seen)
                print("{} attempt {}: {} case(s) over {} fixture(s) in {:.0f}s".format(
                    platform, attempt, len(seen), len(fixtures), wall), flush=True)

                # One file per attempt. A round that reports nothing says only that something the
                # tree holds did not build, never which file, so withdrawing every silent one at
                # once would take out the files that were merely standing next to the offender and
                # leave their cases unmeasured behind somebody else's error.
                ran = fixtures_that_ran(seen)
                silent = sorted({case.path for case in live if case.fixture not in ran})
                if not silent:
                    break
                offender = next_to_withdraw(
                    log.read_text(errors="replace") if log.exists() else "",
                    carry, withdrawn, silent)
                withdraw(base_tree, offender)
                withdrawn.add(offender)
    finally:
        if holder is not None:
            git(project, "worktree", "remove", "--force", str(base_tree), check=False)
            shutil.rmtree(holder, ignore_errors=True)

    if transcript:
        (output / "python.log").write_text("\n".join(transcript))

    if not ever_wrote:
        print("no round wrote a result, so nothing any of them was asked was measured", flush=True)
        if rounds_spent >= args.max_rounds:
            print(exhausted_reason(rounds_spent, put_back, len(carry)), flush=True)
    offenders = report(cases, control, reported, canaries, ever_wrote)
    print("\nlogs: {}".format(output), flush=True)
    return 1 if offenders else 0


if __name__ == "__main__":
    sys.exit(main())
