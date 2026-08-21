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
nobody asked about must never be a pass. So the ways a run can measure less than it looks like it
measured each fail on their own -- a cap that left mutants unrun, an editor killed at --timeout, a
build that rejected the mutation, an assembly the editor never rebuilt, a second editor sharing the
machine, and a file whose comment and string mask swallowed code, which generates no mutant there and
says nothing.

It is not success over the whole change, and the difference is most of one: the operators reach a
minority of the code lines a branch touches, so the reach is printed beside every verdict rather than
folded into it, and a change nothing reaches at all refuses.

The default scope is the whole platform suite rather than the fixtures nearest the mutated file,
so that nothing is reported as surviving merely because the fixture that would have killed it was
out of scope.

A campaign holds a mutation in the working tree while the suite runs, so it records what it holds
before writing it and clears that record only after putting the original back. Nothing else in the
tree says a campaign is running: the mutation is a plausible one-line change in a file the branch is
already touching, and two of them reached a commit that way.
"""

import argparse
import bisect
import hashlib
import json
import os
import re
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

DEFAULT_UNITY = "/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity"
# Anchored at the editor binary so that a shell waiting on this pattern does not match itself and
# report a busy machine forever on an idle one.
UNITY_RUNNING = "^/Applications/.*/MacOS/Unity -runTests"

# At the project root and not in .gitignore, so `git status` names it beside the file it explains.
# Under Logs/ it would be correct and unread: what a resumed session looks at is `git status`, and a
# mutation there reads as an interrupted implementation.
SENTINEL = "MUTATION_IN_PROGRESS.json"

KILLED = "killed"
TIMED_OUT = "not measured (timed out)"
SURVIVED = "survived"
INCONCLUSIVE = "survived (inconclusive)"
UNCOMPILABLE = "uncompilable"
NOT_BUILT = "not rebuilt"

SURVIVING = (SURVIVED, INCONCLUSIVE)

CATEGORIES = ("equivalent", "unreachable")

# Four words, for the reason base_red_check.py's own floor gives.
MINIMUM_REASON_WORDS = 4

DECLARATION = re.compile(r"MUTANT_SURVIVES\(([A-Za-z]*)\)\s*:\s*(.*)")

# How many unreached line numbers a file lists before the rest become a count. The count stays exact
# either way; what this bounds is a whole-file `--files` run printing several hundred of them.
LINES_LISTED = 25

# A record exists and cannot be read. Every reader treats it as a campaign holding something whose
# name is unavailable, which is the only reading that does not let a mutation through.
UNREADABLE = object()

# What `--carried` and `--receipt` exit with when they refuse, so a caller can tell a refusal from a
# script that could not take the reading at all. Both stop the tool; only one is about a campaign.
CARRIED_REFUSAL = 3
RECEIPT_REFUSAL = 3


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
    def __init__(self, category, reason, line, claim=None, through=None, written_here=True):
        self.category = category
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
    found = {}
    for match in marker.finditer(text):
        if any(start <= match.start() < end for start, end in spans):
            found.setdefault(bisect.bisect_right(starts, match.start()), match)
    return found


def comment_lines(text, spans):
    """The 1-based lines whose first non-space character sits inside one of `spans`.

    Which lines are prose rather than which hold a marker, so a block comment's continuation counts
    and a remark trailing a statement does not.
    """
    found = set()
    for number, (start, end) in enumerate(line_spans(text), start=1):
        line = text[start:end]
        if not line.strip():
            continue
        head = start + len(line) - len(line.lstrip())
        if any(opened <= head < closed for opened, closed in spans):
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
        claim, reason = folded_reason(match.group(2), lines[index + 1:subject])
        declaration = Declaration(match.group(1), reason, line=index + 1, claim=claim,
                                  through=subject)
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


def code_mask(text):
    """True at each offset `mask_spans` did not read as a comment, a literal or a directive."""
    mask = [True] * len(text)
    for start, end, _ in mask_spans(text):
        for offset in range(start, end):
            mask[offset] = False
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

# An identifier, a parenthesised head, a semicolon-terminated tail. The word in front of the
# parenthesis is no part of that, so what the removal takes is everything the line runs rather than
# one call -- which is what its verdict is named for.
REMOVABLE_LINE = re.compile(r"^[A-Za-z_][A-Za-z0-9_.]*(\.[A-Za-z_][A-Za-z0-9_]*)*\s*\([^;]*\)\s*;$")

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


def deletable_line(text, mask, spans, number):
    """Whether removing line `number`'s code leaves what surrounds it standing.

    Two ways it is not, both measured with the C# parser over every mutant this package generates.
    The code above has to have ended a statement -- `=> Fragment(...)` and `= new(...)` both match
    the removal pattern and are the tail of a declaration, so deleting them leaves a member with no
    body, which was 77 mutants. And an `if (...) Call();` whose next line is an `else` takes the
    `if` with it and strands the `else`, which was six more.
    """
    if not code_above(text, mask, spans, number).endswith(STATEMENT_BOUNDARY):
        return False
    return not code_below(text, mask, spans, number).startswith("else")


def groups_left_open(code):
    """How many more groups a run of code opens than it closes."""
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


def assigned_above(text, mask, spans, number, name):
    """Whether a statement above line `number`, in the block holding it, writes `name`.

    Only that block. A write nested inside one above runs on some of the paths reaching line `number`
    and not others. A write in an enclosing block runs on all of them and is passed over anyway, since
    a walk that left the block would have to tell a member's own writes from a field initialiser, which
    a local of the same name shadows.
    """
    written = re.compile(r"^(?:[\w.<>\[\]?]+\s+)?" + re.escape(name)
                         + r"\s*(?<![-+*/%&|^!<>=])=(?![=>])")
    depth = 0
    for above in range(number - 1, 0, -1):
        code = code_only(text, mask, *spans[above - 1]).strip()
        depth += code.count("}") - code.count("{")
        if depth < 0:
            return False
        if depth == 0 and written.match(code):
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
    declaring one is exempt wherever something above the statement already wrote that name, because no
    ordering of the clauses can then leave it unwritten.
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


def line_spans(text):
    spans = []
    offset = 0
    for line in text.splitlines(keepends=True):
        spans.append((offset, offset + len(line)))
        offset += len(line)
    return spans


def mutations_for(path, text, target_lines):
    mask = code_mask(text)
    spans = line_spans(text)
    found = []
    for number in sorted(target_lines):
        if number > len(spans):
            continue
        start, end = spans[number - 1]
        line = text[start:end]
        # Asked of the raw line first, so the statement walk is not taken on a line holding no join
        # for it to answer about.
        flippable = not (any(join in line for join in LOGIC_JOINS)
                         and binds_a_name_conditionally(text, mask, spans, number))
        for before, after, operator in OPERATORS:
            if operator == "logic" and not flippable:
                continue
            index = line.find(before)
            while index >= 0:
                if all(mask[start + index:start + index + len(before)]):
                    found.append(Mutant(path, number, index, before.strip(), after.strip(), operator))
                index = line.find(before, index + 1)
        for before, after, operator in WORD_OPERATORS:
            for match in re.finditer(r"\b{}\b".format(before), line):
                if all(mask[start + match.start():start + match.end()]):
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
        if (GUARD_STATEMENT.match(stripped) and all(mask[start:start + limit])
                and not DECLARES_A_NAME.search(code_only(text, mask, start, start + limit))):
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


def merge_base_of(project, base):
    found = subprocess.run(
        ["git", "-C", str(project), "merge-base", base, "HEAD"],
        capture_output=True, text=True,
    )
    if found.returncode != 0:
        raise SystemExit("cannot resolve a merge base with {}: {}".format(base, found.stderr.strip()))
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
    if not relative.startswith("Packages/com.velvet.core/"):
        return False
    # Generators~ is outside the Unity build and has its own mutation run; see its README.
    if "/Generators~/" in relative:
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

    def hold(self, source, original, mutated, description):
        # Refusing rather than overwriting: two campaigns started close enough together both reach
        # here, and the second overwriting the first ends with one restoring the other's file.
        if self.sentinel.exists():
            raise SystemExit("{} already records a held mutation; two campaigns are running over one "
                             "tree".format(self.sentinel))
        self.sentinel.write_text(json.dumps({
            "source": str(source),
            "original": original,
            "original_sha": hashlib.sha256(original.encode()).hexdigest(),
            "mutated_sha": hashlib.sha256(mutated.encode()).hexdigest(),
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
            Path(held["source"]).write_text(held["original"])
        except (OSError, ValueError, KeyError) as failure:
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
            if self.child is not None and self.child.poll() is None:
                self.child.kill()
            self.release()
            signal.signal(number, signal.SIG_DFL)
            os.kill(os.getpid(), number)

        for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            signal.signal(number, handler)


def unity_busy():
    result = subprocess.run(["ps", "-Ao", "command="], capture_output=True, text=True)
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


def run_suite(unity, project, platform, scope, results, log, timeout, holder=None):
    """Returns the wall clock and whether the editor had to be killed.

    A mutation can turn a loop bound into one that never terminates, and the run would otherwise
    wait on it for as long as the machine is left alone.

    The editor is held rather than waited on, so that a signal arriving here reaps it before the
    restore: an editor left running over a tree somebody has just put back writes a results file
    for a mutant that is no longer on disk.
    """
    command = [
        unity, "-runTests", "-batchmode", "-projectPath", str(project),
        "-testPlatform", platform, "-testResults", str(results), "-logFile", str(log),
    ]
    command += scope
    start = time.time()
    child = subprocess.Popen(command)
    if holder is not None:
        holder.child = child
    try:
        child.wait(timeout=timeout)
        return time.time() - start, False
    except subprocess.TimeoutExpired:
        child.kill()
        child.wait()
        return time.time() - start, True
    finally:
        if holder is not None:
            holder.child = None


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


def scope_digest(base, targets, project, platform):
    """What a campaign measured, in a form a later check can compare a tree against.

    Not the head tree: the campaign diffs the merge base against the **working tree**, so an
    uncommitted edit to a mutated file changes what it measured and moves no tree sha at all --
    measured, and it is a receipt that would validate a run taken before the edit. Nor the head tree
    for a second reason: 16 of 44 commits over five recent branches changed no mutable production
    file, and each would have voided a receipt over a change no operator can see.

    What it does not cover is a test-side change. Removing a test can make a killed mutant survive,
    and this stays valid across it; including tests would void the receipt on the ordinary act of
    adding one after the run, which is most of a branch's commits.
    """
    parts = [base, platform]
    for path in sorted(targets, key=str):
        # Repository-relative, so a receipt does not depend on where the checkout sits: a resolved
        # path and an unresolved one reach the same file and digest differently.
        parts.append("{}:{}".format(relative_to(path, project).as_posix(),
                                    hashlib.sha256(path.read_bytes()).hexdigest()))
    return hashlib.sha256("\n".join(parts).encode()).hexdigest()


def receipt_path(output, digest):
    return output / "receipts" / "{}.json".format(digest)


PASSING_RECEIPTS = ("pass", "unreachable")


def write_receipt(output, digest, base, verdict, detail):
    path = receipt_path(output, digest)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps({
        "digest": digest, "base": base, "verdict": verdict, "detail": detail,
        "at": time.strftime("%Y-%m-%dT%H:%M:%S"),
    }, indent=2))
    return path


def read_receipt(output, digest):
    path = receipt_path(output, digest)
    if not path.exists():
        return None
    try:
        return json.loads(path.read_text())
    except (OSError, ValueError):
        return None


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
    covers = {}
    for (path, subject), declaration in declared.items():
        covers.setdefault((path, declaration.line), [declaration, []])[1].append(subject)
    stale = [(path, min(subjects), declaration)
             for (path, _), (declaration, subjects) in sorted(covers.items(),
                                                              key=lambda item: (str(item[0][0]),
                                                                                item[0][1]))
             if declaration.written_here
             and not any((path, subject) in settled for subject in subjects)]
    return unanswered, stale


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", default=".", help="Unity project root (default: cwd)")
    parser.add_argument("--base", default="main", help="branch to diff against (default: main)")
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
    parser.add_argument("--timeout", type=int, default=900,
                        help="seconds before a mutant run is killed; a killed run is not measured and fails (default: 900)")
    parser.add_argument("--busy-timeout", type=int, default=1800,
                        help="seconds to wait for another Unity run to finish (default: 1800)")
    parser.add_argument("--output", default="", help="directory for the per-mutant logs and XML")
    parser.add_argument("--unity", default=DEFAULT_UNITY, help="editor binary (default: the pinned macOS one)")
    parser.add_argument("--restore", action="store_true",
                        help="put back the mutation an interrupted campaign left, and stop")
    parser.add_argument("--carried", nargs="*",
                        help="paths something is about to record; refuses when one of them is the "
                             "source a campaign is holding a mutation in")
    parser.add_argument("--emit-lines",
                        help="write every mutant this package generates as {path, line, text} and "
                             "stop, for a reader that parses them with something other than this "
                             "script's own model of C#")
    parser.add_argument("--receipt", action="store_true",
                        help="ask whether a finished campaign covers this tree's mutable change, and "
                             "stop. Refuses when one is owed and none was run")
    args = parser.parse_args()

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
        held = Path(outstanding.get("source", "")).resolve()
        for name in args.carried:
            candidate = Path(name)
            candidate = candidate if candidate.is_absolute() else project / name
            if candidate.resolve() == held:
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
            source = Path(outstanding.get("source", ""))
            on_disk = hashlib.sha256(source.read_bytes()).hexdigest() if source.exists() else ""
            known = (outstanding.get("mutated_sha"), outstanding.get("original_sha"))
            if on_disk and on_disk not in known:
                raise SystemExit(
                    "{} holds neither the mutation {} recorded nor the original it replaced, so "
                    "something\nelse has written it since. Nothing here can tell your work from the "
                    "campaign's:\nkeep what is there, or take the original out of the record by "
                    "hand.".format(source, holder.sentinel))
        held = holder.release()
        if held is None:
            raise SystemExit("{} names a mutation this could not put back; read it and restore by "
                             "hand".format(holder.sentinel))
        print("restored {} ({})".format(held["source"], held["mutation"]))
        return 0

    # Before anything is read, because everything below reads the working tree: the baseline would be
    # taken over somebody else's mutation, every mutant would be applied on top of it, and the restore
    # at the end would write it back as though it were the author's own code.
    outstanding = holder.outstanding()
    if outstanding is not None:
        names = ("<unreadable>", "<unreadable>") if outstanding is UNREADABLE else (
            outstanding.get("source", "<unnamed>"), outstanding.get("mutation", "<unnamed>"))
        raise SystemExit(
            "a campaign is holding a mutation in this tree, so nothing here can be measured:\n"
            "  {} -- {}\n"
            "Wait for it if one is running; otherwise put it back with\n"
            "  python3 scripts/test_quality/mutation_check.py --restore".format(*names))

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
        scope += ["-assemblyNames", args.assemblies]
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

    if args.receipt:
        # What is owed is decided the way the campaign decides it, by generating the mutants -- which
        # needs no editor. A production file changed only in its documentation comments has a target
        # and nothing to ask of it, and keying on the target alone left such a branch owing a receipt
        # no campaign could ever write.
        owed, nothing_reached = {}, {}
        for path, lines in sorted(targets.items()):
            text = path.read_text()
            found = mutations_for(path, text, lines)
            covered = {mutant.line for mutant in found}
            left = [number for number in code_line_numbers(text, lines) if number not in covered]
            if found:
                owed[path] = found
            if left:
                nothing_reached[path] = left
        if not targets or not (owed or nothing_reached):
            print("no mutable change; no campaign is owed")
            return 0
        since = merge_base_of(project, args.base)
        digest = scope_digest(since, targets, project, args.platform)
        held = read_receipt(output, digest)
        if held is None:
            return refusal(RECEIPT_REFUSAL,
                           "no campaign has measured this tree's mutable change:\n"
                           + "\n".join("  {}".format(relative_to(path, project))
                                       for path in sorted(targets, key=str))
                           + "\nRun it, and this passes on the reading it leaves:\n"
                             "  python3 scripts/test_quality/mutation_check.py --base {}".format(
                                 args.base))
        if held.get("verdict") not in PASSING_RECEIPTS:
            return refusal(RECEIPT_REFUSAL, "the campaign over this tree ended {}: {}".format(
                held.get("verdict"), held.get("detail")))
        print("campaign {} over {}: {}".format(held.get("verdict"), digest[:12], held.get("detail")))
        return 0

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

    if not mutants:
        # Which of the two happens is decided by the lines, not by the kind of change: a change that
        # touched no code line at all has nothing to ask and passes, and one that touched code lines
        # no operator reaches refuses, because the verdict would be about no line. A rename lands on
        # either side depending on what its lines carry -- measured, twice, on both.
        if unreached:
            print(coverage)
            left = sum(len(lines) for lines in unreached.values())
            # Recorded, because the branch cannot earn a passing run and this is the reading it got.
            # A campaign nothing can ask anything of is a finished campaign, not an absent one.
            if whole and not args.list:
                write_receipt(output,
                              scope_digest(merge_base_of(project, args.base), targets, project,
                                           args.platform),
                              args.base, "unreachable",
                              "no operator reaches any of {} changed code line(s)".format(left))
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

    if not wait_for_quiet(args.busy_timeout):
        raise SystemExit("another Unity test run is still in flight after {}s".format(args.busy_timeout))

    print(coverage)

    # Before the baseline, not before the loop: the guard reaps the editor as well as restoring, and
    # the baseline's editor outliving a killed campaign holds the project lock against the next one.
    holder.guard()
    baseline_results = output / "baseline.xml"
    baseline_wall, baseline_timed_out = run_suite(args.unity, project, args.platform, scope,
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

    originals = {path: path.read_text() for path in targets}
    started = time.time()
    try:
        for index, mutant in enumerate(mutants, start=1):
            print("[{}/{}] {}".format(index, len(mutants), mutant.describe(project)), flush=True)
            results = output / "mutant-{:03d}.xml".format(index)
            log = output / "mutant-{:03d}.log".format(index)
            if results.exists():
                results.unlink()
            # Queue first, mutate second. The wait runs to --busy-timeout, half an hour by default,
            # and there is nothing the campaign needs on disk across it.
            if not wait_for_quiet(args.busy_timeout):
                raise SystemExit("another Unity test run is still in flight after {}s, so this "
                                 "mutant's failures would not all be its own".format(args.busy_timeout))
            mutated = apply_mutation(originals[mutant.path], mutant)
            holder.hold(mutant.path, originals[mutant.path], mutated, mutant.describe(project))
            mutant.path.write_text(mutated)
            wall, timed_out = run_suite(args.unity, project, args.platform, scope, results, log,
                                        args.timeout, holder)
            if holder.release() is None:
                # The record is still there naming a file still mutated. Going on would apply the
                # next mutation over this one and end by restoring the wrong text.
                raise SystemExit("could not put {} back, so the campaign cannot continue; the record "
                                 "at {} names what is outstanding".format(mutant.path, holder.sentinel))

            counts = read_counts(results)
            dll = assemblies_dir / "{}.dll".format(assembly_of(mutant.path))
            blamed = build_error(log)
            if timed_out:
                # Not killed. A mutation that leaves a loop unbounded and a --timeout shorter than the
                # suite reach here identically, and nothing in the results tells them apart: a killed
                # editor writes no verdict either way. One of the two is a mutant nobody asked about,
                # so the run refuses and says which reading to take.
                mutant.verdict = TIMED_OUT
                mutant.detail = ("the editor was killed at --timeout {}s; raise it, or read the log "
                                 "for a mutation that does not terminate".format(args.timeout))
            elif blamed:
                mutant.verdict = UNCOMPILABLE
                mutant.detail = "the build stopped in {}".format(blamed)
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
                mutant.verdict = KILLED
                mutant.detail = "{} failed: {}".format(
                    counts["failed"], ", ".join(name.split(".")[-1] for name in names[:3]))
            elif counts["inconclusive"]:
                mutant.verdict = INCONCLUSIVE
                mutant.detail = "{} inconclusive, 0 failed".format(counts["inconclusive"])
            else:
                mutant.verdict = SURVIVED
            average = (time.time() - started) / index
            print("      {} ({}) in {:.0f}s; {:.0f}s left at {:.0f}s each".format(
                mutant.verdict, mutant.detail or "-", wall, average * (len(mutants) - index), average))
    finally:
        holder.release()
        for path, text in originals.items():
            path.write_text(text)

    declared = declarations_for(targets, changed) if whole else {}
    unanswered, stale = answered(mutants, deferred, declared)

    print("\n--- mutants no test killed ---")
    survivors = [m for m in mutants if m.verdict in SURVIVING]
    for mutant in survivors:
        print("{}  [{}] {}".format(mutant.describe(project), mutant.verdict, mutant.detail))
    if not survivors:
        print("(none)")

    unmeasured = [m for m in mutants if m.verdict in (NOT_BUILT, TIMED_OUT, UNCOMPILABLE)]
    if unmeasured:
        print("\n--- mutants nothing was asked of the suite about ---")
        for mutant in unmeasured:
            print("{}  {}".format(mutant.describe(project), mutant.detail))

    # Counts of what this run did, and deliberately no ratio: a mutation score over a diff is a
    # different denominator every branch, and a percentage is the part that gets quoted after the
    # run it came from is forgotten. The survivors above are the output; this is the receipt.
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
    # Only a whole-suite run over the diff leaves a reading anything else can stand on. A narrowed
    # one asked a different question, and its answer must not be able to sign a branch off.
    if whole:
        detail = "{} mutant(s), {} survivor(s) unanswered, {} line(s) unreached".format(
            len(mutants), len(unanswered), sum(len(lines) for lines in unreached.values()))
        written = write_receipt(output, scope_digest(merge_base_of(project, args.base), targets, project, args.platform),
                                args.base, "fail" if failed else "pass", detail)
        print("\nreceipt: {}".format(written))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
