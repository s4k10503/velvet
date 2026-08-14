#!/usr/bin/env python3
"""Refuse a GREEN_ON_BASE or MUTANT_SURVIVES declaration whose first line breaks off mid-clause.

Only the first line of a declaration is a claim on its own, and it is the line the readers measure
their four-word floor on — which holds whether or not they fold the lines under it into the reason.
So where the sentence runs on past it, the wrap position is part of what the declaration says.

The floor is not what this adds. A first line under four words is refused by the readers already,
by name, and saying so twice would buy nothing. What gets through them is a line long enough to
clear the floor and still not a claim, and that is the whole of what is refused here.

Only what can be shown is refused: a first line ending on a word that must be followed by more of
its own clause, one ending on a comma or on a comma and a relativiser, and one leaving a delimiter
open. A first line that reads as a whole claim is allowed however the rest of the reason continues,
because judging that would mean judging prose, and a guard that refuses good declarations is turned
off and takes the rest of the class with it.

The table below is small on purpose and every member of it is posed a case: a rule nothing exercises
is a way to refuse good work that nobody has measured, and deleting one has to turn the suite red or
the guard can be hollowed out in silence.

Only a declaration the edit introduces is judged, so the ones already in the tree do not make their
files unwritable — the same in-band route `changelog_into_closed_version.py` keeps open.

Run: python3 scripts/hooks/test_declaration_first_line_fragment.py
"""

import bisect
import io
import json
import re
import sys
import tokenize
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "scripts" / "test_quality"))
import mutation_check

HOOK_TOOLS = {"Edit", "Write"}

# The two markers, with the claim `mutation_check.py` and `base_red_check.py` measure their floor
# on. Their own patterns stay theirs: those run over a tree and this over a proposed edit, and the
# one thing all three must agree on is that the claim begins after the colon and ends at the newline.
DECLARATION = re.compile(r"(GREEN_ON_BASE|MUTANT_SURVIVES)\([A-Za-z]*\)\s*:\s*(.*)")

# Where a declaration is read from. Nothing reads one out of markdown -- a marker there is prose
# about the convention -- so a guide showing a malformed one to explain it stays writable.
READ_IN = {".cs", ".py"}

COMMENT = re.compile(r"^\s*(//|#)")

# Words that have to be followed by more of their own clause: an article, a coordinator, and a
# determiner with no pronoun spelling of its own. A preposition is not one of them -- it strands at
# a clause end all the time, and this repository writes "the case the canary exists for". Nor is a
# demonstrative ("must not change that"), a copula ("what it is") or "so" ("it did so").
#
# Subordinators are all out rather than some in: `because`, `if`, `while`, `since`, `when` and
# `than` read no differently from `although` and `unless`, and a table taking half a class is a
# table nobody can predict.
DANGLING = {
    "a", "an", "the",
    "and", "or", "nor",
    "its", "their", "our", "your", "my",
    "every",
}

# A relativiser is refused only behind a comma, which is the spelling that cannot close a sentence:
# bare, these end clauses readily enough ("it does not matter which"), and the tree's own break is
# `... on the base, which`.
RELATIVISERS = {"which", "who", "whom", "whose", "where", "when"}

# A comma is the mark that says the clause is unfinished. `:`, `—` and `–` were here too, on the
# reading that only `;` closes a clause; that refused `... by never deferring at all —`, which is a
# whole claim, and no declaration in this tree ends on a colon at all.
UNCLOSED = (",",)

PAIRS = (("(", ")"), ("[", "]"))
# `'` is not paired here, since this repository's prose spells possessives with it.
BALANCED = (("`", "backtick"), ('"', "quotation mark"))


def fragment(reason):
    """Why this first line cannot be read as a claim on its own, or None when it can be.

    An empty reason answers None: the readers' four-word floor already refuses it, by name, and a
    second refusal of the same thing is one more rule to keep true for nothing.
    """
    for opener, closer in PAIRS:
        if reason.count(opener) != reason.count(closer):
            return f"leaves {opener!r} unmatched"
    for mark, name in BALANCED:
        if reason.count(mark) % 2:
            return f"leaves a {name} open"
    stripped = reason.rstrip()
    for mark in UNCLOSED:
        if stripped.endswith(mark):
            return f"ends on {mark!r}, so the sentence is unfinished"
    words = stripped.split()
    if not words:
        return None
    last = words[-1].lower()
    if last in DANGLING:
        return f"ends on {last!r}, which no clause ends on"
    if last in RELATIVISERS and len(words) > 1 and words[-2].endswith(","):
        return f"ends on ', {last}', which opens a clause that is not there"
    return None


def comment_lines(text, suffix):
    """The line numbers a marker on them would be a declaration rather than fixture text.

    Neither lane reads the raw line, because both hold snippets of the other's language inside
    string literals and a reading over the line's prefix takes those for declarations. Python is
    tokenized; C# goes through the mask `mutation_check.py` owns, which is where the question of
    what the compiler sees as code is answered for this repository.

    A Python file that does not tokenize is left to the prefix test rather than dropped: standing
    down there would make a syntax error a way through.
    """
    if suffix == ".py":
        try:
            return {token.start[0] for token in
                    tokenize.generate_tokens(io.StringIO(text).readline)
                    if token.type == tokenize.COMMENT}
        except (tokenize.TokenError, IndentationError, SyntaxError, ValueError):
            return {number for number, line in enumerate(text.splitlines(), 1)
                    if COMMENT.match(line)}
    starts = [start for start, _ in mutation_check.line_spans(text)]
    return {bisect.bisect_right(starts, start)
            for start, _, kind in mutation_check.mask_spans(text)
            if kind == mutation_check.LINE_COMMENT}


def declarations(text, suffix):
    """(line number, marker, first line of the reason) for each declaration written in `text`."""
    commented = comment_lines(text, suffix)
    found = []
    for number, line in enumerate(text.splitlines(), 1):
        match = DECLARATION.search(line)
        if match and number in commented:
            found.append((number, match.group(1), match.group(2).strip()))
    return found


def introduced(before, after, suffix):
    """Every declaration `after` carries that `before` does not, with what is wrong with it.

    Counted rather than compared as a set, and by the text of the reason rather than by line. Moving
    a declaration down a file is then not a new one and rewording it is -- and so is copying a
    sibling's verbatim, which is how a broken one spreads: this tree already holds duplicate pairs,
    so a reading that asked only whether the text occurs before would let every later copy through.
    """
    written = Counter(reason for _, _, reason in declarations(before, suffix))
    seen = Counter()
    found = []
    for number, marker, reason in declarations(after, suffix):
        seen[reason] += 1
        if seen[reason] > written[reason] and (broken := fragment(reason)):
            found.append((number, marker, reason, broken))
    return found


# The verdict reads a tool input, never a shell word, so no operand of this guard can arrive
# unexpanded.
UNEXPANDED_POLICY = "n/a"

# Neither git nor gh is consulted: what decides is the text of the edit and the text of the file it
# lands on, and a file that cannot be read is one whose declarations are all introduced.
UNREADABLE_POLICY = "none"
UNREADABLE_PROBE = {
    "file_path": "Fragment.cs",
    "content": "// GREEN_ON_BASE(characterization): the base already answers the\n"
               "// question this case pins, so it reads the same there.\n",
}


def proposed(tool, payload, text):
    """The file text this edit would leave behind, or None when it would leave it unchanged."""
    if tool == "Write":
        content = payload.get("content")
        return content if isinstance(content, str) else None
    old = payload.get("old_string", "")
    if not old or old not in text:
        return None
    count = text.count(old) if payload.get("replace_all") else 1
    return text.replace(old, payload.get("new_string", ""), count)


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0
    payload = event.get("tool_input") or {}
    path = Path(payload.get("file_path") or "")
    if path.suffix not in READ_IN:
        return 0

    try:
        text = path.read_text(encoding="utf-8")
    except OSError:
        text = ""
    after = proposed(event["tool_name"], payload, text)
    if after is None:
        return 0

    found = introduced(text, after, path.suffix)
    if not found:
        return 0

    lines = "\n".join(f"  line {number}: {marker} {broken}\n    {reason}"
                      for number, marker, reason, broken in found)
    sys.stderr.write(
        "Refusing this edit: a declaration's first line breaks off before it says anything.\n\n"
        f"{lines}\n\n"
        "The readers take the first line as the whole claim, so a reason that wraps mid-clause is "
        "read as saying what stands before the wrap.\n\n"
        "Put a claim that stands on the marker line and continue underneath it. Nothing is lost by "
        "moving the wrap.\n")
    return 2


if __name__ == "__main__":
    sys.exit(main())
