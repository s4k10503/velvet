#!/usr/bin/env python3
"""Refuse a GREEN_ON_BASE or MUTANT_SURVIVES declaration whose first line breaks off mid-clause.

Only the first line of a declaration is a claim on its own, and it is the line the readers' four-word
floor is measured on — which holds whether or not they fold the lines under it into the reason. So
where the sentence runs on past it, the wrap position is part of what the declaration says: a
fifteen-word reason wrapping early is reported as under four words, and the remedy that reading
prescribes is to write more.

Only what can be shown is refused: a first line that says nothing at all, one ending on a word no
English clause ends on, one ending on punctuation that cannot close a clause, and one leaving a
delimiter open. A first line that reads as a whole claim is allowed however the rest of the reason
continues, because judging that would mean judging prose, and a guard that refuses good declarations
is turned off and takes the rest of the class with it.

Only a declaration the edit introduces is judged, so the ones already in the tree do not make their
files unwritable — the same in-band route `changelog_into_closed_version.py` keeps open.

Run: python3 scripts/hooks/test_declaration_first_line_fragment.py
"""

import io
import json
import re
import sys
import tokenize
from pathlib import Path

HOOK_TOOLS = {"Edit", "Write"}

# The two markers, with the claim `mutation_check.py` and `base_red_check.py` measure their floor
# on. Their own patterns stay theirs: those run over a tree and this over a proposed edit, and the
# one thing all three must agree on is that the claim begins after the colon and ends at the newline.
DECLARATION = re.compile(r"(GREEN_ON_BASE|MUTANT_SURVIVES)\([A-Za-z]*\)\s*:\s*(.*)")

# Where a declaration is read from. Nothing reads one out of markdown -- a marker there is prose
# about the convention -- so a guide showing a malformed one to explain it stays writable.
READ_IN = {".cs", ".py"}

COMMENT = re.compile(r"^\s*(//|#)")

# Words that cannot close an English clause under any reading, and so cannot end a claim. A
# preposition is not one of them: it strands at a clause end all the time, and this repository
# writes "the case the canary exists for". Nor is a demonstrative ("must not change that"), a
# copula ("what it is") or "so" ("it did so") -- each of those ends a clause somewhere, and a
# refusal has to be about a line that cannot be one.
DANGLING = {
    "a", "an", "the",
    "and", "or", "nor",
    "which", "whose",
    "its", "their", "our", "your", "my",
    "every", "although", "unless",
}

# `;` is absent deliberately: it closes a clause and joins it to the next, so a first line ending on
# one is a claim that stands. The rest leave the sentence open.
UNCLOSED = (",", ":", "—", "–", "-")

PAIRS = (("(", ")"), ("[", "]"))
# `'` is not paired here, since this repository's prose spells possessives with it.
BALANCED = (("`", "backtick"), ('"', "quotation mark"))


def fragment(reason):
    """Why this first line cannot be read as a claim on its own, or None when it can be."""
    if not reason.strip():
        return "is empty"
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
    last = stripped.split()[-1].lower()
    if last in DANGLING:
        return f"ends on {last!r}, which no clause ends on"
    return None


def comment_lines(text, suffix):
    """The line numbers a marker on them would be a declaration rather than fixture text.

    Python is tokenized, because `scripts/test_quality/test_*.py` holds C# snippets carrying these
    markers inside string literals and a prefix test reads those as declarations. A file that does
    not tokenize is left to the prefix test rather than dropped: standing down there would make a
    syntax error a way through.
    """
    if suffix == ".py":
        try:
            return {token.start[0] for token in
                    tokenize.generate_tokens(io.StringIO(text).readline)
                    if token.type == tokenize.COMMENT}
        except (tokenize.TokenError, IndentationError, SyntaxError, ValueError):
            pass
    return {number for number, line in enumerate(text.splitlines(), 1) if COMMENT.match(line)}


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

    Compared by the text of the reason rather than by line, so moving a declaration down a file is
    not a new one and rewording it is.
    """
    written = {reason for _, _, reason in declarations(before, suffix)}
    return [(number, marker, reason, broken)
            for number, marker, reason in declarations(after, suffix)
            if reason not in written and (broken := fragment(reason))]


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
        "The four-word floor is measured on the first line, so a long reason that wraps early is "
        "reported as under four words and the remedy that prescribes — write more — is the wrong "
        "one.\n\n"
        "Put a claim that stands on the marker line and continue underneath it. Nothing is lost by "
        "moving the wrap.\n")
    return 2


if __name__ == "__main__":
    sys.exit(main())
