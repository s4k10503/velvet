#!/usr/bin/env python3
"""Refuse a mutation of this branch's changed lines that no test failed on and nobody answered for.

A test that asserts nothing passes whether or not the code under test works, and the suite is
green either way, so nothing in CI can see it. Mutating the code the branch touched and rerunning
the suite is the only check that asks the question directly: change the behaviour, and if the
suite still passes, no test was measuring it.

A mutant surviving is a question, and a run that only prints the question leaves answering it to
whoever feels like it. So a survivor either goes away -- the test that should have noticed gets
written -- or it is answered above the line it lives on, with a reason a reviewer can disagree with:

    // MUTANT_SURVIVES(equivalent): both spellings clamp to the same bound, so nothing can differ.

A declaration answers for the change written under it, so it is read the three ways `base_red_check.py`
reads `GREEN_ON_BASE`: one over a statement whose mutants all died is stale and fails, one whose category
or reason is malformed fails, and one the branch did not itself write answers for a change the base
already carries rather than for this one.

**Success means every mutant was measured, and every survivor answered for.** Not that nothing was
reported: most of what goes wrong with a campaign ends in a mutant nobody asked about, and a mutant
nobody asked about must never be a pass. So a run fails or stops rather than pass over one, and
Generators~/README.md ▸ The Unity assemblies says when it does which.

It is not success over the whole change, and the difference is most of one: the operators reach a
minority of the code lines a branch touches, so the reach is printed beside every verdict rather than
folded into it, and a change nothing reaches at all refuses.

The default scope is the whole platform suite rather than the fixtures nearest the mutated file,
so that nothing is reported as surviving merely because the fixture that would have killed it was
out of scope. A mutant in a cheap area is asked of that area's assemblies first, and only a kill
there stands; every other verdict comes from the whole suite.

A campaign holds a mutation in the working tree while the suite runs, so it records what it holds
before writing it and clears that record only after putting the original back. Nothing else in the
tree says a campaign is running: the mutation is a plausible one-line change in a file the branch is
already touching, and two of them reached a commit that way.
"""

import argparse
import bisect
import fcntl
import functools
import hashlib
import json
import os
import re
import signal
import subprocess
import sys
import threading
import time
import xml.etree.ElementTree as ET
from pathlib import Path

DEFAULT_UNITY = "/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity"
# Anchored at the editor binary so that a shell waiting on this pattern does not match itself and
# report a busy machine forever on an idle one.
UNITY_RUNNING = "^/Applications/.*/MacOS/Unity -runTests"

# At the project root and not in .gitignore, so `git status` names it beside the file it explains.
# Under Logs/ it would be correct and unread: what a resumed session looks at is `git status`, and a
# mutation there reads as an interrupted implementation.
SENTINEL = "MUTATION_IN_PROGRESS.json"

PACKAGE = "Packages/com.velvet.core"
REFUSAL_BASELINE = "scripts/test_quality/logic_refusal_baseline.txt"

KILLED = "killed"
TIMED_OUT = "not measured (timed out)"
HUNG = "killed (the suite hung past --timeout)"

HANG_MARGIN = 3
SURVIVED = "survived"
INCONCLUSIVE = "survived (inconclusive)"
UNCOMPILABLE = "uncompilable"
# Neither a survivor nor unmeasured: there was no mutated program to measure. `operator_does_not_apply`
# says when a build failure is this rather than UNCOMPILABLE.
INAPPLICABLE = "not a mutant (the operator does not apply)"
NOT_BUILT = "not rebuilt"
UNRECORDED = "not measured (no shard recorded it)"
LOCKED = "not measured (the project lock was held)"

SURVIVING = (SURVIVED, INCONCLUSIVE)
# The verdicts a decision can pass, besides a survivor a declaration answers. Every other verdict,
# however it is spelled, fails the decision as one nothing measured.
DECIDED = (KILLED, HUNG, INAPPLICABLE)
OTHER_PLATFORM = {"EditMode": "PlayMode", "PlayMode": "EditMode"}

# How `--plan` splits a pass across CI jobs, per platform. Each shard pays an image pull, a licence
# activation and a baseline before its first mutant; CONTRIBUTING.md ▸ Checking that the tests can fail
# has the measured cost of each.
SHARD_SIZE = {"EditMode": 6, "PlayMode": 2}
MAX_SHARDS = 10
# The most a shard is given before `--plan` refuses: this many mutants at the cost `ShardCeilingTests`
# charges one on its platform, after the longest of each setup phase and, where the platform is
# narrowed, an area's own launch for each area, fit that platform's shard job's timeout in mutation.yml,
# and that case holds each pair together. The charge is not a worst case -- the EditMode one is a mutant
# 18 of 645 measured runs exceeded -- so a full shard holding a slower mutant or a hang can still
# outrun the job, and the mutants it had not reached are then recorded by no shard.
SHARD_CEILING = {"EditMode": 16, "PlayMode": 10}
# What `--plan` exits with over that ceiling, apart from 1, so the workflow can let it through where
# no licence means no shard would run.
CEILING_REFUSAL = 4

# A mutant under an area whose own test assemblies took at most 1/NARROW_SHARE of the baseline's test
# time runs against those assemblies first, and a kill there is its verdict. Anything else is measured
# again on the whole suite, so the narrowed run can end a mutant early and never decides that it
# survived.
NARROW_SHARE = 4
# A platform absent here is not narrowed. PlayMode is absent: its pass measures only what the whole
# EditMode suite left surviving, and no PlayMode area's own launch has been measured for
# `ShardCeilingTests` to charge.
NARROWED_PLATFORMS = ("EditMode",)
# A mutant's narrowed launch is killed, and the whole suite decides instead, once it has taken this
# many times its area's own launch on the unmutated tree -- which runs on the tree the whole baseline
# built, where a mutant's launch compiles its mutation first. Never later than the whole baseline took,
# since the whole suite answers everything the narrowed launch can.
NARROW_MARGIN = 1.5

# Where an editor holds its project, and what it prints when it finds another editor there.
LOCKFILE = Path("Temp") / "UnityLockfile"
LOCK_REFUSAL = "another Unity instance is running with this project open"
# How many times a launch the editor refused for the lock is made, and how long before each one the
# lock is waited on. The wait ends early once the lock can be taken, and a launch follows it either
# way, so what decides that the lock was held is the editor's refusal and not this reading of it.
LOCK_ATTEMPTS = 3
LOCK_WAIT = 60
# The last line `run_suite` writes into a log whose every launch was refused.
LOCK_REFUSED_LINE = "mutation_check: the editor refused every launch for the project lock"

CATEGORIES = ("equivalent", "unreachable")

# Four words, for the reason base_red_check.py's own floor gives.
MINIMUM_REASON_WORDS = 4

# The category, and optionally the operator it answers for. A line carries more than one mutant --
# `if (depth > cap) Reset();` carries a boundary flip and a void-call removal -- and a declaration
# keyed by line alone answers for every one of them. Measured: 4 of this package's 10 declarations sit
# on such a line, one of them over five mutants. Where only one of them survives today the
# declaration is unambiguous now and widens the moment a sibling starts surviving, and the staleness
# guard cannot report that: it asks whether the LINE still produces a survivor, and the one already
# answered for keeps it non-stale.
DECLARATION = re.compile(
    r"MUTANT_SURVIVES\(([A-Za-z]*)(?:\s*,\s*([^)]*?))?\)\s*:\s*(.*)")

# How many unreached line numbers a file lists before the rest become a count. The count stays exact
# either way; what this bounds is a whole-file `--files` run printing several hundred of them.
LINES_LISTED = 25

# A record exists and cannot be read. Every reader treats it as a campaign holding something whose
# name is unavailable, which is the only reading that does not let a mutation through.
UNREADABLE = object()

# What `--carried` exits with when it refuses, so a caller can tell a refusal from a script that
# could not take the reading at all. Both stop the tool; only one is about a campaign.
CARRIED_REFUSAL = 3

# Include generation semantics in verdict identity.
MUTATION_MODEL_VERSION = 7


class Mutant:
    def __init__(self, path, line, column, before, after, operator):
        self.path = path
        self.line = line
        self.column = column
        self.before = before
        self.after = after
        self.operator = operator
        self.verdict = None
        self.detail = ""

    def describe(self, project):
        try:
            where = self.path.relative_to(project)
        except ValueError:
            where = self.path
        return "{}:{} {} -> {} ({})".format(where, self.line, self.before, self.after, self.operator)


def folded_reason(tail, wrapped):
    """(the marker line's own claim, that claim with the comment lines under it folded onto it).

    The reason spans the block so that a branch which rewrote only its wrapped half wrote the
    declaration. The floor is measured on the claim rather than on that span, over which a comment
    line that is not the reason at all would count toward it.
    """
    folded = [tail] + [line.strip().lstrip("/#") for line in wrapped]
    return " ".join(tail.split()), " ".join(" ".join(folded).split())


class Declaration:
    def __init__(self, category, reason, line, claim=None, through=None, written_here=True,
                 operator=None):
        self.category = category
        # Which operator's mutant this answers for, or None for every one on its line. Named where
        # the line carries more than one, so an answer written for the removal there does not sign
        # off the boundary flip beside it the day that starts surviving.
        self.operator = operator
        self.reason = reason
        self.claim = reason if claim is None else claim
        self.line = line
        self.through = line if through is None else through
        # Whether the branch wrote it, for the reason base_red_check.py's own field carries.
        self.written_here = written_here

    def written_in(self, lines):
        """Whether the branch wrote any line of the span `folded_reason` reads the reason over."""
        return any(number in lines for number in range(self.line, self.through + 1))

    @property
    def complaint(self):
        if self.category not in CATEGORIES:
            return "category {!r} is not one of {}".format(self.category, ", ".join(CATEGORIES))
        if len(self.claim.split()) < MINIMUM_REASON_WORDS:
            return "the reason's first line is under {} words".format(MINIMUM_REASON_WORDS)
        return None

    def __repr__(self):
        return "Declaration({!r}, line {})".format(self.category, self.line)


def comment_spans(text):
    """(start, end) for each span `mask_spans` read as a comment."""
    return [(start, end) for start, end, kind in mask_spans(text)
            if kind in (LINE_COMMENT, BLOCK_COMMENT)]


def declared_lines(text, marker, spans):
    """1-based line -> the `marker` match that opens inside one of `spans`, for each line holding one.

    A string literal is what this rules out. A marker there is the material of whatever asserts over
    the declaration syntax, and adopting it as an answer for the statement beneath silences that
    statement instead of reporting it.

    Membership is the match's own offset rather than its line's, because one line can carry a literal
    and a comment at once -- a verbatim string closing above a trailing remark is the shape in this
    repository's own fixtures. Asked by the line, both the reading and the count that exists to catch
    a lost declaration accept such a marker, so the file balances and nothing reports it.
    """
    starts = [start for start, _ in line_spans(text)]
    inside = within(spans)
    found = {}
    for match in marker.finditer(text):
        if inside(match.start()):
            found.setdefault(bisect.bisect_right(starts, match.start()), match)
    return found


def within(spans):
    """Whether an offset sits inside any of the (start, end) `spans`, asked by bisecting their union."""
    union = []
    for opened, closed in sorted(spans):
        if closed <= opened:
            continue
        if union and opened <= union[-1][1]:
            union[-1][1] = max(union[-1][1], closed)
        else:
            union.append([opened, closed])
    starts = [opened for opened, _ in union]

    def inside(offset):
        position = bisect.bisect_right(starts, offset) - 1
        return position >= 0 and offset < union[position][1]
    return inside


def comment_lines(text, spans):
    """The 1-based lines whose first non-space character sits inside one of `spans`.

    Which lines are prose rather than which hold a marker, so a block comment's continuation counts
    and a remark trailing a statement does not.
    """
    inside = within(spans)
    found = set()
    for number, (start, end) in enumerate(line_spans(text), start=1):
        line = text[start:end]
        if not line.strip():
            continue
        head = start + len(line) - len(line.lstrip())
        if inside(head):
            found.add(number)
    return found


def declarations_in(text):
    """(a line it answers for, the declaration) for every one in a file, once per line it covers.

    A declaration answers for the statement under it, reached past any further comment lines of the
    same block. A blank line ends the block: prose further up belongs to whatever sits under it, and
    reaching past the gap would let one line's answer cover a neighbour nobody wrote it for.

    The statement rather than the line, because a condition spread over two lines carries mutants on
    both -- `if (a == null ||` and `b <= 0)` -- and one declaration over it would answer for the first
    only, leaving the same survivor UNANSWERED on one line and the declaration STALE on the other.
    """
    lines = text.splitlines()
    mask = code_mask(text)
    spans = line_spans(text)
    declared = declared_lines(text, DECLARATION, comment_spans(text))

    def seen(number):
        start, end = spans[number]
        return "".join(text[offset] for offset in range(start, end) if mask[offset])

    found = []
    for index in range(len(lines)):
        match = declared.get(index + 1)
        if not match:
            continue
        subject = index + 1
        while subject < len(lines) and lines[subject].strip() and not seen(subject).strip():
            subject += 1
        if subject >= len(lines) or not lines[subject].strip():
            continue
        claim, reason = folded_reason(match.group(3), lines[index + 1:subject])
        named = (match.group(2) or "").strip() or None
        declaration = Declaration(match.group(1), reason, line=index + 1, claim=claim,
                                  through=subject, operator=named)
        # Extends while the statement's own parentheses are still open, which is what a condition
        # broken across lines leaves and what a finished statement does not.
        depth = 0
        last = subject
        while last < len(lines):
            depth += seen(last).count("(") - seen(last).count(")")
            if depth <= 0:
                break
            last += 1
        for number in range(subject, min(last, len(lines) - 1) + 1):
            found.append((number + 1, declaration))
    return found


# The furthest offset from an opening quote at which a closing one can still sit: `'\U0001F600'` is
# the longest character literal C# can spell.
CHARACTER_LITERAL_REACH = len("'\\U0001F600'") - 1


DIRECTIVE = "preprocessor directive"
LINE_COMMENT = "line comment"
BLOCK_COMMENT = "block comment"
STRING = "string literal"
VERBATIM = "verbatim string literal"
CHARACTER = "character literal"

# The four `mask_spans` below reads as ending on the line they open on. A span of one of them that
# reaches the next line is therefore the scanner having read something as a construct it is not, and
# every offset it covers is blanked out of the mask -- which generates no mutant there and reports
# nothing. A raw string literal is the shape that would land here legitimately; the scanner does not
# read one, so refusing is the answer rather than trusting the mask over that file.
SINGLE_LINE_CONSTRUCTS = (DIRECTIVE, LINE_COMMENT, STRING, CHARACTER)

MASK_OPENER = re.compile(r"[#/\"'@$]")


def mask_spans(text):
    """(start, end, kind) for the spans this reads as something other than code.

    Mutating a comment or a string literal produces a mutant that cannot change behaviour, and
    each one still costs a full compile-and-run cycle. Whether the reading is right about a file is
    what `mask_defects` puts a floor under; a raw string literal is a shape it does not read at all.
    """
    spans = []
    i = 0
    n = len(text)
    while i < n:
        # Every branch below opens on one of these characters, so the offsets between are code.
        found = MASK_OPENER.search(text, i)
        if found is None:
            break
        i = found.start()
        two = text[i:i + 2]
        if text[i] == "#" and not text[text.rfind("\n", 0, i) + 1:i].strip():
            # A preprocessor line is blanked whole. Nothing downstream of this mask reads a directive,
            # and none of them can hold a brace or a type; what one can hold is `#region Boundary's
            # own tree`, whose apostrophe opens a character literal against the rule below.
            end = text.find("\n", i)
            end = n if end < 0 else end
            spans.append((i, end, DIRECTIVE))
            i = end
        elif two == "//":
            end = text.find("\n", i)
            end = n if end < 0 else end
            spans.append((i, end, LINE_COMMENT))
            i = end
        elif two == "/*":
            end = text.find("*/", i + 2)
            end = n if end < 0 else end + 2
            spans.append((i, end, BLOCK_COMMENT))
            i = end
        elif text[i] == '"' or two in ('@"', '$"') or text[i:i + 3] == '$@"':
            start = i
            while i < n and text[i] != '"':
                i += 1
            verbatim = "@" in text[start:i]
            i += 1
            while i < n:
                if text[i] == "\\" and not verbatim:
                    i += 2
                    continue
                if text[i] == '"':
                    if verbatim and text[i + 1:i + 2] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            spans.append((start, min(i, n), VERBATIM if verbatim else STRING))
        elif text[i] == "'":
            # An apostrophe with no closing one inside a literal's reach is not a literal. Consuming
            # to the next one anywhere in the file instead blanks arbitrary code, and nothing after
            # this reports a wrongly blanked offset: no mutant is generated there and no brace
            # counted, so both halves come back looking like a file with less in it than it has.
            start = i
            i += 1
            while i < n and i - start <= CHARACTER_LITERAL_REACH and text[i] != "'":
                i += 2 if text[i] == "\\" else 1
            if i >= n or i - start > CHARACTER_LITERAL_REACH or text[i] != "'":
                i = start + 1
                continue
            i += 1
            spans.append((start, min(i, n), CHARACTER))
        else:
            i += 1
    return spans


def code_mask(text, spans=None):
    """True at each offset `mask_spans` did not read as a comment, a literal or a directive."""
    mask = [True] * len(text)
    for start, end, _ in mask_spans(text) if spans is None else spans:
        mask[start:end] = [False] * max(0, min(end, len(text)) - start)
    return mask


def mask_defects(text):
    """(first line, last line, kind) for every span blanked through a construct that ends on its own line.

    What this catches is the mask reading something as a construct it is not. It cannot see the
    converse -- code read as code that the compiler treats otherwise -- so it is a floor rather than a
    proof that the mask is right about a file.
    """
    starts = [start for start, _ in line_spans(text)]
    defects = []
    for start, end, kind in mask_spans(text):
        if kind in SINGLE_LINE_CONSTRUCTS and "\n" in text[start:end]:
            first = sum(1 for offset in starts if offset <= start)
            last = sum(1 for offset in starts if offset < end)
            defects.append((first, last, kind))
    return defects


# Spacing is what separates a comparison from a generic argument list and a binary operator from
# its unary or compound-assignment spelling, so every operator below carries its own spaces.
OPERATORS = [
    (" <= ", " < ", "boundary"),
    (" >= ", " > ", "boundary"),
    (" < ", " <= ", "boundary"),
    (" > ", " >= ", "boundary"),
    (" == ", " != ", "equality"),
    (" != ", " == ", "equality"),
    (" && ", " || ", "logic"),
    (" || ", " && ", "logic"),
    (" + ", " - ", "arithmetic"),
    (" - ", " + ", "arithmetic"),
]

WORD_OPERATORS = [("true", "false", "literal"), ("false", "true", "literal")]

def comment_edges(spans):
    """Comment starts and ends, kept separate from the literal spans in the same mask."""
    comments = [(start, end) for start, end, kind in spans
                if kind in (LINE_COMMENT, BLOCK_COMMENT)]
    return ({start: end for start, end in comments}, {end: start for start, end in comments})


def skip_trivia_right(text, index, comments):
    """The next operand offset after whitespace and comments."""
    starts, _ = comments
    while index < len(text):
        while index < len(text) and text[index].isspace():
            index += 1
        end = starts.get(index)
        if end is None:
            break
        index = end
    return index


def skip_trivia_left(text, index, comments):
    """The exclusive end of the prior operand before whitespace and comments."""
    _, ends = comments
    while index > 0:
        while index > 0 and text[index - 1].isspace():
            index -= 1
        start = ends.get(index)
        if start is None:
            break
        index = start
    return index


def skip_null_forgiving_left(text, index, comments):
    """The prior operand's end before any null-forgiving postfixes."""
    index = skip_trivia_left(text, index, comments)
    while index > 0 and text[index - 1] == "!":
        index = skip_trivia_left(text, index - 1, comments)
    return index


def character_literal_end(text, index):
    """The exclusive end of a character token at `index`, or None when it is incomplete."""
    cursor = index + 1
    while cursor < len(text) and text[cursor] != "\n":
        if text[cursor] == "\\":
            cursor += 2
            continue
        if text[cursor] == "'":
            return cursor + 1
        cursor += 1
    return None


def starts_string_literal(text, index):
    """Whether `index` has one of the string prefixes this bounded reader supports."""
    if text.startswith(('"', '$"', '@"', '$@"', '@$"'), index):
        return True
    dollars = index
    while dollars < len(text) and text[dollars] == "$":
        dollars += 1
    return dollars > index and text.startswith('"""', dollars)


def interpolation_hole_end(text, index, closing_width=1):
    """The offset after a hole's closing brace, skipping nested lexical constructs."""
    depth = 0
    cursor = index
    while cursor < len(text):
        if text.startswith("//", cursor):
            newline = text.find("\n", cursor + 2)
            if newline < 0:
                return None
            cursor = newline + 1
            continue
        if text.startswith("/*", cursor):
            closed = text.find("*/", cursor + 2)
            if closed < 0:
                return None
            cursor = closed + 2
            continue
        if starts_string_literal(text, cursor):
            closed = string_literal_end(text, cursor)
            if closed is None:
                return None
            cursor = closed
            continue
        if text[cursor] == "'":
            closed = character_literal_end(text, cursor)
            if closed is None:
                return None
            cursor = closed
            continue
        if text[cursor] == "{":
            depth += 1
        elif text[cursor] == "}":
            if depth == 0:
                run_end = cursor + 1
                while run_end < len(text) and text[run_end] == "}":
                    run_end += 1
                run_width = run_end - cursor
                if closing_width <= run_width < 2 * closing_width:
                    return cursor + closing_width
                return None
            depth -= 1
        cursor += 1
    return None


def raw_string_literal_end(text, index, delimiter, interpolation_width):
    """The exclusive outer delimiter end, with interpolation holes skipped."""
    cursor = index
    while cursor < len(text):
        if interpolation_width > 0 and text[cursor] == "{":
            run_end = cursor + 1
            while run_end < len(text) and text[run_end] == "{":
                run_end += 1
            run_width = run_end - cursor
            if run_width < interpolation_width:
                cursor = run_end
                continue
            if run_width >= 2 * interpolation_width:
                return None
            closed = interpolation_hole_end(
                text, run_end, interpolation_width)
            if closed is None:
                return None
            cursor = closed
            continue
        if text.startswith('"' * delimiter, cursor):
            return cursor + delimiter
        cursor += 1
    return None


def string_literal_end(text, index):
    """The exclusive end of a string token whose spelling fixes its type, or None."""
    dollars = index
    while dollars < len(text) and text[dollars] == "$":
        dollars += 1
    quotes = dollars
    while quotes < len(text) and text[quotes] == '"':
        quotes += 1
    delimiter = quotes - dollars
    if delimiter >= 3:
        return raw_string_literal_end(text, quotes, delimiter, dollars - index)

    if text.startswith(("$@\"", "@$\""), index):
        verbatim = True
        interpolated = True
        cursor = index + 3
    elif text.startswith('@"', index):
        verbatim = True
        interpolated = False
        cursor = index + 2
    elif text.startswith('$"', index):
        verbatim = False
        interpolated = True
        cursor = index + 2
    elif text.startswith('"', index):
        verbatim = False
        interpolated = False
        cursor = index + 1
    else:
        return None

    while cursor < len(text):
        if not verbatim and text[cursor] == "\\":
            cursor += 2
            continue
        if text[cursor] == '"':
            if verbatim and text[cursor + 1:cursor + 2] == '"':
                cursor += 2
                continue
            return cursor + 1
        if interpolated and text[cursor] == "{":
            if text[cursor + 1:cursor + 2] == "{":
                cursor += 2
                continue
            closed = interpolation_hole_end(text, cursor + 1)
            if closed is None:
                return None
            cursor = closed
            continue
        if interpolated and text[cursor] == "}":
            if text[cursor + 1:cursor + 2] != "}":
                return None
            cursor += 2
            continue
        cursor += 1
    return None


def matching_open_parenthesis(text, mask, close):
    depth = 0
    for offset in range(close, -1, -1):
        if not mask[offset]:
            continue
        if text[offset] == ")":
            depth += 1
        elif text[offset] == "(":
            depth -= 1
            if depth == 0:
                return offset
    return None


def matching_close_parenthesis(text, mask, opened, limit):
    depth = 0
    for offset in range(opened, limit):
        if not mask[offset]:
            continue
        if text[offset] == "(":
            depth += 1
        elif text[offset] == ")":
            depth -= 1
            if depth == 0:
                return offset
    return None


def is_grouping_parenthesis(text, opened, comments):
    """Whether an opening parenthesis groups an operand rather than calling the token before it."""
    before = skip_trivia_left(text, opened, comments)
    if before == 0:
        return True
    previous = text[before - 1]
    if previous.isalnum() or previous == "_":
        start = before - 1
        while start > 0 and (text[start - 1].isalnum() or text[start - 1] == "_"):
            start -= 1
        return text[start:before] in ("return", "throw", "yield")
    if previous == ">":
        return before >= 2 and text[before - 2:before] == "=>"
    return previous not in ").]"


def top_level_addition(text, mask, start, end):
    """The last ` + ` outside a nested group in the bounded expression."""
    round_depth = square_depth = brace_depth = 0
    found = None
    offset = start
    while offset < end:
        if not mask[offset]:
            offset += 1
            continue
        character = text[offset]
        if character == "(":
            round_depth += 1
        elif character == ")":
            round_depth -= 1
        elif character == "[":
            square_depth += 1
        elif character == "]":
            square_depth -= 1
        elif character == "{":
            brace_depth += 1
        elif character == "}":
            brace_depth -= 1
        elif (round_depth == square_depth == brace_depth == 0
              and text.startswith(" + ", offset)
              and offset + 3 <= end
              and all(mask[offset:offset + 3])):
            found = offset
            offset += 2
        offset += 1
    return found


def top_level_conditional(text, mask, start, end):
    """The question and matching colon of the bounded expression's outer conditional."""
    round_depth = square_depth = brace_depth = 0
    question = None
    nested = 0
    for offset in range(start, end):
        if not mask[offset]:
            continue
        character = text[offset]
        if character == "(":
            round_depth += 1
        elif character == ")":
            round_depth -= 1
        elif character == "[":
            square_depth += 1
        elif character == "]":
            square_depth -= 1
        elif character == "{":
            brace_depth += 1
        elif character == "}":
            brace_depth -= 1
        elif round_depth == square_depth == brace_depth == 0 and character == "?":
            following = text[offset + 1:offset + 2]
            if following in ("?", ".", "[") or text[offset - 1:offset] == "?":
                continue
            if question is None:
                question = offset
            else:
                nested += 1
        elif (round_depth == square_depth == brace_depth == 0
              and character == ":" and question is not None):
            if nested:
                nested -= 1
            else:
                return question, offset
    return None


def preceding_addition(text, mask, index):
    """The prior ` + ` in this additive chain, or None at the expression boundary."""
    round_depth = square_depth = brace_depth = 0
    offset = index - 1
    while offset >= 0:
        if not mask[offset]:
            offset -= 1
            continue
        character = text[offset]
        if character == ")":
            round_depth += 1
        elif character == "]":
            square_depth += 1
        elif character == "}":
            brace_depth += 1
        elif character == "(":
            if round_depth == 0:
                return None
            round_depth -= 1
        elif character == "[":
            if square_depth == 0:
                return None
            square_depth -= 1
        elif character == "{":
            if brace_depth == 0:
                return None
            brace_depth -= 1

        if round_depth == square_depth == brace_depth == 0:
            candidate = offset - 2
            if (candidate >= 0 and text[candidate:candidate + 3] == " + "
                    and all(mask[candidate:candidate + 3])):
                return candidate
            if character in ";,=?:&|":
                return None
        offset -= 1
    return None


def bounded_expression_is_string(text, mask, start, end, comments):
    """Whether the bounded expression's text alone establishes a string result."""
    start = skip_trivia_right(text, start, comments)
    end = skip_trivia_left(text, end, comments)
    if start >= end:
        return False
    if string_literal_end(text, start) == end:
        return True
    if text[start] == "(":
        nested = matching_close_parenthesis(text, mask, start, end)
        if nested == end - 1:
            return bounded_expression_is_string(
                text, mask, start + 1, nested, comments)
    conditional = top_level_conditional(text, mask, start, end)
    if conditional is not None:
        question, colon = conditional
        return (bounded_expression_is_string(
                    text, mask, question + 1, colon, comments)
                and bounded_expression_is_string(
                    text, mask, colon + 1, end, comments))
    addition = top_level_addition(text, mask, start, end)
    return addition is not None and joins_a_string(
        text, mask, addition, comments)


def continues_with_postfix(text, end, comments):
    """Whether postfix syntax keeps computing the operand after the known string result."""
    following = skip_trivia_right(text, end, comments)
    while (text.startswith("!", following)
           and not text.startswith("!=", following)):
        following = skip_trivia_right(text, following + 1, comments)
    return text.startswith((".", "[", "("), following) or text.startswith(
        ("?.", "?["), following)


def joins_a_string(text, mask, index, comments):
    """Whether the ` + ` at `index` has a string literal for one of its operands.

    A prior addition in the same chain is an operand too: once one addition produced a string, every
    addition after it is string concatenation. Expression boundaries keep a string in another
    argument or statement from suppressing numeric arithmetic.
    """
    left = skip_null_forgiving_left(text, index, comments)
    right = skip_trivia_right(text, index + 3, comments)
    if left > 0 and text[left - 1] == '"':
        return True
    literal_end = string_literal_end(text, right)
    if literal_end is not None and not continues_with_postfix(text, literal_end, comments):
        return True
    if left > 0 and text[left - 1] == ")":
        opened = matching_open_parenthesis(text, mask, left - 1)
        if (opened is not None and is_grouping_parenthesis(text, opened, comments)
                and bounded_expression_is_string(
                    text, mask, opened + 1, left - 1, comments)):
            return True
    if right < len(text) and text[right] == "(":
        closed = matching_close_parenthesis(text, mask, right, len(text))
        if (closed is not None and not continues_with_postfix(text, closed + 1, comments)
                and bounded_expression_is_string(
                    text, mask, right + 1, closed, comments)):
            return True
    previous = preceding_addition(text, mask, index)
    return previous is not None and joins_a_string(
        text, mask, previous, comments)

# `while (true)`, which is how a method with no other returning path returns at all.
FOREVER_LOOP = re.compile(r"\bwhile\s*\(\s*true\s*$")

# An identifier, an optional type argument list, a parenthesised head, a semicolon-terminated tail.
# The word in front of the parenthesis is no part of that, so what the removal takes is everything the
# line runs rather than one call -- which is what its verdict is named for.
#
# The type arguments are read because `target.RegisterCallback<GeometryChangedEvent>(OnGeometry);` is
# a call whose deletion the tests should notice, and without them it matched nothing: 109 whole
# statements in this package passed every other reading and generated no mutant. The loss was silent,
# because a line no operator reaches is reported the same way as a line with nothing to mutate.
#
# The argument list is a character class rather than a balanced read: a nested generic is inside it,
# and what is deliberately not inside it is `;` or `(`, so a comparison -- `a < b && c > (d);` -- is
# not read as one.
REMOVABLE_LINE = re.compile(
    r"^[A-Za-z_][A-Za-z0-9_.]*(\.[A-Za-z_][A-Za-z0-9_]*)*"
    r"(<[A-Za-z0-9_.,<>\[\]?\s]*>)?"
    r"\s*\([^;]*\)\s*;$")

# `return (value, done);` has the shape above and is not a line whose code can go: what replaces it
# is an empty statement, so what the line returns goes with it. A word rather than a prefix, because
# `returns.Add(instr);` is a call whose name starts with one.
CONTROL_KEYWORD = re.compile(r"^(?:return|throw|yield)\b")

# Where a statement may begin. A line matching REMOVABLE_LINE is only removable when the code before
# it finished a statement: `=> Fragment(...)` and `= new(...)` both match the pattern and are the tail
# of a declaration, so deleting them leaves a member with no body. Measured with the C# parser over
# every mutant this package generates, that was 77 of them.
STATEMENT_BOUNDARY = (";", "{", "}", ":")

# Every operator above keeps the clause it lands in participating in the condition, so a clause no test
# reaches survives all of them: swapping its comparison or its join still leaves some test's own clause
# deciding the outcome. Removing the clause is the only mutation that asks whether anything depends on
# that condition existing, and the same holds one level out for a guard statement, whose condition the
# operators above mutate while none of them deletes the guard.
LOGIC_JOINS = (" && ", " || ")
GUARD_STATEMENT = re.compile(r"^if \(.+\)\s*(?:return[^;]*|continue|break);$")

# A removal that carries off a declaration leaves the reads of that name unresolved, so the mutant
# compiles nowhere and the campaign scores it unmeasured and fails. `var (state, setState) =
# UseState(...)` is the shape REMOVABLE_LINE reads as a removable line: `var` satisfies its leading
# identifier, and the argument list the pattern looks for runs from the deconstruction's `(` to the
# initializer's last `)`.
# Three spellings of C# rather than a reading of it -- a deconstruction, an `out` argument, a pattern
# variable. The `out` arm refuses the argument that declares nothing along with the one that does,
# because what a removal carries off at `out existingField` is the assignment rather than the
# declaration: the name survives the cut, and whether anything still writes it on the paths that read
# it is definite assignment, which a pattern over the text cannot decide. A discard and a member
# access are the two exemptions.
# The pattern arm reads the name directly behind `var`, behind one type token, or behind one closed
# brace or bracket group; a designation standing behind a type and a group both, or behind a
# parenthesised pattern, is not read as one.
# `MutantDeclarationRemovalTests` is the reading -- of the declaration and of the `out` assignment
# both -- so a spelling missing from here is one red line there, and so is any narrowing that lets a
# removal carry off an `out` argument spelled as a bare name.
# The last two arms are read on their own by `binds_a_name_conditionally`, which asks which paths
# reach a binding rather than whether a removal carries one off.
DECONSTRUCTION = re.compile(r"\bvar\s*\(")
OUT_ARGUMENT = re.compile(r"\bout\b(?!\s*(?:_|[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+)\s*[,)])")
PATTERN_DESIGNATION = re.compile(
    r"\bis\s+(?:not\s+)?(?!not\b)"
    r"(?:var\s+|[A-Za-z_][\w.<>\[\]?]*\s+|[{\[][^;]*[}\]]\s+)[A-Za-z_]")
DECLARES_A_NAME = re.compile("|".join(
    (DECONSTRUCTION.pattern, OUT_ARGUMENT.pattern, PATTERN_DESIGNATION.pattern)))

# `out spec` naming a variable rather than declaring one, which is the spelling a flip need not
# strand: unlike a declaration, the name can have been written before the statement runs.
OUT_BARE_NAME = re.compile(r"\bout\s+([A-Za-z_]\w*)\s*[,)]")

# What has to stand in front of a write for `assigned_above` to read it as the block's own. `:` is a
# STATEMENT_BOUNDARY and is left out here because a write standing behind one need not have run: a
# ternary's arm is entered on one side of its condition only. Inside a switch the same exclusion
# refuses a write standing first in the flip's own arm, which does reach the flip on every path -- a
# cost of the reading rather than its reason, since the arm above the flip's is SWITCH_LABEL's.
ASSIGNMENT_LEAD = (";", "{", "}")

# The label the walk cannot pass: the arm behind one is entered without the arm above it having run.
# `default` is spelled with its colon because the word is also an expression.
SWITCH_LABEL = re.compile(r"^(?:case\b|default\s*:)")


# Braces and brackets count towards depth as well as parentheses. A property pattern puts a colon
# inside braces at the enclosing parenthesis depth -- `is { Count: > 0 }` -- so a model that reads
# parentheses alone sees that colon as the clause's own and cuts the brace away from its partner.
OPENING = "([{"
CLOSING = ")]}"


def encloses_its_own_groups(line, start, mask, limit):
    """Whether every group the line opens it also closes, and it closes none it did not open."""
    depth = 0
    for index in range(limit):
        if not mask[start + index]:
            continue
        if line[index] in OPENING:
            depth += 1
        elif line[index] in CLOSING:
            depth -= 1
            if depth < 0:
                return False
    return depth == 0


def clause_cuts(line, start, mask, limit):
    """Removable (column, text) spans over one line, each a join plus the clause it introduces.

    A span ends at the next join of its own depth or at the close of the group holding it, so what comes
    out is parenthesis-balanced and the remainder still parses. A chain's first clause has no preceding
    join to carry away with it, and where its expression begins cannot be read off the line, so it is
    left alone: under-reporting one clause beats emitting mutants that only ever come back uncompilable.

    A condition spread over several lines is skipped whole, for the same reason. Its group closes on a
    later line, so a cut runs to the end of this one and takes an unmatched parenthesis with it — which
    is what the generation-health guard in test_mutation_check.py caught when this returned cuts for any
    line at all.
    """
    if not encloses_its_own_groups(line, start, mask, limit):
        return []

    joins = []
    depth = 0
    index = 0
    while index < limit:
        if not mask[start + index]:
            index += 1
            continue
        character = line[index]
        if character in OPENING:
            depth += 1
        elif character in CLOSING:
            depth -= 1
        else:
            join = next((candidate for candidate in LOGIC_JOINS
                         if line.startswith(candidate, index)), None)
            if join is not None:
                joins.append((index, depth, join))
                index += len(join)
                continue
        index += 1

    cuts = []
    for column, depth, join in joins:
        probe = column + len(join)
        level = depth
        while probe < limit:
            if mask[start + probe]:
                character = line[probe]
                if character in OPENING:
                    level += 1
                elif character in CLOSING:
                    if level == depth:
                        break
                    level -= 1
                elif level != depth:
                    # Everything below ends the clause, and only at the join's own depth: a comma one
                    # level in belongs to an argument list the clause is calling, and stopping there
                    # cut `s.StartsWith("rgb(", …)` in half.
                    pass
                elif character in ";,":
                    # A chain that is a whole statement rather than a condition closes no group, so
                    # without these the probe runs to the end of the line and takes the terminator or
                    # the separator with it: `var ok = a && b;` came back as `var ok = a`, and an
                    # object initializer's `Memoize = a || b,` lost the comma that ended the member.
                    break
                elif character == "?" and line[probe + 1:probe + 2] not in (".", "?", "[", ">"):
                    # A ternary's own punctuation belongs to the expression around the chain, not to
                    # the chain: `x = a || b ? c : d` cut to `x = a` where the type came from the
                    # ternary. The four spellings excluded are `?.`, `??`, `?[` and a nullable type.
                    break
                elif character == ":" and ":" not in (line[probe + 1:probe + 2],
                                                      line[probe - 1:probe]):
                    # The other half of one, reached when the chain sits inside a ternary's branch.
                    break
                elif level == depth and any(line.startswith(c, probe) for c in LOGIC_JOINS):
                    break
            probe += 1
        # Trailing space left behind rather than taken: a cut ending at a `?` would otherwise
        # close up against it and leave `index >= 0? count`, which compiles and reads as a typo.
        text = line[column:probe].rstrip()
        if text.strip() != join.strip():
            cuts.append((column, text))
    return cuts


def code_only(text, mask, start, end):
    """What the compiler sees between two offsets, with the comments and the literals taken out."""
    return "".join(text[offset] for offset in range(start, end) if mask[offset])


def code_above(text, mask, spans, number):
    """The nearest code the mask leaves above line `number`, or "" at the top of the file."""
    for above in range(number - 2, -1, -1):
        seen = code_only(text, mask, *spans[above]).strip()
        if seen:
            return seen
    return ""


def code_below(text, mask, spans, number):
    """The nearest code the mask leaves below line `number`, or "" at the end of the file."""
    for below in range(number, len(spans)):
        seen = code_only(text, mask, *spans[below]).strip()
        if seen:
            return seen
    return ""


# A `do` loop's tail. `while (more);` is an identifier, a parenthesised head and a semicolon, which
# is what the removal pattern reads -- and the head it would leave, `do { ... } ;`, is not a
# statement: the C# parser says `'while' expected`. An empty-bodied `while` loop is spelled the same
# and its removal compiles, so the two are separated by what the code above closes.
WHILE_TAIL = re.compile(r"^while\s*\(")


def closes_a_do_block(text, mask, spans, number):
    """Whether the code above line `number` closes a `do` block.

    Walked up by brace depth rather than by pattern, because the `do` and the brace it opens may sit
    on different lines and the block between them holds braces of its own.
    """
    if not code_above(text, mask, spans, number).endswith("}"):
        return False
    depth = 0
    for above in range(number - 2, -1, -1):
        seen = code_only(text, mask, *spans[above]).strip()
        if not seen:
            continue
        depth += seen.count("}") - seen.count("{")
        if depth > 0:
            continue
        cut = seen.rfind("{")
        head = seen[:cut].rstrip() if cut >= 0 else ""
        if head:
            return head.endswith("do")
        # The brace stands alone, so the `do` is whatever code sits above it.
        return code_above(text, mask, spans, above + 1).rstrip().endswith("do")
    return False


# A control header whose body is the next line: deleting that line leaves the header with none.
AWAITING_A_BODY = re.compile(r"(?:^|[;{}])\s*(?:if|for|foreach|while|else\s+if)\s*\(.*\)$|(?:^|[;{}])\s*else$")


def deletable_line(text, mask, spans, number):
    """Whether removing line `number`'s code leaves what surrounds it standing.

    Three ways it is not, each measured with the C# parser. The code above has to have ended a
    statement -- `=> Fragment(...)` and `= new(...)` both match the removal pattern and are the tail
    of a declaration, so deleting them leaves a member with no body, which was 77 mutants. An
    `if (...) Call();` whose next line is an `else` takes the `if` with it and strands the `else`,
    which was six more. And a `do` loop's `while` tail leaves a `do` block with no tail at all.
    """
    if not code_above(text, mask, spans, number).endswith(STATEMENT_BOUNDARY):
        return False
    if WHILE_TAIL.match(code_only(text, mask, *spans[number - 1]).strip()) \
            and closes_a_do_block(text, mask, spans, number):
        return False
    return not code_below(text, mask, spans, number).startswith("else")


def groups_left_open(code):
    return sum(code.count(mark) for mark in OPENING) - sum(code.count(mark) for mark in CLOSING)


def statement_span(text, mask, spans, number):
    """The first and last line of the statement holding line `number`, one-based and inclusive.

    Bounded by the code the mask leaves rather than by the raw lines, in both directions, so a comment
    between two continuation lines does not end a statement.

    A brace the statement itself holds -- a list or property pattern, a lambda body -- is in
    STATEMENT_BOUNDARY, so a walk stopping at every boundary reads one statement as two. The group
    count is what carries the span past it. Downwards it stops counting once the groups balance,
    because the brace after a balanced condition opens the block it guards rather than continuing it.
    """
    first = number
    open_groups = groups_left_open(code_only(text, mask, *spans[number - 1]))
    while first > 1:
        if (open_groups >= 0
                and code_only(text, mask, *spans[first - 2]).strip().endswith(STATEMENT_BOUNDARY)):
            break
        first -= 1
        open_groups += groups_left_open(code_only(text, mask, *spans[first - 1]))
    last = number
    while last < len(spans):
        if (open_groups <= 0
                and code_only(text, mask, *spans[last - 1]).strip().endswith(STATEMENT_BOUNDARY)):
            break
        carrying = open_groups > 0
        last += 1
        if carrying:
            open_groups += groups_left_open(code_only(text, mask, *spans[last - 1]))
    return first, last


def statement_start(text, number):
    """The first line of the statement holding line `number`."""
    return statement_span(text, code_mask(text), line_spans(text), number)[0]


def assigned_above(text, mask, spans, number, name):
    """Whether a write to `name` above line `number` reaches it on every path.

    Three readings of the text, none of them definite assignment. The write is in the block holding
    line `number` rather than one nested in it; the code in front of it ended a statement; and no
    switch label stands between the two. The brace count is the first of those on its own, and a
    braceless body opens no brace for it -- so without the second, the write `if (fallback)` guards
    reads the same as an unguarded one.

    A write in an enclosing block reaches line `number` on every path too and is passed over anyway,
    since a walk that left the block would have to tell a member's own writes from a field
    initialiser, which a local of the same name shadows.
    """
    # `else` stands where a declaration's type stands, so the pattern would otherwise read the
    # braceless body written on the header's own line as one -- and that body has no line above it
    # for the lead below to refuse.
    written = re.compile(r"^(?:(?!else\b)[\w.<>\[\]?]+\s+)?" + re.escape(name)
                         + r"\s*(?<![-+*/%&|^!<>=])=(?![=>])")
    depth = 0
    for above in range(number - 1, 0, -1):
        code = code_only(text, mask, *spans[above - 1]).strip()
        depth += code.count("}") - code.count("{")
        if depth < 0:
            return False
        if depth != 0:
            continue
        if SWITCH_LABEL.match(code):
            return False
        if not written.match(code):
            continue
        lead = code_above(text, mask, spans, above)
        if not lead or lead.endswith(ASSIGNMENT_LEAD):
            return True
    return False


def binds_a_name_conditionally(text, mask, spans, number):
    """Whether the statement holding line `number` binds a name on only some of the paths through it.

    Flipping a join there can leave a read of that name unassigned, and the mutant then compiles
    nowhere -- the outcome DECLARES_A_NAME refuses for a removal, reached by a rewrite. Whether such a
    read exists is definite assignment, which a pattern over the text cannot decide, so what is refused
    is the statement that could strand one rather than the statement that does. The answer is the
    statement's rather than one join's, because a join in front of the binding strands it as surely as
    one behind, and because the read left unassigned can sit in the block the condition guards, which
    no reading of the condition alone reaches.

    A pattern designation binds only where the pattern matched, so each one this reads counts. An `out`
    argument is refused from the first join along, since flipping a join cannot stop a condition's first
    clause from running -- a reading of the clauses rather than of what runs inside one, and
    `Generators~/README.md` is where that gap is measured. An `out` naming a variable rather than
    declaring one is exempt where `assigned_above` finds a write, which is that reading's own.
    """
    first, last = statement_span(text, mask, spans, number)
    statement = " ".join(code_only(text, mask, *spans[line - 1]).strip()
                         for line in range(first, last + 1))
    if PATTERN_DESIGNATION.search(statement):
        return True
    for found in OUT_ARGUMENT.finditer(statement):
        named = OUT_BARE_NAME.match(statement, found.start())
        if named and assigned_above(text, mask, spans, first, named.group(1)):
            continue
        if any(statement.find(join, 0, found.start()) >= 0 for join in LOGIC_JOINS):
            return True
    return False


def flip_refused(text, mask, spans, number):
    """Whether line `number` carries a join the statement's bindings keep the logic operator off.

    The raw line is asked first, so the statement walk is not taken on a line holding no join for it
    to answer about.
    """
    start, end = spans[number - 1]
    return (any(join in text[start:end] for join in LOGIC_JOINS)
            and binds_a_name_conditionally(text, mask, spans, number))


def refusal_census(project):
    """A tab-separated path and line count for each source a campaign may mutate that it fires in.

    Recorded per file rather than counted in total, for the reason `duplication_check.py` records a
    set: a total nets out, so a widening that silences one file passes behind sources that grew.
    `Generators~/README.md` carries why it records the refusal rather than what the operator emits,
    and what the floor this replaces could not see.
    """
    rows = []
    for path in sorted((project / PACKAGE).rglob("*.cs")):
        if not mutable(path, project):
            continue
        text = path.read_text()
        mask = code_mask(text)
        spans = line_spans(text)
        refused = sum(1 for number in range(1, len(spans) + 1)
                      if flip_refused(text, mask, spans, number))
        if refused:
            rows.append("{}\t{}".format(relative_to(path, project).as_posix(), refused))
    return rows


# Cached because a file's mutants are each applied to the same text, and every one of them asks.
@functools.lru_cache(maxsize=16)
def line_spans(text):
    spans = []
    offset = 0
    for line in text.splitlines(keepends=True):
        spans.append((offset, offset + len(line)))
        offset += len(line)
    return tuple(spans)


def mutations_for(path, text, target_lines):
    constructs = mask_spans(text)
    mask = code_mask(text, constructs)
    comments = comment_edges(constructs)
    spans = line_spans(text)
    found = []
    for number in sorted(target_lines):
        if number > len(spans):
            continue
        start, end = spans[number - 1]
        line = text[start:end]
        flippable = not flip_refused(text, mask, spans, number)
        for before, after, operator in OPERATORS:
            if operator == "logic" and not flippable:
                continue
            index = line.find(before)
            while index >= 0:
                # `"a" - "b"` is not an expression: `-` has no string overload, so a `+` with a
                # literal on either side rewrites into CS0019 rather than into behaviour a test
                # could notice. Read per occurrence -- a line can hold one of each, and skipping
                # the line took 650 arithmetic mutants where the mechanism is 93.
                if (all(mask[start + index:start + index + len(before)])
                        and not (before == " + " and joins_a_string(
                            text, mask, start + index, comments))):
                    found.append(Mutant(path, number, index, before.strip(), after.strip(), operator))
                index = line.find(before, index + 1)
        for before, after, operator in WORD_OPERATORS:
            for match in re.finditer(r"\b{}\b".format(before), line):
                if not all(mask[start + match.start():start + match.end()]):
                    continue
                # `while (true)` is how a method's only returning path is reached; `while (false)`
                # leaves it with none and the file stops compiling -- CS0161 rather than a verdict.
                if before == "true" and FOREVER_LOOP.search(line[:match.end()]):
                    continue
                found.append(Mutant(path, number, match.start(), before, after, operator))
        stripped = line.strip()
        limit = len(line.rstrip())
        # The masked code rather than the raw line: `;$` fails wherever anything follows the
        # semicolon, and a trailing comment or a semicolon inside a literal is not something the
        # compiler sees a difference in.
        code = code_only(text, mask, start, end)
        statement = code.strip()
        if (REMOVABLE_LINE.match(statement) and not CONTROL_KEYWORD.match(statement)
                and not DECLARES_A_NAME.search(code)
                and deletable_line(text, mask, spans, number)):
            # Spliced over the code the mask leaves rather than over the raw line: taking the line
            # whole carries off a block comment's opening, or its closing, when only one of the two
            # is on this line.
            columns = [index for index in range(limit)
                       if mask[start + index] and not line[index].isspace()]
            found.append(Mutant(path, number, columns[0],
                                line[columns[0]:columns[-1] + 1], ";", "line removed"))
        # `cut`, not `text`: binding the file's source here left every line processed after the
        # first cut read out of a clause string, so one `||` early in a range silenced the rest.
        for column, cut in clause_cuts(line, start, mask, limit):
            if DECLARES_A_NAME.search(code_only(text, mask, start + column, start + column + len(cut))):
                continue
            found.append(Mutant(path, number, column, cut, "", "clause removed"))
        # This operator deletes a whole line too, so it owes the same two questions the removal
        # operator asks -- but not the third. `deletable_line` also requires the code above to have
        # ended a statement, which is about a declaration's tail and rejects a fragment that starts
        # on the line under test; a guard is a whole statement by the pattern that matched it.
        # What it does owe: the line above must not be a control header still waiting for its body,
        # and the line below must not be an `else`. Neither is in the package today -- 396 mutants
        # either way -- so what moves is that the next one cannot be emitted unread.
        if (GUARD_STATEMENT.match(stripped) and all(mask[start:start + limit])
                and not DECLARES_A_NAME.search(code_only(text, mask, start, start + limit))
                and not AWAITING_A_BODY.search(code_above(text, mask, spans, number))
                and not code_below(text, mask, spans, number).startswith("else")):
            found.append(Mutant(path, number, line.index(stripped), stripped, "", "guard removed"))
    return found


def code_line_numbers(text, numbers):
    """The changed lines the compiler sees something on beyond block punctuation.

    This is the denominator every verdict is quoted against, because the operators above reach a
    minority of it -- a method written as a run of assignments generates nothing at all -- and a
    campaign reporting only that nothing survived reads as a statement about the whole change.
    Generators~/README.md ▸ Mutation testing carries what that minority measures.
    """
    spans = line_spans(text)
    mask = code_mask(text)
    found = []
    for number in sorted(numbers):
        if number > len(spans):
            continue
        start, end = spans[number - 1]
        seen = "".join(text[offset] for offset in range(start, end) if mask[offset])
        if seen.strip(" \t\r\n{};"):
            found.append(number)
    return found


def apply_mutation(text, mutant):
    spans = line_spans(text)
    start, end = spans[mutant.line - 1]
    line = text[start:end]
    if mutant.operator in ("clause removed", "guard removed", "line removed"):
        mutated = line[:mutant.column] + mutant.after + line[mutant.column + len(mutant.before):]
    elif mutant.operator == "literal":
        mutated = (
            line[:mutant.column]
            + mutant.after
            + line[mutant.column + len(mutant.before):]
        )
    else:
        mutated = (
            line[:mutant.column]
            + " {} ".format(mutant.after)
            + line[mutant.column + len(mutant.before) + 2:]
        )
    return text[:start] + mutated + text[end:]


def assembly_of(path):
    """The assembly a source file compiles into: the nearest .asmdef at or above it."""
    for parent in [path.parent] + list(path.parents):
        for asmdef in sorted(parent.glob("*.asmdef")):
            return json.loads(asmdef.read_text())["name"]
    return None


def origin_copy(project, base):
    """As origin's copy of a branch moves on, its merge base with a branch cut from it stays at the
    fork point. The local branch can sit behind the fork point until somebody brings it current, and
    keyed on it, a kept verdict stopped matching an unchanged tree once they did, while one written
    against `origin/main` was not found until then. `LaggingLocalBaseTests` poses both.

    Only a name that is a branch here as well is read at origin's, because origin carries a `HEAD`.
    """
    remote = "refs/remotes/origin/" + base
    both = subprocess.run(
        ["git", "-C", str(project), "show-ref", "--verify", "--quiet", "refs/heads/" + base, remote],
        capture_output=True, text=True,
    )
    return remote if both.returncode == 0 else base


def merge_base_of(project, base):
    against = origin_copy(project, base)
    found = subprocess.run(
        ["git", "-C", str(project), "merge-base", against, "HEAD"],
        capture_output=True, text=True,
    )
    if found.returncode != 0:
        raise SystemExit("cannot resolve a merge base with {}: {}".format(against, found.stderr.strip()))
    return found.stdout.strip()


def changed_files_and_lines(project, base):
    since = merge_base_of(project, base)
    # Diffing the merge base against the working tree rather than against HEAD, so a branch whose
    # change is not committed yet is still measured.
    diff = subprocess.run(
        ["git", "-C", str(project), "diff", "--unified=0", since],
        capture_output=True, text=True, check=True,
    ).stdout
    # A file the branch has created and not yet staged is in no diff, and a new file is where a
    # missing test is likeliest, so it is taken whole rather than skipped.
    untracked = subprocess.run(
        ["git", "-C", str(project), "ls-files", "--others", "--exclude-standard"],
        capture_output=True, text=True, check=True,
    ).stdout.splitlines()
    changed = {}
    for name in untracked:
        path = project / name
        if path.suffix == ".cs":
            changed[path] = set(range(1, len(path.read_text().splitlines()) + 1))
    current = None
    for line in diff.splitlines():
        if line.startswith("+++ b/"):
            current = project / line[6:]
        elif line.startswith("+++ ") or line.startswith("diff --git"):
            current = None
        elif line.startswith("@@") and current is not None:
            match = re.search(r"\+(\d+)(?:,(\d+))?", line)
            if match:
                start = int(match.group(1))
                count = int(match.group(2) or 1)
                changed.setdefault(current, set()).update(range(start, start + count))
    return changed


def mutable(path, project):
    if path.suffix != ".cs" or not path.exists():
        return False
    try:
        relative = path.relative_to(project).as_posix()
    except ValueError:
        return False
    if not relative.startswith(PACKAGE + "/"):
        return False
    # Unity's asset database does not import a `~`-suffixed directory, so a source under one compiles
    # into nothing here: mutating a line there leaves every assembly byte-identical, the run scores
    # NOT_BUILT, and the campaign fails -- so a branch that edits one can never earn a passing
    # campaign, and what it is told names a build state rather than a scope rule. The starter sample
    # is the live case: `Samples~/StarterApp` is the copy nothing compiles, and `sync_starter_sample.py`
    # keeps `Assets/VelvetStarterSample` beside it as the copy that does. `Generators~` was named here
    # one directory at a time, which is the same rule read off one instance of it.
    if any(part.endswith("~") for part in relative.split("/")):
        return False
    # A mutation inside a test asserts nothing about the code the test covers.
    return "/Tests/" not in relative and "/TestUtilities/" not in relative


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else ""


# --------------------------------------------------------------------------------------------------
# Holding a mutation
# --------------------------------------------------------------------------------------------------

class Holder:
    """The one mutation on disk, recorded before it is written and released after it is undone.

    The order is the whole mechanism, and it is chosen so that no interruption can leave a mutation
    with nothing naming it: the record is written first and removed last, so an interruption leaves
    either nothing, or a record over a file that may or may not be mutated -- and rewriting the
    original is correct in both of those. The other order leaves the state this exists to end, a
    mutated production file that reads as somebody's unfinished edit.

    A `finally` alone does not reach it. SIGTERM runs no Python at all under the default handler,
    which is what `TaskStop`, a timeout and a killed session all send.
    """

    def __init__(self, sentinel):
        self.sentinel = Path(sentinel)
        self.child = None
        # While an editor is being started it exists before `child` names it, so a signal arriving
        # then is held until it does rather than leaving that editor running over a restored tree.
        self.starting = False
        self.pending = None

    def started(self):
        """Ends the start `launch` marked, delivering a signal that arrived during it."""
        self.starting = False
        if self.pending is not None:
            number, self.pending = self.pending, None
            signal.raise_signal(number)

    def hold(self, source, original, mutated, description):
        self.hold_files({source: (original, mutated)}, description)

    def hold_files(self, files, description):
        """Records every source in `files` (path -> (original, mutated)) under the one sentinel, which
        is how a session's rewritten tree is held: `release`, `--restore` and `--carried` read the list."""
        # Refusing rather than overwriting: two campaigns started close enough together both reach
        # here, and the second overwriting the first ends with one restoring the other's file.
        if self.sentinel.exists():
            raise SystemExit("{} already records a held mutation; two campaigns are running over one "
                             "tree".format(self.sentinel))
        self.sentinel.write_text(json.dumps({
            "sources": [{
                "source": str(source),
                "original": original,
                "original_sha": hashlib.sha256(original.encode()).hexdigest(),
                "mutated_sha": hashlib.sha256(mutated.encode()).hexdigest(),
            } for source, (original, mutated) in sorted(files.items(), key=lambda item: str(item[0]))],
            "mutation": description,
            "pid": os.getpid(),
            "since": time.strftime("%Y-%m-%dT%H:%M:%S"),
        }, indent=2))

    def release(self):
        """Puts back whatever the record names and removes it. Safe to call when there is no record."""
        if not self.sentinel.exists():
            return None
        try:
            held = json.loads(self.sentinel.read_text())
            for entry in held_sources(held):
                Path(entry["source"]).write_text(entry["original"])
        except (OSError, ValueError, KeyError, TypeError) as failure:
            # Leaving the record is the point: what it names is still on disk, and a run that removed
            # it would take the only thing saying so with it.
            print("could not restore from {}: {}".format(self.sentinel, failure), file=sys.stderr)
            return None
        self.sentinel.unlink()
        return held

    def outstanding(self):
        """None when no campaign holds anything, UNREADABLE when one does and this cannot say what.

        The three are kept apart because every reader has to fail closed on the middle one, and a
        placeholder dict standing in for it fails open in whichever reader compares its fields.
        """
        if not self.sentinel.exists():
            return None
        try:
            return json.loads(self.sentinel.read_text())
        except (OSError, ValueError):
            return UNREADABLE

    def guard(self):
        """Restores on the signals that end a campaign, then dies of the signal rather than of this."""
        def handler(number, _frame):
            if self.starting:
                self.pending = number
                return
            if self.child is not None and self.child.poll() is None:
                kill_group(self.child)
            self.release()
            signal.signal(number, signal.SIG_DFL)
            os.kill(os.getpid(), number)

        for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            signal.signal(number, handler)


def held_sources(held):
    """The sources a record names: a list under `sources`, or the one `source` a record written before
    a session could hold several carries."""
    if "sources" in held:
        return list(held["sources"])
    return [{key: held[key] for key in ("source", "original", "original_sha", "mutated_sha") if key in held}]


def held_names(held):
    return ", ".join(entry.get("source", "<unnamed>") for entry in held_sources(held))


def unity_busy():
    result = subprocess.run(["ps", "-Ao", "command="], capture_output=True, encoding="utf-8",
                            errors="replace")
    return sum(1 for line in result.stdout.splitlines() if re.match(UNITY_RUNNING, line))


def wait_for_quiet(seconds):
    """Waits rather than sharing the machine: every failure in a mutant run has to be attributable
    to the mutation, and a second editor is a second explanation for all of them."""
    deadline = time.time() + seconds
    announced = False
    while unity_busy():
        if time.time() > deadline:
            return False
        if not announced:
            print("another Unity test run is in flight; waiting for it", flush=True)
            announced = True
        time.sleep(5)
    return True


def lock_held(project):
    """Whether `project`'s editor lock file exists and something holds a lock on it.

    Both kinds of lock are asked for, since which one the editor takes has not been measured here.
    """
    try:
        descriptor = os.open(str(Path(project) / LOCKFILE), os.O_RDWR)
    except FileNotFoundError:
        return False
    except OSError:
        return True
    try:
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        fcntl.flock(descriptor, fcntl.LOCK_UN)
        fcntl.lockf(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        return False
    except OSError:
        return True
    finally:
        os.close(descriptor)


def wait_for_release(project, seconds):
    """Whether `project`'s lock came free within `seconds`."""
    deadline = time.time() + seconds
    announced = False
    while lock_held(project):
        if time.time() > deadline:
            print("the project lock is still held after {}s; launching anyway".format(seconds), flush=True)
            return False
        if not announced:
            print("the project lock is held; waiting for it", flush=True)
            announced = True
        time.sleep(1)
    return True


def kill_group(child):
    """Kills the watchdog `launch` starts and every process still in the group it leads."""
    try:
        os.killpg(child.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass


def reap(child):
    kill_group(child)
    child.wait()


# Leads the editor's group: runs the editor, exits with it, and kills the group once the process that
# started it has gone, which no handler here can do for a SIGKILL.
WATCHDOG = """\
import os, signal, subprocess, sys
parent = int(sys.argv[1])
editor = subprocess.Popen(sys.argv[2:])
while True:
    try:
        sys.exit(editor.wait(timeout=1))
    except subprocess.TimeoutExpired:
        if os.getppid() != parent:
            os.killpg(0, signal.SIGKILL)
"""


def relay(stream, said):
    """Passes what the editor prints on to this job's log as it arrives, keeping a copy in `said`."""
    for line in iter(stream.readline, b""):
        text = line.decode("utf-8", "replace")
        said.append(text)
        sys.stdout.write(text)
        sys.stdout.flush()


def launch(command, timeout, holder, env=None, expired=None):
    """One editor launch: its wall clock, whether it had to be killed, the most other editors seen at
    once, and what it printed. `expired`, where given, is asked every few seconds as well, and a true
    answer kills the editor as the bound does."""
    start = time.time()
    said = []
    if holder is not None:
        holder.starting = True
    try:
        # A group of its own, so a kill reaches whatever the editor started and left in it.
        child = subprocess.Popen([sys.executable, "-c", WATCHDOG, str(os.getpid()), *command],
                                 stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                 start_new_session=True, env=env)
        if holder is not None:
            holder.child = child
    finally:
        if holder is not None:
            holder.started()
    reader = threading.Thread(target=relay, args=(child.stdout, said), daemon=True)
    reader.start()
    # Sampled for the run's whole life, not once before it. The campaign waits before its baseline, each
    # session launch and each mutant's own launch, and a neighbour arriving ten seconds in is invisible
    # for the rest of that run -- where it can redden a timing-sensitive case, and the mutant is then
    # recorded killed. A mutant that actually survived, which is a hole in the tests, reported as covered.
    peak = 0
    timed_out = False
    try:
        while True:
            try:
                child.wait(timeout=3)
                break
            except subprocess.TimeoutExpired:
                if time.time() - start > timeout or (expired is not None and expired()):
                    reap(child)
                    timed_out = True
                    break
                peak = max(peak, max(0, unity_busy() - 1))
    finally:
        if child.poll() is None:
            reap(child)
        if holder is not None:
            holder.child = None
    wall = time.time() - start
    # Bounded, since a process that left the group can still hold the pipe open.
    reader.join(timeout=5)
    return wall, timed_out, peak, "".join(said)


def lock_refused(log):
    """Whether `run_suite` gave up on the launch that wrote `log` because every one was refused."""
    try:
        lines = Path(log).read_text(errors="replace").rstrip().splitlines()
    except OSError:
        return False
    return bool(lines) and lines[-1] == LOCK_REFUSED_LINE


def run_suite(unity, project, platform, scope, results, log, timeout, holder=None):
    """Returns the wall clock, whether the editor had to be killed, and the most other
    editors seen at once.

    A mutation can turn a loop bound into one that never terminates, and the run would otherwise
    wait on it for as long as the machine is left alone.

    The editor is held rather than waited on, so that a signal arriving here reaps it before the
    restore: an editor left running over a tree somebody has just put back writes a results file
    for a mutant that is no longer on disk.

    A launch the editor refused because the project was locked is made again, after the lock is
    waited on. Where every one was refused, `log` ends with `LOCK_REFUSED_LINE`.
    """
    command = [
        # -debugCodeOptimization: AGENTS.md's headless recipe says why a local run passes it.
        unity, "-runTests", "-batchmode", "-debugCodeOptimization", "-projectPath", str(project),
        "-testPlatform", platform, "-testResults", str(results), "-logFile", str(log),
    ]
    command += scope
    for _ in range(LOCK_ATTEMPTS):
        wait_for_release(project, LOCK_WAIT)
        wall, timed_out, peak, printed = launch(command, timeout, holder)
        if LOCK_REFUSAL not in printed:
            return wall, timed_out, peak
    with open(str(log), "a") as written:
        written.write("\n{}\n".format(LOCK_REFUSED_LINE))
    # Not timed out, whatever the last launch did after refusing: a caller reads a timeout with no
    # result as a hang, which is a kill.
    return wall, False, peak


# Anchored on a source path and a position, so an assertion message quoting the words "error CS" is
# not one. The code is not pinned to CS: this repository's own analyzers report VEL500 and VEL501 as
# errors, and a build they stop writes no results file at all -- which reads, from the results alone,
# exactly like an editor that crashed.
BUILD_ERROR = re.compile(
    r"^(?:.*?[\\/])?((?:Assets|Packages)[\\/][^(]+)\(\d+,\d+\): error [A-Z]+\d+", re.MULTILINE)


def build_error(log):
    """The first source a Unity log blames a build error on, or None."""
    if not log.exists():
        return None
    found = BUILD_ERROR.search(log.read_text(errors="replace"))
    return found.group(1).replace("\\", "/") if found else None


# Any compiler or analyzer error line. The editor log can cut the front off one -- measured,
# `r/Tests/Editor/InlineOwnerListPoolingTests.cs(45,33): error CS1061: ...` beside three whole copies of
# it -- so the path is kept as the fragment it arrived as rather than required to open at the root.
ERROR_LINE = re.compile(r"(\S*?)\((\d+),\d+\): error ([A-Z]+\d+): (.*)")
ANY_ERROR = re.compile(r"\berror [A-Z]+\d+:")


def operator_does_not_apply(log, mutant, project, first_line):
    """Whether an arithmetic `+ -> -` mutant's build stopped on CS0019 for its `-` and nothing else, in
    the statement it wrote it in.

    That is no `-` applying to the operands `+` joined, so the rewrite produced no program and there
    was nothing a test could have been asked. The statement rather than the
    mutated line, so a diagnostic placed on a left operand that starts above the operator still counts;
    `Generators~/README.md` ▸ the verdict list carries where that was measured. Any other error stays
    UNCOMPILABLE, so a generator emitting broken C# stays loud.
    """
    if (mutant.operator, mutant.before, mutant.after) != ("arithmetic", "+", "-") or not log.exists():
        return False
    where = relative_to(mutant.path, project).as_posix()
    claim = "Operator '-' cannot be applied"
    lines = [line for line in log.read_text(errors="replace").splitlines() if ANY_ERROR.search(line)]
    for line in lines:
        found = ERROR_LINE.search(line)
        if found is None:
            return False
        fragment, number, code, message = found.groups()
        fragment = fragment.replace("\\", "/")
        if not (fragment.endswith(".cs") and where.endswith(fragment) and code == "CS0019"
                and message.startswith(claim) and first_line <= int(number) <= mutant.line):
            return False
    return bool(lines)


# What a fixture reads when it walks this repository's own text rather than running its code. A
# mutation edits a source file, so such a fixture reddens on the edit itself -- measured on the
# campaign for Hooks.cs:1603, where a clause-removal mutant was recorded killed by three cases and
# one of them was DocumentationDriftTests walking an identifier allowlist against the sources.
TEXT_CORPUS = ("DocumentationCorpus", "TrackedFiles")

FIXTURE_CLASS = re.compile(r"\bclass\s+([A-Z][A-Za-z0-9_]*)")


def text_reading_fixtures(project):
    """Fixture names whose own source reads the repository's text.

    Derived rather than listed: the set is what references the corpus, so a fixture added to it
    later is covered by having been written that way. A list would be the mirror shape this
    repository pins against, and it is the shape that goes stale silently.
    """
    found = set()
    # Pruned while walking rather than filtered after, so a restored Library is never walked.
    for root, directories, files in os.walk(str(project)):
        directories[:] = [name for name in directories if name not in ("Library", "obj")]
        for name in files:
            if not name.endswith("Tests.cs"):
                continue
            try:
                text = (Path(root) / name).read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            if any(corpus in text for corpus in TEXT_CORPUS):
                found.update(FIXTURE_CLASS.findall(text))
    return found


# A fixture reading compiled IL rather than running it. Inside a session it reads the assembly every
# placed guard was compiled into, not the one mutant its own launch would have built.
IL_READ = re.compile(r"\b(?:ModuleDefinition\.ReadModule|AssemblyDefinition\.ReadAssembly)\s*\(|\.GetILAsByteArray\s*\(")
TYPEOF_ASSEMBLY = re.compile(r"typeof\(\s*(?:[A-Za-z_][\w]*\.)*([A-Za-z_]\w*)\s*(?:<[^()]*>)?\s*\)\.Assembly\b")
QUOTED = re.compile(r'"([A-Za-z_][\w.]*)"')
DECLARED_TYPE = re.compile(r"\b(?:class|struct|interface|enum|record)\s+([A-Za-z_]\w*)")


def il_reading_assemblies(project):
    """The assemblies some fixture reads the IL of: those declaring a type whose `typeof(...).Assembly`
    such a fixture takes, and those it names by a string equal to an assembly's name.

    Derived as `text_reading_fixtures` is. A name counts whichever .asmdef in the project declares it,
    test assemblies included, since what matters is only whether a mutated one is among them.
    """
    sources, asmdefs = [], {}
    for root, directories, files in os.walk(str(project)):
        directories[:] = [name for name in directories if name not in ("Library", "obj", "Logs", "Temp")]
        for name in files:
            path = Path(root) / name
            if name.endswith(".asmdef"):
                try:
                    asmdefs[json.loads(path.read_text())["name"]] = path.parent
                except (OSError, ValueError, KeyError):
                    continue
            elif name.endswith(".cs"):
                sources.append(path)
    types, names = set(), set()
    for path in sources:
        if not path.name.endswith("Tests.cs"):
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        if IL_READ.search(text):
            types.update(TYPEOF_ASSEMBLY.findall(text))
            names.update(name for name in QUOTED.findall(text) if name in asmdefs)
    if types:
        for path in sources:
            text = path.read_text(encoding="utf-8", errors="replace")
            if types & set(DECLARED_TYPE.findall(text)):
                names.add(assembly_of(path))
    return names - {None}


def killed_by_behaviour(names, text_readers):
    """The failing cases that are not a text-reading fixture's.

    A mutant killed only by those was not killed by anything a user could notice: the fixture
    reddened because the mutation edited a source file, not because a behaviour changed. Counting it
    as a kill hides exactly what a campaign exists to find, and hides it on the lines most worth
    mutating -- an identifier the documentation names is one that is public.
    """
    return [name for name in names
            if not any(part in text_readers for part in name.split("."))]


def excluding(scope, text_readers):
    """`scope` with every fixture of `text_readers` taken out of the run.

    `killed_by_behaviour` discounts their failures whatever they are, so running them cannot move a
    verdict. A term matches a suite whose full name ends in the class, so a case it takes out is one
    whose name carries that class, which `killed_by_behaviour` discounts too.
    """
    terms = ["!\\.{}$".format(name) for name in sorted(text_readers)]
    if not terms:
        return list(scope)
    scope = list(scope)
    if "-testFilter" in scope:
        position = scope.index("-testFilter") + 1
        scope[position] = ";".join([scope[position]] + terms)
    else:
        scope += ["-testFilter", ";".join(terms)]
    return scope


def area_of(path, project):
    """The `Runtime/<Area>` directory a source sits under, or None for one outside every area."""
    try:
        parts = path.relative_to(project).parts
    except ValueError:
        return None
    root = tuple(PACKAGE.split("/")) + ("Runtime",)
    if len(parts) > len(root) + 1 and parts[:len(root)] == root:
        return project.joinpath(*parts[:len(root) + 1])
    return None


def assembly_seconds(results):
    """(the run's test seconds, test assembly -> its seconds) from a results file."""
    root = ET.parse(str(results)).getroot()
    found = {}
    for suite in root.iter("test-suite"):
        if suite.get("type") == "Assembly":
            name = suite.get("name", "")
            found[name[:-len(".dll")] if name.endswith(".dll") else name] = float(
                suite.get("duration", "0"))
    return float(root.get("duration", "0")), found


def narrowable(results, areas):
    """Area -> the test assemblies under it that `results` ran, for each of `areas` whose assemblies
    took at most 1/NARROW_SHARE of that run's test seconds.

    Read off the baseline rather than listed, so what is taken is the platform's own assemblies, and
    an area whose fixtures grow slow stops being narrowed without anyone editing a list.
    """
    total, seconds = assembly_seconds(results)
    found = {}
    for area in sorted(areas):
        names = sorted(name for name in (json.loads(asmdef.read_text())["name"]
                                         for asmdef in area.rglob("*.asmdef"))
                       if name in seconds)
        if names and sum(seconds[name] for name in names) * NARROW_SHARE <= total:
            found[area] = names
    return found


def complete_result(results, since, expected):
    """Whether `results` is a whole reading of a launch started at `since` and asked for `expected`
    cases, so that it stands even though the editor was then killed at its bound.

    Written since the launch started; a `test-run` root carrying its result; its counts readable; as
    many cases reported as the run held and the matching baseline ran; and every one of them passed or
    failed, none inconclusive or skipped. Anything short of all of that is read as a launch the bound
    ended.
    """
    try:
        if os.path.getmtime(str(results)) < since:
            return False
        root = ET.parse(str(results)).getroot()
    except (OSError, ET.ParseError):
        return False
    if root.tag != "test-run" or not root.get("result"):
        return False
    try:
        total, held, passed, failed = (int(root.get(key)) for key in (
            "total", "testcasecount", "passed", "failed"))
    except (TypeError, ValueError):
        return False
    return total == held == expected and passed + failed == total


def narrowed_kill(results, log, timed_out, dll, baseline_hashes, text_readers):
    """Every case that failed in a narrowed run that `measure` would read as a kill had the whole
    suite run, or nothing."""
    counts = read_counts(results)
    if timed_out or build_error(log) or counts is None or not counts["failed"]:
        return []
    if sha(dll) == baseline_hashes.get(dll.name):
        return []
    names = failing_names(results)
    return names if killed_by_behaviour(names, text_readers) else []


def failing_names(results):
    root = ET.parse(str(results)).getroot()
    return [
        case.get("fullname") or case.get("name") or "<unnamed>"
        for case in root.iter("test-case")
        if case.get("result") == "Failed"
    ]


def read_counts(results):
    if not results.exists():
        return None
    try:
        root = ET.parse(str(results)).getroot()
    except ET.ParseError:
        # A killed editor leaves a part-written file. Raising here ends an hours-long campaign one
        # line before the verdict that would have classified it.
        return None
    if root.tag != "test-run":
        return None
    return {key: int(root.get(key, "0")) for key in ("total", "passed", "failed", "inconclusive")}


# --------------------------------------------------------------------------------------------------
# Measuring many mutants in one editor
# --------------------------------------------------------------------------------------------------

# A session is launched with this rather than `-runTests`, which starts the test framework's own run
# and quits after it. The prefix keeps a session inside UNITY_RUNNING, the pattern neuter_check.py's and
# base_red_check.py's busy counts spell too.
# `Assets/MutantSchemata/Editor/SchemataRunner.cs` takes the launch over from the plan it is handed.
SESSION_FLAG = "-runTestsSchemata"
SESSION_PLAN = "VELVET_SCHEMATA_PLAN"
# What each rewritten assembly's switch reads, and so which mutant is live.
SESSION_SWITCH = "VELVET_MUTANT"
# Added to a stage's bound before its session is killed: the stage's reload and the test framework's
# preparation of the job are inside it, where a launch's bound covers its startup the same way.
SESSION_STAGE_SLACK = 120
# A reload asked for and not finished within this ends the session: every stage asks for one, so the rest
# would each wait out their bound.
SESSION_RELOAD_BOUND = 180
# The unmutated program the areas' own baselines run under.
SESSION_OPENING = 0
SESSION_MUTANTS = 16


def utf16(text):
    return len(text.encode("utf-16-le")) // 2


def mutant_edit(text, mutant):
    """(utf-16 column, utf-16 length, replacement) of the edit `apply_mutation` makes on the mutant's
    line, or None where the edit read off the mutant is not that one."""
    start, end = line_spans(text)[mutant.line - 1]
    line = text[start:end]
    column = mutant.column
    if mutant.operator in {operator for _, _, operator in OPERATORS}:
        length, replacement = len(mutant.before) + 2, " {} ".format(mutant.after)
    else:
        length, replacement = len(mutant.before), mutant.after
    applied = text[:start + column] + replacement + text[start + column + length:]
    if applied != apply_mutation(text, mutant):
        return None
    return utf16(line[:column]), utf16(line[column:column + length]), replacement


def response_file(project, assembly):
    """The response file the editor last compiled `assembly` with, relative to `project`, or None."""
    found = sorted(Path(project, "Library", "Bee", "artifacts").glob("*/{}.rsp".format(assembly)),
                   key=lambda path: path.stat().st_mtime)
    return str(found[-1].relative_to(project)) if found else None


def schemata_request(project, mutants, indexes):
    """What the rewriter is asked, and the mutants declined before it is asked: (request, declined)."""
    files, assemblies, declined = {}, {}, {}
    for index in indexes:
        mutant = mutants[index - 1]
        text = mutant.path.read_text()
        edit = mutant_edit(text, mutant)
        if edit is None:
            declined[index] = "the edit read off the mutant is not the one apply_mutation makes"
            continue
        assembly = assembly_of(mutant.path)
        rsp = response_file(project, assembly)
        if rsp is None:
            declined[index] = "no response file for {}".format(assembly)
            continue
        assemblies[assembly] = rsp
        entry = files.setdefault(mutant.path, {"path": relative_to(mutant.path, project).as_posix(),
                                               "assembly": assembly, "text": text, "mutants": []})
        start, length, replacement = edit
        entry["mutants"].append({"id": index, "line": mutant.line, "start": start, "length": length,
                                 "replacement": replacement, "operator": mutant.operator,
                                 "before": mutant.before})
    return ({"project": str(project), "env": SESSION_SWITCH, "shapeRulesOff": True,
             "assemblies": assemblies, "files": list(files.values())}, declined)


def rewrite_schemata(unity, project, request, scratch):
    """The rewriter's answer, or None where it could not be built or run."""
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import schemata_tool
    try:
        return schemata_tool.rewrite(unity, project, request, scratch)
    except SystemExit as failure:
        print("the rewriter could not run, so every mutant takes its own launch: {}".format(failure))
        return None


def session_filter(scope):
    """(assembly names, filter terms) of an editor scope, as the test framework's command line reads
    `-assemblyNames` and `-testFilter`: each split on `;`."""
    names, terms = [], []
    for flag, into in (("-assemblyNames", names), ("-testFilter", terms)):
        if flag in scope:
            into.extend(scope[scope.index(flag) + 1].split(";"))
    return names, terms


def session_stage(name, scope, stop, bound, gate=""):
    names, terms = session_filter(scope)
    return {"name": name, "assemblyNames": names, "groupNames": terms, "testNames": [],
            "stopAtFirstFailure": stop, "bound": bound, "gate": gate}


def cs_id(number):
    """An id as the runner's `ToString("000")` spells it in a file name."""
    return format(number, "03d") if number >= 0 else "-" + format(-number, "03d")


def read_item(directory, number):
    try:
        return json.loads((Path(directory) / "item-{}.json".format(cs_id(number))).read_text())
    except (OSError, ValueError):
        return None


def clean_stage(stage):
    """Whether a baseline stage the session ran came out green over at least one case."""
    return bool(stage) and stage.get("finished") and not stage.get("omitted") and not stage.get(
        "failed") and not stage.get("inconclusive") and not stage.get("failures") and stage.get(
        "passed", 0) > 0


def stage_counts(stage):
    return read_counts(Path(stage["xml"])) if stage.get("xml") else None


def session_verdict(item, planned, index, assembly, expected, opening, confirmed, text_readers):
    """(verdict, detail, killers) for one mutant the session measured, or None where what it recorded
    does not stand as a reading -- the mutant then takes its own launch, whose reading does.

    `planned` is the plan's item for it; `expected` maps each of its stages to the cases a whole run of
    that stage holds; `opening` is the areas' baselines under the unmutated program; `confirmed` says,
    per killing case, whether it passed again under the unmutated program once the segment was done.
    """
    if not item:
        return None
    stages = item.get("stages") or []
    armed = "{}:{}".format(assembly, index)
    ran = [stage for stage in stages if not stage.get("omitted")]
    if not ran or any(not stage.get("finished") or armed not in stage.get("armed", []) for stage in ran):
        return None
    for stage in ran:
        if not stage.get("failures"):
            continue
        # A kill stands only where the stage's own baseline was green, and its first case passed again
        # under the unmutated program: a case failing there failed on what the session carried, not on
        # this mutant.
        if stage["name"] != "whole" and not clean_stage(opening.get(stage["name"])):
            return None
        names = stage["failures"]
        if confirmed.get(names[0]) is not True:
            return None
        behavioural = killed_by_behaviour(names, text_readers)
        if not behavioural:
            return None
        if stage["name"] == "whole":
            detail = "{} failed: {}".format(len(behavioural), ", ".join(
                name.split(".")[-1] for name in behavioural[:3]))
        else:
            names_of = {planned_stage["name"]: planned_stage for planned_stage in planned["stages"]}
            detail = "{} failed in {}: {}".format(len(behavioural), ", ".join(
                names_of[stage["name"]]["assemblyNames"]),
                                                   ", ".join(name.split(".")[-1] for name in behavioural[:3]))
        return KILLED, detail, names
    last = ran[-1]
    if last["name"] != "whole":
        return None
    # A survivor stands on complete runs: every stage it ran held every case that stage's baseline did.
    for stage in ran:
        counts = stage_counts(stage)
        if counts is None or counts["failed"] or counts["total"] != expected.get(stage["name"]):
            return None
    counts = stage_counts(last)
    if counts["inconclusive"]:
        return INCONCLUSIVE, "{} inconclusive, 0 failed".format(counts["inconclusive"]), ()
    return SURVIVED, "", ()


def session_plan(project, mutants, placed, attempts, whole_scope, platform, ceiling, timeout, directory):
    """The plan a session runs: the areas' baselines under the unmutated program, then each placed
    mutant's stages, with a confirmation after every SESSION_MUTANTS of them.

    A mutant runs the stages its own launches would: its area's assemblies first where the area is
    narrowed, stopping at a kill there, then the whole suite. An EditMode stage is cancelled at its first
    failing case rather than run out, since the verdict is decided there, so `killers` is the cases that
    failed before the stop; a PlayMode stage runs out.
    """
    stop = platform == "EditMode"
    opening = []
    for area in sorted(attempts):
        opening.append(session_stage("narrowed:" + area.name, attempts[area], False, ceiling))
    items = [{"id": SESSION_OPENING, "stages": opening}] if opening else []
    segments = []
    for number, index in enumerate(placed):
        area = area_of(mutants[index - 1].path, project)
        stages = []
        if area in attempts:
            stages.append(session_stage("narrowed:" + area.name, attempts[area], stop, ceiling,
                                        gate="narrowed:" + area.name))
        stages.append(session_stage("whole", whole_scope, stop, timeout))
        items.append({"id": index, "stages": stages})
        if number % SESSION_MUTANTS == SESSION_MUTANTS - 1 or number == len(placed) - 1:
            # A negative id matches no guard, so the confirmation runs the unmutated program.
            confirmation = -(len(segments) + 1)
            items.append({"id": confirmation, "confirm": True,
                          "stages": [session_stage("confirm", [], False, ceiling)]})
            segments.append(len(items))
    return {"output": str(directory), "platform": platform, "env": SESSION_SWITCH, "items": items,
            "start": 0, "end": len(items)}, segments


def run_session(args, project, plan, directory, holder):
    """Runs `plan` through as many session launches as it takes: one per segment, and one more past
    any item whose stage outlived its bound or took its editor down.

    Returns (position -> {"launch": which launch carried it to its end, "peak": the most other editors
    seen while it was the item in progress}, the positions no launch finished, and the reason the rest of
    the session was abandoned, or None).
    """
    plan_path = directory / "plan.json"
    state = directory / "runner-state.json"
    progress = directory / "runner-progress.json"
    done = directory / "runner-done"
    failed = directory / "runner-failed"
    items = plan["items"]
    runs, lost = {}, set()
    launches = 0
    position = 0
    for end in plan["segments"]:
        while position < end:
            launches += 1
            plan["start"], plan["end"] = position, end
            plan_path.write_text(json.dumps(plan, indent=1))
            state.write_text(json.dumps({"position": position, "stage": 0, "phase": "arm"}))
            for stale in (progress, done, failed):
                if stale.exists():
                    stale.unlink()
            log = directory / "session-{:03d}.log".format(position)
            command = [args.unity, SESSION_FLAG, "-batchmode", "-debugCodeOptimization", "-projectPath",
                       str(project), "-logFile", str(log)]
            env = dict(os.environ, **{SESSION_PLAN: str(plan_path)})
            env.pop(SESSION_SWITCH, None)
            launched = time.time()
            where, peaks, stuck = {}, {}, []

            def expired():
                try:
                    current = json.loads(progress.read_text())
                except (OSError, ValueError):
                    return time.time() - launched > args.timeout
                where.update(current)
                now = current["position"]
                peaks[now] = max(peaks.get(now, 0), max(0, unity_busy() - 1))
                waited = time.time() - current["since"]
                if current.get("phase") == "start" and waited > SESSION_RELOAD_BOUND:
                    stuck.append(now)
                    return True
                item = items[now] if now < len(items) else None
                stage = item["stages"][current["stage"]] if item and current["stage"] < len(item["stages"]) else None
                return waited > (stage["bound"] if stage else args.timeout) + SESSION_STAGE_SLACK

            if not wait_for_quiet(args.busy_timeout):
                return runs, lost, "another Unity test run was still in flight after {}s".format(args.busy_timeout)
            printed = ""
            for _ in range(LOCK_ATTEMPTS):
                wait_for_release(project, LOCK_WAIT)
                _, _, _, printed = launch(command, sum(
                    stage["bound"] + SESSION_STAGE_SLACK for item in items[position:end]
                    for stage in item["stages"]) + args.timeout, holder, env, expired)
                if LOCK_REFUSAL not in printed:
                    break
            else:
                return runs, lost, "the editor refused each of {} session launches for the project lock".format(
                    LOCK_ATTEMPTS)
            # Read again rather than taken from the last `expired`, which the editor can outlive by a sample.
            try:
                where.update(json.loads(progress.read_text()))
            except (OSError, ValueError):
                pass
            reached = end if done.exists() else where.get("position", position)
            for passed in range(position, min(reached, end)):
                runs[passed] = {"launch": launches, "peak": peaks.get(passed, 0)}
            if failed.exists():
                return runs, lost, "the runner stopped: {}".format(failed.read_text().strip())
            if stuck:
                return runs, lost, "a domain reload did not complete within {}s".format(SESSION_RELOAD_BOUND)
            if done.exists():
                position = end
                break
            if not where or build_error(log):
                return runs, lost, "the session never reached its first stage; read {}".format(log)
            # The item it died in takes its own launch; the next launch starts past it.
            lost.add(reached)
            if items[reached]["id"] == SESSION_OPENING:
                return runs, lost, "the session died in the areas' own baselines; read {}".format(log)
            position = reached + 1
    return runs, lost, None


def confirmations(plan, directory):
    """Confirmation position -> killing case -> whether it passed there under the unmutated program."""
    found = {}
    for position, item in enumerate(plan["items"]):
        if not item.get("confirm"):
            continue
        result = read_item(directory, item["id"]) or {}
        stage = (result.get("stages") or [{}])[0]
        if not stage.get("finished") or not stage.get("xml"):
            continue
        try:
            root = ET.parse(stage["xml"]).getroot()
        except (OSError, ET.ParseError):
            continue
        found[position] = {case.get("fullname"): case.get("result") == "Passed" for case in root.iter("test-case")}
    return found


def confirmed_for(plan, position, runs, found):
    """What the confirmation closing `position`'s segment read, where that confirmation ran in the same
    launch as the item; otherwise nothing, so no kill of it stands."""
    closing = next((later for later in range(position + 1, len(plan["items"]))
                    if plan["items"][later].get("confirm")), None)
    if closing is None or closing not in runs or runs[closing]["launch"] != runs.get(position, {}).get("launch"):
        return {}
    return found.get(closing, {})


def measure_in_session(args, project, holder, output, mutants, pending, baseline_results, baseline,
                       baseline_wall, text_readers, whole_scope, campaign, scope):
    """Measures in one editor every mutant of `pending` the rewriter can place, and returns index ->
    (verdict, detail) for each whose reading stands, each recorded as `write_verdict` records it.
    Every other mutant is left to its own launches."""
    directory = output / "session"
    directory.mkdir(exist_ok=True)
    for stale in list(directory.glob("item-*.json")) + list(directory.glob("mutant-*.xml")):
        stale.unlink()
    request, declined = schemata_request(project, mutants, pending)
    answer = rewrite_schemata(args.unity, project, request, directory) if request["files"] else None
    if answer is None:
        return {}
    placed = [index for index in answer["placed"] if index in pending and index not in declined]
    declined.update({int(key): value for key, value in answer["declined"].items()})
    for index in sorted(declined):
        print("[{}] takes its own launch: {}".format(index, declined[index]))
    if answer["fatal"]:
        print("the rewriter stopped: {}".format("; ".join(answer["fatal"])))
    if not placed:
        return {}

    attempts = {}
    ceiling = min(args.timeout, baseline_wall)
    expected = {"whole": baseline["total"]}
    if not scope and args.platform in NARROWED_PLATFORMS:
        areas = {area_of(mutants[index - 1].path, project) for index in placed} - {None}
        for area, names in narrowable(baseline_results, areas).items():
            attempts[area] = excluding(["-assemblyNames", ";".join(names)], text_readers)
    plan, segments = session_plan(project, mutants, placed, attempts, whole_scope, args.platform, ceiling,
                                  args.timeout, directory)
    plan["segments"] = segments

    rewritten = {project / relative: text for relative, text in answer["files"].items()}
    if not wait_for_quiet(args.busy_timeout):
        raise SystemExit("another Unity test run is still in flight after {}s, so this "
                         "session's failures would not all be its mutants'".format(args.busy_timeout))
    holder.hold_files({path: (path.read_text(), text) for path, text in rewritten.items()},
                      "{} mutant(s) guarded for one session".format(len(placed)))
    started = time.time()
    try:
        for path, text in rewritten.items():
            path.write_text(text)
        runs, lost, stopped = run_session(args, project, plan, directory, holder)
    finally:
        if holder.release() is None:
            raise SystemExit("could not put the session's sources back; the record at {} names what is "
                             "outstanding".format(holder.sentinel))
    print("session: {} mutant(s) in {:.0f}s{}".format(len(placed), time.time() - started,
                                                      "; " + stopped if stopped else ""))
    if not runs:
        return {}

    opening = {stage["name"]: stage for stage in (read_item(directory, SESSION_OPENING) or {}).get("stages", [])}
    for name, stage in opening.items():
        counts = stage_counts(stage)
        expected[name] = counts["total"] if counts else None
    found = confirmations(plan, directory)
    il_read = il_reading_assemblies(project)
    measured = {}
    for position, item in enumerate(plan["items"]):
        index = item["id"]
        if item.get("confirm") or index == SESSION_OPENING or position not in runs:
            continue
        mutant = mutants[index - 1]
        assembly = assembly_of(mutant.path)
        reading = session_verdict(read_item(directory, index), item, index, assembly, expected, opening,
                                  confirmed_for(plan, position, runs, found), text_readers)
        if reading is not None and reading[0] in SURVIVING and assembly in il_read:
            # The session compiled every guard into the assembly, so a fixture reading its IL read the
            # union of the mutants rather than this one.
            reading = None
        if reading is None:
            print("[{}] the session's reading does not stand, so it takes its own launch".format(index))
            continue
        mutant.verdict, mutant.detail, killers = reading
        if runs[position]["peak"]:
            mutant.detail = "{}; {} other editor(s) were up".format(mutant.detail or "-", runs[position]["peak"])
        write_verdict(output, index, campaign, mutant, project, killers, scope)
        measured[index] = (mutant.verdict, mutant.detail)
    return measured


# --------------------------------------------------------------------------------------------------
# Deciding
# --------------------------------------------------------------------------------------------------

def relative_to(path, project):
    try:
        return path.relative_to(project)
    except ValueError:
        return path


def refusal(code, message):
    """Prints a refusal and hands back the status to exit with.

    A distinct status rather than 1, because a caller has to tell a campaign holding something from
    this script failing to run at all -- both stop a commit, and only one of them is about a campaign.
    """
    print(message)
    return code


def digestible(text):
    """The file with its comments removed, and the lines they emptied with them.

    What a verdict record is keyed on has to move when what a campaign measured moves, and no
    further. A comment carries no mutant -- `mask_spans` reads it as something other than code, and
    the generator skips it -- so an edit to one voids a record over a change no operator could have
    seen, and a review round here is largely prose.

    A string literal is masked for generation and kept here, because editing one changes behaviour a
    mutant could have covered. A directive is kept for the same reason: it decides what compiles. A
    blank line inside a verbatim string is part of the value, and a verbatim span is the only one
    `mask_spans` reads across lines, so a line holding one is never dropped as empty.
    """
    keep = [True] * len(text)
    quoted = [False] * len(text)
    for start, end, kind in mask_spans(text):
        for offset in range(start, min(end, len(text))):
            if kind in (LINE_COMMENT, BLOCK_COMMENT):
                keep[offset] = False
            elif kind == VERBATIM:
                quoted[offset] = True

    lines, held, spans = [], [], False
    for offset, character in enumerate(text):
        if character == "\n":
            # The newline too: a line that is empty inside a verbatim string holds nothing else to
            # ask, and it is the one this has to keep.
            lines.append(("".join(held), spans or quoted[offset]))
            held, spans = [], False
            continue
        spans = spans or quoted[offset]
        if keep[offset]:
            held.append(character)
    lines.append(("".join(held), spans))
    return "\n".join(line for line, held_a_span in lines if line.strip() or held_a_span)


def scope_digest(base, targets, project, platform):
    """What a campaign measured, in a form a later check can compare a tree against.

    Not the head tree: the campaign diffs the merge base against the **working tree**, so an
    uncommitted edit to a mutated file changes what it measured and moves no tree sha at all --
    measured, and it is a record that would validate a run taken before the edit. Nor the head tree
    for a second reason: 16 of 44 commits over five recent branches changed no mutable production
    file, and each would have voided every record over a change no operator can see.

    What it does not cover is a test-side change. Removing a test can make a killed mutant survive,
    and this stays valid across it; including tests would void the records on the ordinary act of
    adding one after the run, which is most of a branch's commits.

    Nor a comment: `digestible` takes them out before the hash, so a record survives the prose
    correction a review round leaves behind. Anything narrower than the whole bytes has to be
    checked for what it stops voiding on, and this stops on exactly the spans the generator already
    refuses to mutate.
    """
    parts = ["mutation-model:{}".format(MUTATION_MODEL_VERSION), base, platform]
    for path in sorted(targets, key=str):
        # Repository-relative, so a record does not depend on where the checkout sits: a resolved
        # path and an unresolved one reach the same file and digest differently.
        parts.append("{}:{}".format(
            relative_to(path, project).as_posix(),
            hashlib.sha256(digestible(path.read_text(encoding="utf-8")).encode()).hexdigest()))
    return hashlib.sha256("\n".join(parts).encode()).hexdigest()


def verdict_path(output, index):
    return output / "mutant-{:03d}.json".format(index)


def write_verdict(output, index, digest, mutant, project, killers=(), scope=()):
    """Record one mutant's verdict beside its results, with everything `read_verdict` keys it on."""
    verdict_path(output, index).write_text(json.dumps({
        "digest": digest, "scope": list(scope), "index": index, "mutant": mutant.describe(project),
        "column": mutant.column,
        "verdict": mutant.verdict, "detail": mutant.detail,
        # Every case that failed in the run that decided the verdict, not the three the detail names:
        # for a kill the narrowed run took, the area's cases alone, since the whole suite never ran.
        # Which cases kill which mutant is a reading the decision does not carry, and it is on disk
        # here anyway -- the detail truncates it for a reader rather than because that is all there was.
        "killers": sorted(killers),
    }, indent=2))


def read_verdict(output, index, digest, mutant, project, scope=()):
    """(verdict, detail) of the kill a previous run of this same campaign recorded for this mutant, or
    None.

    Keyed on the digest, which covers the working tree rather than a commit, so an edit to a mutated
    file since moves it and the record is refused rather than reused. On the suite `scope` narrows
    the run to, because a kill another suite took can hide a fixture that does not notice, and one
    taken under `--filter` or `--assemblies` is not the whole-suite reading a decision stands on. And on
    the mutant with its column, because an index is only this mutant's while the list is the same list
    -- `--files` takes a file whole where the diff takes its changed lines -- and two mutants on one
    line can describe alike.

    A kill that named its failing tests alone: each other verdict turns on something outside that key
    -- a survivor of either kind on the tests, which the test written for it changes; a timed-out or
    hung mutant on `--timeout` and the baseline's wall clock, and on anything else that kept the
    editor running; uncompilable and not rebuilt on the editor as well as on the mutation. A kill
    turns on the tests too, and is kept across a test removed since for the reason `scope_digest`
    gives for its key; one taken while another editor was up is kept as well, and its detail says so.
    """
    held = recorded(output, index, digest, mutant, project, scope)
    if held is None or held.get("verdict") != KILLED:
        return None
    return held.get("verdict"), held.get("detail")


def recorded(output, index, digest, mutant, project, scope=()):
    """The record `write_verdict` left for this mutant under `output`, whatever its verdict, or None
    where there is none or it was written for another mutant, tree or scope."""
    try:
        held = json.loads(verdict_path(output, index).read_text())
    except (OSError, ValueError):
        return None
    key = (held.get("digest"), held.get("scope"), held.get("mutant"), held.get("column"))
    if key != (digest, list(scope), mutant.describe(project), mutant.column):
        return None
    return held


def collected(directories, index, digest, mutant, project, scope=()):
    """(verdict, detail) a shard recorded for this mutant in one of `directories`.

    Any verdict, where `read_verdict` keeps only a kill: a survivor or an unmeasured verdict turns on
    the tests and the bounds, which the key does not hold, and a resumed run can come after either
    moved. The workflow hands this the shards of one run, over one commit.
    """
    for directory in directories:
        held = recorded(Path(directory), index, digest, mutant, project, scope)
        if held is not None:
            return held.get("verdict"), held.get("detail") or ""
    return UNRECORDED, "no shard's output holds a verdict for it"


def parse_shard(text):
    """(K, N) from `K/N`: the zero-based shard K of N."""
    match = re.fullmatch(r"(\d+)/(\d+)", text)
    if not match or int(match.group(1)) >= int(match.group(2)):
        raise argparse.ArgumentTypeError("expected K/N with 0 <= K < N, got {!r}".format(text))
    return int(match.group(1)), int(match.group(2))


def in_shard(position, shard):
    """Whether the 1-based `position` is this shard's to run."""
    return shard is None or (position - 1) % shard[1] == shard[0]


def shard_count(mutants, platform="EditMode"):
    return min(MAX_SHARDS, -(-mutants // SHARD_SIZE[platform]))


def sharded(chosen, shard):
    """The indices in `chosen` that `shard` measures, dealt by position in `chosen` rather than by
    index: a second pass chooses the first pass's survivors, and dealt by index, two survivors at
    indices 2 and 4 both go to the second of two shards."""
    return [index for position, index in enumerate(chosen, start=1) if in_shard(position, shard)]


def reach(mutants, unreached, project):
    """What the campaign was able to ask about, printed beside whatever it then answers.

    The lines are named rather than counted, because the count alone is a number nobody has to act on.
    """
    reached = {(mutant.path, mutant.line) for mutant in mutants}
    left = sum(len(lines) for lines in unreached.values())
    report = ["{} mutant(s) over {} changed code line(s); {} line(s) no operator reaches".format(
        len(mutants), len(reached) + left, left)]
    for path, lines in sorted(unreached.items(), key=lambda item: str(item[0])):
        where = relative_to(path, project)
        shown = ",".join(str(number) for number in lines[:LINES_LISTED])
        rest = "" if len(lines) <= LINES_LISTED else " and {} more".format(len(lines) - LINES_LISTED)
        report.append("  unreached  {}:{}{}".format(where, shown, rest))
    return "\n".join(report)


def declarations_for(targets, changed):
    """(path, subject line) -> the declaration answering for it, over every file being mutated.

    `changed` is the branch's own lines, and a declaration outside them was written for a change the
    base already carries.
    """
    found = {}
    for path in targets:
        for subject, declaration in declarations_in(path.read_text()):
            declaration.written_here = declaration.written_in(changed.get(path, set()))
            found[(path, subject)] = declaration
    return found


def answered(mutants, deferred, declared):
    """(the survivors nothing answers for, the declarations nothing is left for them to answer).

    A declaration is stale when the line under it produced no survivor -- because the mutants there
    all died, or because there were none to begin with. Both mean it describes a state the tree is
    no longer in, and a declaration that outlives what it describes silences whatever lands there
    next. A line the cap left unrun is neither: it says nothing about that line at all, and the cap
    fails the run on its own.
    """
    surviving = {(mutant.path, mutant.line) for mutant in mutants if mutant.verdict in SURVIVING}
    unanswered = []
    for mutant in mutants:
        if mutant.verdict not in SURVIVING:
            continue
        declaration = declared.get((mutant.path, mutant.line))
        if declaration is not None and declaration.operator not in (None, mutant.operator):
            # Named for a different mutant on this line, so it says nothing about this one.
            unanswered.append((mutant, "the declaration above answers for '{}', not for this".format(
                declaration.operator)))
            continue
        if declaration is None:
            unanswered.append((mutant, "nothing above this line answers for it"))
        elif not declaration.written_here:
            unanswered.append((mutant, "its declaration is the base's own; restate it for this change"))
        elif declaration.complaint:
            unanswered.append((mutant, declaration.complaint))
        else:
            mutant.detail = "{}: {}".format(declaration.category, declaration.reason)
    # Grouped by the declaration rather than by the line, because one covering a condition broken
    # over two lines has a survivor on either of them and is stale only when neither carries one.
    settled = surviving | deferred
    # A named declaration is settled by a survivor of its own operator rather than by any survivor on
    # the line: without that, a sibling still surviving would keep a stale one from being reported,
    # which is the widening this naming exists to close read from the other side.
    named = {(mutant.path, mutant.line, mutant.operator)
             for mutant in mutants if mutant.verdict in SURVIVING}
    covers = {}
    for (path, subject), declaration in declared.items():
        covers.setdefault((path, declaration.line), [declaration, []])[1].append(subject)
    stale = []
    for (path, _), (declaration, subjects) in sorted(
            covers.items(), key=lambda item: (str(item[0][0]), item[0][1])):
        if not declaration.written_here:
            continue
        if declaration.operator is None:
            answered_here = any((path, subject) in settled for subject in subjects)
        else:
            answered_here = any((path, subject, declaration.operator) in named
                                or (path, subject) in deferred for subject in subjects)
        if not answered_here:
            stale.append((path, min(subjects), declaration))
    return unanswered, stale


def measure(args, project, holder, output, targets, mutants, scope, campaign, coverage, selected):
    """Waits for the machine, takes the baseline and gives each mutant whose index is in `selected`
    its verdict, recording each one under `output` as it is reached."""
    if not wait_for_quiet(args.busy_timeout):
        raise SystemExit("another Unity test run is still in flight after {}s".format(args.busy_timeout))

    print(coverage)

    # Before the baseline, not before the loop: the guard reaps the editor as well as restoring, and
    # the baseline's editor outliving a killed campaign holds the project lock against the next one.
    holder.guard()
    baseline_results = output / "baseline.xml"
    # Derived once: which fixtures redden on the edit rather than on what it does.
    text_readers = text_reading_fixtures(project)
    # The launch carries the editor arguments as well; `scope` alone is what a verdict is keyed on.
    launched = excluding(scope, text_readers) + args.editor_arg
    baseline_wall, baseline_timed_out, _ = run_suite(args.unity, project, args.platform, launched,
                                                  baseline_results, output / "baseline.log",
                                                  args.timeout, holder)
    if baseline_timed_out:
        raise SystemExit("the baseline run did not finish within --timeout, so no mutant can be timed")
    baseline = read_counts(baseline_results)
    if baseline is None:
        raise SystemExit("the baseline run wrote no result; read {}".format(output / "baseline.log"))
    if baseline["failed"] or baseline["inconclusive"]:
        raise SystemExit("baseline is not green, so a mutant would read as killed by {}".format(
            ", ".join(failing_names(baseline_results)) or "an inconclusive case"))
    print("baseline: {} passed in {:.0f}s".format(baseline["passed"], baseline_wall))

    # A mutant whose assembly comes out byte-identical to this ran the unmutated code, and a run
    # over the pristine binary is green for the same reason a surviving mutant is. Writing the
    # file is not evidence that the editor compiled it.
    # Reading the editor log for a compile line was tried instead and does not answer it — the
    # line appears for an artifact the build cache served without compiling anything.
    # This detects an edit that never reached the compiler, and only that. A mutation the
    # compiler read and discarded, inside a preprocessor branch the editor does not define,
    # still produces a different assembly here and so reads as survived.
    assemblies_dir = project / "Library" / "ScriptAssemblies"
    baseline_hashes = {path.name: sha(path) for path in assemblies_dir.glob("*.dll")}

    # The mutants the rewriter can place are measured in one editor, and each of the rest -- and each
    # whose session reading does not stand -- takes its own launches below, as every mutant once did.
    # Not under --editor-arg: the runner starts each stage itself, so an argument meant for the test
    # framework would scope the baseline and the own launches and not the session's stages.
    in_session = {}
    if args.editor_arg and not args.launch_per_mutant:
        print("--editor-arg reaches no session stage, so every mutant takes its own launches")
    if not args.launch_per_mutant and not args.editor_arg:
        pending = [index for index in sorted(selected) if read_verdict(
            output, index, campaign, mutants[index - 1], project, scope) is None]
        if pending:
            in_session = measure_in_session(args, project, holder, output, mutants, pending,
                                            baseline_results, baseline, baseline_wall, text_readers,
                                            excluding(scope, text_readers), campaign, scope)
    own = set(selected) - set(in_session)

    # Each area's own baseline, on the tree the whole one just built. A case that fails whenever its
    # area's assemblies run by themselves would otherwise read as a kill of every mutant there that
    # built. Not
    # taken under a scope the caller chose, which already asks a question of its own.
    attempts = {}
    ceiling = min(args.timeout, baseline_wall)
    if not scope and args.platform in NARROWED_PLATFORMS:
        areas = {area_of(mutants[index - 1].path, project) for index in own} - {None}
        for area, names in narrowable(baseline_results, areas).items():
            attempt = excluding(["-assemblyNames", ";".join(names)], text_readers) + args.editor_arg
            results = output / "baseline-{}.xml".format(area.name)
            if results.exists():
                results.unlink()
            area_wall, timed_out, _ = run_suite(args.unity, project, args.platform, attempt, results,
                                                output / "baseline-{}.log".format(area.name), ceiling,
                                                holder)
            counts = read_counts(results)
            if timed_out or not counts or counts["failed"] or counts["inconclusive"] or not counts["passed"]:
                print("{} alone did not pass green, so its mutants run on the whole suite".format(
                    ", ".join(names)))
                continue
            bound = min(ceiling, area_wall * NARROW_MARGIN)
            print("{} alone passed in {:.0f}s, so each of its mutants is given {:.0f}s there".format(
                ", ".join(names), area_wall, bound))
            attempts[area] = (attempt, names, bound, counts["total"])

    originals = {path: path.read_text() for path in targets}
    started = time.time()
    resumed = measured = 0
    try:
        for index, mutant in enumerate(mutants, start=1):
            if index not in selected:
                continue
            print("[{}/{}] {}".format(index, len(mutants), mutant.describe(project)), flush=True)
            if index in in_session:
                mutant.verdict, mutant.detail = in_session[index]
                measured += 1
                print("      {} ({}) in the session".format(mutant.verdict, mutant.detail or "-"))
                continue
            kept = read_verdict(output, index, campaign, mutant, project, scope)
            if kept is not None:
                mutant.verdict, mutant.detail = kept
                resumed += 1
                print("      {} ({}) from a previous run of this campaign".format(
                    mutant.verdict, mutant.detail or "-"))
                continue
            results = output / "mutant-{:03d}.xml".format(index)
            log = output / "mutant-{:03d}.log".format(index)
            # The log as well, since a verdict is read off its last line and a launch that stops
            # before writing one would leave an earlier campaign's there.
            for stale in (results, log):
                if stale.exists():
                    stale.unlink()
            # Queue first, mutate second. The wait runs to --busy-timeout, half an hour by default,
            # and there is nothing the campaign needs on disk across it.
            if not wait_for_quiet(args.busy_timeout):
                raise SystemExit("another Unity test run is still in flight after {}s, so this "
                                 "mutant's failures would not all be its own".format(args.busy_timeout))
            mutated = apply_mutation(originals[mutant.path], mutant)
            holder.hold(mutant.path, originals[mutant.path], mutated, mutant.describe(project))
            mutant.path.write_text(mutated)
            dll = assemblies_dir / "{}.dll".format(assembly_of(mutant.path))
            attempt, narrowed_to, narrow_bound, area_cases = attempts.get(
                area_of(mutant.path, project), (None, (), 0, 0))
            early, wall, neighbours, late = [], 0.0, 0, False
            if attempt is not None:
                narrowed = output / "mutant-{:03d}-narrowed.xml".format(index)
                narrowed_log = output / "mutant-{:03d}-narrowed.log".format(index)
                if narrowed.exists():
                    narrowed.unlink()
                since = time.time()
                wall, timed_out, neighbours = run_suite(args.unity, project, args.platform, attempt,
                                                        narrowed, narrowed_log, narrow_bound, holder)
                if timed_out and complete_result(narrowed, since, area_cases):
                    timed_out, late = False, True
                early = narrowed_kill(narrowed, narrowed_log, timed_out, dll, baseline_hashes,
                                      text_readers)
                # A complete narrowed pass is no verdict, so the whole suite still runs.
                late = late and bool(early)
            timed_out = False
            if not early:
                since = time.time()
                spent, timed_out, seen = run_suite(args.unity, project, args.platform, launched,
                                                   results, log, args.timeout, holder)
                wall, neighbours = wall + spent, max(neighbours, seen)
                if timed_out and complete_result(results, since, baseline["total"]):
                    timed_out, late = False, True
            if holder.release() is None:
                # The record is still there naming a file still mutated. Going on would apply the
                # next mutation over this one and end by restoring the wrong text.
                raise SystemExit("could not put {} back, so the campaign cannot continue; the record "
                                 "at {} names what is outstanding".format(mutant.path, holder.sentinel))

            counts = read_counts(results)
            killers = ()
            blamed = build_error(log)
            if early:
                killers = early
                behavioural = killed_by_behaviour(early, text_readers)
                mutant.verdict = KILLED
                mutant.detail = "{} failed in {}: {}".format(
                    len(behavioural), ", ".join(narrowed_to),
                    ", ".join(name.split(".")[-1] for name in behavioural[:3]))
            # Only where no result could be read: a readable one is the suite's own reading, and a
            # kill taken from the wall clock would override it. One short of complete is left to
            # TIMED_OUT.
            elif timed_out and counts is None and baseline_wall * HANG_MARGIN <= args.timeout:
                mutant.verdict = HUNG
                mutant.detail = ("the suite ran past --timeout {}s and left no readable result, where "
                                 "the baseline finished in {:.0f}s".format(args.timeout, baseline_wall))
            elif timed_out and counts is not None:
                mutant.verdict = TIMED_OUT
                mutant.detail = ("the editor was killed at --timeout {}s after writing a result; read "
                                 "the log for what kept it running".format(args.timeout))
            elif timed_out:
                # The baseline was already close to the bound, so a mutant reaching it says nothing
                # about the mutation. One of the two readings is a mutant nobody asked about, so the
                # run refuses and says which to take.
                mutant.verdict = TIMED_OUT
                mutant.detail = ("the editor was killed at --timeout {}s and the baseline took {:.0f}s; "
                                 "raise it, or read the log for a mutation that does not terminate"
                                 .format(args.timeout, baseline_wall))
            elif blamed and operator_does_not_apply(log, mutant, project, statement_start(
                    originals[mutant.path], mutant.line)):
                mutant.verdict = INAPPLICABLE
                mutant.detail = "CS0019: '{}' has no overload for these operands".format(mutant.after)
            elif blamed:
                mutant.verdict = UNCOMPILABLE
                mutant.detail = "the build stopped in {}".format(blamed)
            elif counts is None and lock_refused(log):
                mutant.verdict = LOCKED
                mutant.detail = ("the editor refused each of {} launches because another held the "
                                 "project; read the log".format(LOCK_ATTEMPTS))
            elif counts is None:
                mutant.verdict = UNCOMPILABLE
                mutant.detail = "the runner wrote no result"
            elif sha(dll) == baseline_hashes.get(dll.name):
                mutant.verdict = NOT_BUILT
                mutant.detail = "{} is byte-identical to the baseline build".format(dll.name)
            elif counts["failed"]:
                # Naming the killers, because a mutant killed only by a test that also fails on an
                # unmutated tree was not killed by anything.
                names = failing_names(results)
                killers = names
                behavioural = killed_by_behaviour(names, text_readers)
                if behavioural:
                    mutant.verdict = KILLED
                    mutant.detail = "{} failed: {}".format(
                        len(behavioural),
                        ", ".join(name.split(".")[-1] for name in behavioural[:3]))
                else:
                    mutant.verdict = SURVIVED
                    mutant.detail = "{} failed, every one a fixture reading this tree's text: {}".format(
                        counts["failed"], ", ".join(name.split(".")[-1] for name in names[:3]))
            elif counts["inconclusive"]:
                mutant.verdict = INCONCLUSIVE
                mutant.detail = "{} inconclusive, 0 failed".format(counts["inconclusive"])
            else:
                mutant.verdict = SURVIVED
            if late:
                mutant.detail = "{}; read from a complete result the editor wrote before its bound " \
                                "killed it".format(mutant.detail or "-")
            if neighbours:
                mutant.detail = "{}; {} other editor(s) were up".format(
                    mutant.detail or "-", neighbours)
            write_verdict(output, index, campaign, mutant, project, killers, scope)
            measured += 1
            average = (time.time() - started) / measured
            left = sum(1 for later in selected if later > index)
            print("      {} ({}) in {:.0f}s; {:.0f}s left at {:.0f}s each".format(
                mutant.verdict, mutant.detail or "-", wall, average * left, average))
    finally:
        holder.release()
        for path, text in originals.items():
            path.write_text(text)

    if resumed:
        print("\n{} of {} verdict(s) came from a previous run of this campaign, which is why the "
              "wall\nclock below is shorter than the work it reports".format(resumed, len(mutants)),
              flush=True)


def plan_survivors(args, coverage, mutants, chosen, first):
    """The second pass's plan: the shards its platform needs for the first pass's survivors."""
    print(coverage)
    per = SHARD_CEILING[args.platform]
    if len(chosen) > MAX_SHARDS * per:
        print("{} mutants survived the {} suite, more than {} shards of {} can measure on {} inside "
              "the shard job's timeout.\nWrite the tests they ask for, or split the pull request."
              .format(len(chosen), first, MAX_SHARDS, per, args.platform))
        return CEILING_REFUSAL
    print("{} of {} mutant(s) survived the {} suite and are measured on {}".format(
        len(chosen), len(mutants), first, args.platform))
    print("mutants={}".format(len(chosen)))
    print("shards={}".format(json.dumps(list(range(shard_count(len(chosen), args.platform))))))
    return 0


def report_shard(mutants, selected, shard):
    """What one shard measured, and no decision over it. Which survivors a declaration answers for and
    which declarations are stale are both readings over every mutant of the diff, and a shard holds a
    slice of them: `--collect` takes the decision once the slices are together."""
    ran = [mutants[index - 1] for index in selected]
    tally = {}
    for mutant in ran:
        tally[mutant.verdict] = tally.get(mutant.verdict, 0) + 1
    print("\nshard {} of {}: {}".format(shard[0], shard[1], ", ".join(
        "{}: {}".format(key, value) for key, value in sorted(tally.items())) or "no mutant"))
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", default=".", help="Unity project root (default: cwd)")
    parser.add_argument("--base", default="main",
                        help="branch to diff against, read at refs/remotes/origin/BASE where "
                             "both that and refs/heads/BASE exist (default: main)")
    parser.add_argument("--files", nargs="*", help="mutate these files whole instead of a diff")
    parser.add_argument("--platform", default="EditMode", choices=["EditMode", "PlayMode"])
    parser.add_argument("--assemblies", help="comma-separated test assemblies; default is every one")
    parser.add_argument("--filter", help="fixture or test name, as -testFilter takes it. Narrowing to one "
                                         "fixture turns the run into a question about that fixture alone: "
                                         "a mutant surviving it is a mutant the fixture does not notice, "
                                         "which the whole suite hides whenever any other test does")
    parser.add_argument("--max", type=int, default=40,
                        help="run only the first this many mutants, in file order (default: 40)")
    parser.add_argument("--list", action="store_true", help="print the mutants and exit")
    parser.add_argument("--refusals", action="store_true",
                        help="print the join refusal's census over the package and exit; redirect it "
                             "over " + REFUSAL_BASELINE + " to record a deliberate change to it")
    parser.add_argument("--timeout", type=int, default=900,
                        help="seconds before a baseline or mutant editor is killed; Generators~/README.md ▸ "
                             "The Unity assemblies says which verdict that is (default: 900)")
    parser.add_argument("--busy-timeout", type=int, default=1800,
                        help="seconds to wait for another Unity run to finish (default: 1800)")
    parser.add_argument("--output", default="", help="directory for the per-mutant logs and XML")
    parser.add_argument("--unity", default=DEFAULT_UNITY, help="editor binary (default: the pinned macOS one)")
    parser.add_argument("--editor-arg", action="append", default=[], metavar="ARG",
                        help="an argument added to every editor launch, spelt --editor-arg=-flag; "
                             "repeatable")
    parser.add_argument("--launch-per-mutant", action="store_true",
                        help="give every mutant its own editor launches rather than measuring the "
                             "mutants the rewriter can place in one session")
    parser.add_argument("--restore", action="store_true",
                        help="put back the mutation an interrupted campaign left, and stop")
    parser.add_argument("--carried", nargs="*",
                        help="paths something is about to record; refuses when one of them is the "
                             "source a campaign is holding a mutation in")
    parser.add_argument("--emit-lines",
                        help="write every mutant this package generates as {path, line, text} and "
                             "stop, for a reader that parses them with something other than this "
                             "script's own model of C#")
    parser.add_argument("--plan", action="store_true",
                        help="print how many mutants the diff generates and the shards a split run "
                             "would use, as mutants=N and shards=[...] lines, and stop")
    parser.add_argument("--shard", type=parse_shard, metavar="K/N",
                        help="measure only every Nth mutant from the (K+1)th and record their "
                             "verdicts, leaving the decision to --collect")
    parser.add_argument("--collect", nargs="+", metavar="DIR",
                        help="decide the campaign from the verdicts --shard runs recorded in these "
                             "directories, without an editor")
    parser.add_argument("--survivors-of", nargs="+", metavar="DIR",
                        help="take each mutant's verdict from the run on the other platform that "
                             "recorded it in these directories, and measure, plan or collect on "
                             "--platform only the mutants that run left surviving")
    args = parser.parse_args()
    if args.shard is not None and args.collect is not None:
        parser.error("--shard measures and --collect decides; a run does one of them")
    if args.survivors_of is not None:
        # One file name per mutant index in either pass, so a pass writing where the other's records
        # are overwrites the verdicts it was chosen by.
        read = {Path(name).resolve() for name in args.survivors_of}
        written = [Path(args.output or Path(args.project) / "Logs" / "mutation_check").resolve()]
        written += [Path(name).resolve() for name in args.collect or ()]
        if read & set(written):
            parser.error("--survivors-of names a directory this run writes or collects from; the "
                         "two passes keep their records apart")

    project = Path(args.project).resolve()
    holder = Holder(project / SENTINEL)

    if args.carried is not None:
        # A campaign's mutation reads as an ordinary edit in a file the branch is already touching,
        # and `git add -u` stages it with the rest. Naming it in `git status` was not enough on its
        # own: this was written after one reached a commit that way with the record sitting beside it.
        outstanding = holder.outstanding()
        if outstanding is None:
            return 0
        if outstanding is UNREADABLE:
            # Which file it names is exactly what cannot be read, so every path is a candidate. A
            # record is damaged by things the campaign never does -- somebody clearing what
            # `git status` reported, a permission change, a directory left in its place.
            sys.exit(refusal(CARRIED_REFUSAL,
                             "a mutation campaign is holding something, and {} cannot be read to "
                             "say what.\nNo file here can be recorded until that is resolved:\n"
                             "  python3 scripts/test_quality/mutation_check.py --restore".format(
                                 holder.sentinel)))
        # Resolved on both sides: a macOS temporary directory reaches the same file through /var and
        # through /private/var, and comparing the spellings finds no match where there is one.
        held = {Path(entry.get("source", "")).resolve() for entry in held_sources(outstanding)}
        for name in args.carried:
            candidate = Path(name)
            candidate = candidate if candidate.is_absolute() else project / name
            if candidate.resolve() in held:
                sys.exit(refusal(CARRIED_REFUSAL,
                                 "a mutation campaign is holding {} -- {}\n"
                                 "Recording it now captures the campaign's edit, not yours. Wait for "
                                 "the campaign,\nor put the file back with\n"
                                 "  python3 scripts/test_quality/mutation_check.py --restore".format(
                                     name, outstanding.get("mutation", "<unnamed>"))))
        return 0

    if args.restore:
        outstanding = holder.outstanding()
        if outstanding is None:
            print("no mutation is outstanding")
            return 0
        if outstanding is not UNREADABLE:
            # A record survives a SIGKILL, so an author can see the modified file, keep working on it
            # for an hour and then run this. Writing the recorded original back would take that hour
            # with it, and the word this prints afterwards is "restored".
            for entry in held_sources(outstanding):
                source = Path(entry.get("source", ""))
                on_disk = hashlib.sha256(source.read_bytes()).hexdigest() if source.exists() else ""
                known = (entry.get("mutated_sha"), entry.get("original_sha"))
                if not on_disk or on_disk in known:
                    continue
                raise SystemExit(
                    "{} holds neither the mutation {} recorded nor the original it replaced, so "
                    "something\nelse has written it since. Nothing here can tell your work from the "
                    "campaign's:\nkeep what is there, or take the original out of the record by "
                    "hand.".format(source, holder.sentinel))
        held = holder.release()
        if held is None:
            raise SystemExit("{} names a mutation this could not put back; read it and restore by "
                             "hand".format(holder.sentinel))
        print("restored {} ({})".format(held_names(held), held["mutation"]))
        return 0

    # Before anything is read, because everything below reads the working tree: the baseline would be
    # taken over somebody else's mutation, every mutant would be applied on top of it, and the restore
    # at the end would write it back as though it were the author's own code.
    outstanding = holder.outstanding()
    if outstanding is not None:
        names = ("<unreadable>", "<unreadable>") if outstanding is UNREADABLE else (
            held_names(outstanding), outstanding.get("mutation", "<unnamed>"))
        raise SystemExit(
            "a campaign is holding a mutation in this tree, so nothing here can be measured:\n"
            "  {} -- {}\n"
            "Wait for it if one is running; otherwise put it back with\n"
            "  python3 scripts/test_quality/mutation_check.py --restore".format(*names))

    if args.refusals:
        print("\n".join(refusal_census(project)))
        return 0

    # Presence, not truth, the same as --carried above: the flag selects a mode, and an empty operand
    # read as absence falls through to the campaign -- which mutates a source, under a flag whose whole
    # contract is that it writes none.
    if args.emit_lines is not None:
        # The applied line only, not the applied file: 11301 whole files is gigabytes, and the reader
        # holds the originals anyway. What it gets from here is this script's edit, not its opinion
        # of whether the edit parses -- that is the half it exists to answer independently.
        emitted = []
        for source in sorted(project.glob("Packages/com.velvet.core/**/*.cs")):
            if not mutable(source, project):
                continue
            text = source.read_text()
            numbers = set(range(1, len(text.splitlines()) + 1))
            for mutant in mutations_for(source, text, numbers):
                applied = apply_mutation(text, mutant).splitlines()
                emitted.append({
                    "path": relative_to(source, project).as_posix(),
                    "line": mutant.line,
                    "operator": mutant.operator,
                    "text": applied[mutant.line - 1] if mutant.line <= len(applied) else "",
                })
        Path(args.emit_lines).write_text(json.dumps(emitted, indent=1))
        print("{} mutant(s) written to {}".format(len(emitted), args.emit_lines))
        return 0

    output = Path(args.output).resolve() if args.output else project / "Logs" / "mutation_check"
    output.mkdir(parents=True, exist_ok=True)
    scope = []
    if args.assemblies:
        scope += ["-assemblyNames", ";".join(name.strip() for name in args.assemblies.split(","))]
    if args.filter:
        scope += ["-testFilter", args.filter]

    # A declaration answers for a change measured against the whole suite. Every narrowing asks a
    # different question -- whether this file, this fixture or this assembly notices -- and under one
    # nearly everything survives, so a declaration earned there would be well-formed, branch-written
    # and indistinguishable in the tree from one earned against the suite. `--filter` and
    # `--assemblies` still take the diff's scope; what they lose is the right to sign anything off.
    whole = not (args.files or args.filter or args.assemblies)
    changed = {} if args.files else changed_files_and_lines(project, args.base)
    if args.files:
        targets = {}
        for name in args.files:
            path = Path(name).resolve()
            if mutable(path, project):
                targets[path] = set(range(1, len(path.read_text().splitlines()) + 1))
            else:
                print("skipping {}: not a mutable package source".format(name))
    else:
        targets = {path: lines for path, lines in changed.items() if mutable(path, project)}

    # A file the mask misreads has offsets no mutant is ever generated from, and the campaign reports
    # that as a line with nothing to ask rather than as a reading it could not take.
    blinded = [(path, defects) for path in sorted(targets) for defects in [mask_defects(path.read_text())]
               if defects]
    if blinded:
        raise SystemExit("\n".join(
            ["the comment-and-string mask swallows code in these files, so mutants there are missing "
             "with nothing saying which:"]
            + ["  {} lines {}-{} read as a {}".format(path, first, last, kind)
               for path, defects in blinded for first, last, kind in defects]))

    mutants = []
    unreached = {}
    for path, lines in sorted(targets.items()):
        text = path.read_text()
        found = mutations_for(path, text, lines)
        mutants.extend(found)
        covered = {mutant.line for mutant in found}
        left = [number for number in code_line_numbers(text, lines) if number not in covered]
        if left:
            unreached[path] = left
    coverage = reach(mutants, unreached, project)

    if args.plan and args.survivors_of is None:
        if len(mutants) > MAX_SHARDS * SHARD_CEILING[args.platform]:
            print(coverage)
            print("{} mutants is more than {} shards of {} can measure inside the shard job's "
                  "timeout.\nSplit the pull request.".format(len(mutants), MAX_SHARDS,
                                                             SHARD_CEILING[args.platform]))
            return CEILING_REFUSAL
        print(coverage if (mutants or unreached) else "no mutable change; no campaign is owed")
        if unreached and not mutants:
            # Passed rather than refused, where a local run refuses: a change with nothing to ask
            # cannot earn a pass any other way, so the lines are named and the pull request says why.
            print("No operator reaches any of the changed code lines above, so nothing is measured "
                  "and the\ncampaign passes. Say in the pull request why the change is not something "
                  "a mutation\ncan ask about.")
        print("mutants={}".format(len(mutants)))
        print("shards={}".format(json.dumps(list(range(shard_count(len(mutants), args.platform))))))
        return 0

    if not mutants:
        # Which of the two happens is decided by the lines, not by the kind of change: a change that
        # touched no code line at all has nothing to ask and passes, and one that touched code lines
        # no operator reaches refuses, because the verdict would be about no line. A rename lands on
        # either side depending on what its lines carry -- measured, twice, on both.
        if unreached:
            print(coverage)
            left = sum(len(lines) for lines in unreached.values())
            raise SystemExit(
                "no operator reaches any of the {} changed code line(s) above, so a verdict here "
                "would\nbe about nothing. Read them, and widen the operators or say in the pull "
                "request why\nthe change is not something a mutation can ask about.".format(left))
        print("no mutable change found")
        return 0
    if args.list:
        for mutant in mutants:
            print(mutant.describe(project))
        print(coverage)
        print("a run covers the first {}".format(args.max))
        return 0

    deferred = {(mutant.path, mutant.line) for mutant in mutants[args.max:]}
    truncated = len(mutants) - args.max
    mutants = mutants[:args.max]

    # What each verdict record is keyed on, which is how `--collect` knows a shard's record is about
    # this tree.
    base = merge_base_of(project, args.base)
    campaign = scope_digest(base, targets, project, args.platform)
    chosen = list(range(1, len(mutants) + 1))
    first = None
    if args.survivors_of is not None:
        # Read under the other platform's key, so a record this platform wrote is never taken as the
        # first pass's. A mutant without one is unrecorded, which fails the decision, and is not asked
        # about again here.
        first = OTHER_PLATFORM[args.platform]
        earlier = scope_digest(base, targets, project, first)
        for index, mutant in enumerate(mutants, start=1):
            mutant.verdict, mutant.detail = collected(args.survivors_of, index, earlier, mutant,
                                                      project, scope)
        chosen = [index for index, mutant in enumerate(mutants, start=1)
                  if mutant.verdict in SURVIVING]
        if args.plan:
            return plan_survivors(args, coverage, mutants, chosen, first)
        firsts = {index: mutant.verdict for index, mutant in enumerate(mutants, start=1)}
    if args.collect is not None:
        print(coverage)
        for index in chosen:
            mutant = mutants[index - 1]
            mutant.verdict, mutant.detail = collected(args.collect, index, campaign, mutant, project,
                                                      scope)
    else:
        selected = sharded(chosen, args.shard)
        if selected:
            measure(args, project, holder, output, targets, mutants, scope, campaign, coverage,
                    set(selected))
        else:
            print(coverage)
        if args.shard is not None:
            return report_shard(mutants, selected, args.shard)
    if first is not None:
        for index, mutant in enumerate(mutants, start=1):
            if index in chosen:
                mutant.detail = "{} {}; {}: {}".format(first, firsts[index], args.platform,
                                                      mutant.detail or "-")
            else:
                mutant.detail = "{}: {}".format(first, mutant.detail or "-")

    declared = declarations_for(targets, changed) if whole else {}
    unanswered, stale = answered(mutants, deferred, declared)

    print("\n--- mutants no test killed ---")
    survivors = [m for m in mutants if m.verdict in SURVIVING]
    for mutant in survivors:
        print("{}  [{}] {}".format(mutant.describe(project), mutant.verdict, mutant.detail))
    if not survivors:
        print("(none)")

    unmeasured = [m for m in mutants if m.verdict not in DECIDED + SURVIVING]
    if unmeasured:
        print("\n--- mutants nothing was asked of the suite about ---")
        for mutant in unmeasured:
            print("{}  {}".format(mutant.describe(project), mutant.detail))

    # Named, because the reach printed below counts their lines as reached with nothing asked there.
    inapplicable = [m for m in mutants if m.verdict == INAPPLICABLE]
    if inapplicable:
        print("\n--- rewrites the compiler rejected as no program at all ---")
        for mutant in inapplicable:
            print("{}  {}".format(mutant.describe(project), mutant.detail))

    # Named, because no failing case stands behind these kills.
    hung = [m for m in mutants if m.verdict == HUNG]
    if hung:
        print("\n--- mutants killed by hanging the suite ---")
        for mutant in hung:
            print("{}  {}".format(mutant.describe(project), mutant.detail))

    # Counts of what this run did, and deliberately no ratio: a mutation score over a diff is a
    # different denominator every branch, and a percentage is the part that gets quoted after the
    # run it came from is forgotten. The survivors above are the output; this is the count.
    tally = {}
    for mutant in mutants:
        tally[mutant.verdict] = tally.get(mutant.verdict, 0) + 1
    print("\n" + ", ".join("{}: {}".format(key, value) for key, value in sorted(tally.items())))
    # Repeated under the tally, not only before the run: the tally is what gets quoted, and quoted
    # alone it reads as a statement about the diff rather than about the lines an operator reached.
    print(coverage)
    print("logs: {}".format(output))

    for mutant, complaint in unanswered:
        print("\nUNANSWERED  {}\n            {}".format(mutant.describe(project), complaint))
    for path, subject, declaration in stale:
        print("\nSTALE       {}:{} declares a survivor, and line {} has none"
              .format(relative_to(path, project), declaration.line, subject))
    if unanswered and not whole:
        print("\nA survivor is a test that stopped asking, or a mutation nothing can depend on. This "
              "run is\nnarrowed by --files, --filter or --assemblies, so it reads no declaration and "
              "signs\nnothing off: it asks what this scope covers, and the survivors above are the "
              "answer.")
    elif unanswered:
        print("\nA survivor is a test that stopped asking, or a mutation nothing can depend on. Write "
              "the\ntest, or say which it is above the line:")
        print("  // MUTANT_SURVIVES({}): <why>".format("|".join(CATEGORIES)))
    if truncated > 0:
        print("\n{} further mutant(s) were never run: --max is {}. Raise it, or the lines they sit on "
              "went\nunmeasured with the run still reporting.".format(truncated, args.max))

    failed = unanswered or stale or unmeasured or truncated > 0
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
