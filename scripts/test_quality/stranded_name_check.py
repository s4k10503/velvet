#!/usr/bin/env python3
"""Refuse a change that removes a declaration whose name survives only in a comment.

DocumentationDriftTests resolves the names markdown spells, and strips comments from every format
that has them, so a name a removal leaves behind in a C# comment is caught by nothing. That is not a
formatting complaint: the comment was true when it was written and the removal is what made it
false, which is the shape CLAUDE.md's corrected-sentence list is about -- a reader who trusts a
reason that no longer holds is worse off than one who finds none.

Read from the removal side rather than from the comment side, which is what makes it decidable. A
sweep over the corpus for identifier-shaped tokens cannot separate a dangling reference from English:
measured here, bare PascalCase in comments left 518 tokens resolving nowhere for 4 real ones, and the
shapes that are clean -- `cref`, `Foo()`, `Foo.Bar` -- hold none of the four at all. A change's own
removals are a handful of names, and each is either still written somewhere or it is not.

Measured over the 320 commits before this was written: two firings. One is `RefAttachOrderingTests.cs`
naming the base tree's method inside a GREEN_ON_BASE declaration -- a true sentence that does not
need the identifier. Over `ba9d9b31`, the commit that stranded the three names this was opened for: it
fires on both.

Run: python3 scripts/test_quality/test_stranded_name_check.py
"""

import argparse
import re
import subprocess
import sys

# What the measurement above was taken with. A type or a method, not a property or a field: widening
# it changes the yield it was chosen on, so a widening is a fresh measurement rather than an edit.
DECLARATION = re.compile(
    r"\b(?:class|struct|interface|enum|record|delegate)\s+([A-Z][A-Za-z0-9_]*)"
    r"|\b(?:void|bool|int|string|object|var|[A-Z][A-Za-z0-9_<>,?\[\] ]*)\s+"
    r"([A-Z][A-Za-z0-9_]*)\s*(?:<[^>(]*>)?\s*\(")


def git(*arguments):
    done = subprocess.run(["git"] + list(arguments), capture_output=True, text=True)
    if done.returncode > 1:
        raise SystemExit("git {} failed: {}".format(" ".join(arguments), done.stderr.strip()))
    return done.stdout


# A char literal, and only that: an apostrophe in prose closes nothing, and reading one as an opening
# quote swallows the rest of the file out of both streams. Measured before this was narrowed:
# ErrorBoundaryTests.cs lost 42% of its text to the apostrophes in its #region labels.
CHAR_LITERAL = re.compile(r"'(?:\\.|[^'\\\n])'")


def split_comments(text):
    """(comment text, code text) -- one scan, so a `//` inside a string literal is not a comment.

    An interpolated string keeps its text in the code stream rather than being dropped, because the
    holes in it are code and separating them needs a brace-matching pass. That counts a name written
    in the string's own text as written in code, which is the direction that makes this guard say
    nothing rather than say something wrong.
    """
    comments, code, i, size = [], [], 0, len(text)
    while i < size:
        char = text[i]
        if char == '"':
            verbatim = i and text[i - 1] == "@"
            interpolated = "$" in text[max(i - 2, 0):i]
            j = i + 1
            while j < size:
                if text[j] == '"':
                    if verbatim and j + 1 < size and text[j + 1] == '"':
                        j += 2
                        continue
                    break
                j += 2 if not verbatim and text[j] == "\\" else 1
            code.append(text[i:j + 1] if interpolated else " ")
            i = j + 1
            continue
        if char == "'":
            literal = CHAR_LITERAL.match(text, i)
            if literal:
                code.append(" ")
                i = literal.end()
                continue
        if char == "#" and (i == 0 or text[i - 1] == "\n" or text[:i].rsplit("\n", 1)[-1].isspace()):
            # A directive line declares nothing and refers to nothing, and a #region label is prose:
            # routed to the code stream it answers for any name it happens to spell, tree-wide. Only
            # as far as a `//` on it, because a #pragma carrying one is carrying a comment.
            end = text.find("\n", i)
            end = size if end < 0 else end
            spoken = text.find("//", i)
            if 0 <= spoken < end:
                end = spoken
            code.append("\n")
            i = end
            continue
        if text.startswith("//", i):
            end = text.find("\n", i)
            end = size if end < 0 else end
            comments.append(text[i + 2:end])
            code.append("\n")
            i = end + 1
            continue
        if text.startswith("/*", i):
            end = text.find("*/", i)
            end = size if end < 0 else end
            comments.append(text[i + 2:end])
            code.append(" ")
            i = end + 2
            continue
        code.append(char)
        i += 1
    return "\n".join(comments), "".join(code)


def removed_declarations(base, head):
    """Every name a removed line declares that reads as a type or a method.

    Read off the diff rather than off the two trees, so a declaration that moved counts as removed
    here and is answered for by the tree below, which still spells it. A property or a field is not
    read: the yield this was chosen on was measured over types and methods, and widening it is a
    fresh measurement.
    """
    diff = git("diff", "--unified=0", "--format=", base + "..." + head, "--", "*.cs")
    names = set()
    for line in diff.splitlines():
        if not line.startswith("-") or line.startswith("---"):
            continue
        source = line[1:].strip()
        if source.startswith("//"):
            continue
        for found in DECLARATION.finditer(source):
            names.add(found.group(1) or found.group(2))
    return names


def stranded(base, head):
    """(name, files) for every removed name the tree still spells, in comments and nowhere else."""
    left = []
    for name in sorted(removed_declarations(base, head)):
        # C# only. A name a removed declaration leaves behind in USS, JSON or an asmdef is content
        # rather than a reference to the declaration -- which is the reading DocumentationDriftTests
        # takes of those formats too, where it keeps the string and resolves nothing from it.
        pattern = re.compile(r"\b{}\b".format(re.escape(name)))
        files = git("grep", "-l", "-w", name, head, "--", "*.cs").splitlines()
        if not files:
            continue
        naming = []
        for entry in files:
            path = entry.split(":", 1)[1]
            comments, code = split_comments(git("show", "{}:{}".format(head, path)))
            if pattern.search(code):
                naming = []
                break
            if pattern.search(comments):
                naming.append(path)
        if naming:
            left.append((name, naming))
    return left


def main(argv):
    parser = argparse.ArgumentParser(
        description="Refuse a removal whose name survives only in a comment.")
    parser.add_argument("--base", default="origin/main", help="what the change is read against")
    parser.add_argument("--head", default="HEAD", help="the change")
    arguments = parser.parse_args(argv)

    merge_base = git("merge-base", arguments.base, arguments.head).strip()
    if not merge_base:
        print("cannot take this reading: {} and {} share no history".format(
            arguments.base, arguments.head), file=sys.stderr)
        return 2

    left = stranded(merge_base, arguments.head)
    if not left:
        print("no removed declaration is left named by a comment alone")
        return 0

    print("these removals leave their name in a comment and nowhere else:", file=sys.stderr)
    for name, files in left:
        print("  {} -- {}".format(name, ", ".join(files)), file=sys.stderr)
    print("\nThe comment was true until the removal. Say what the code does now, or drop the "
          "sentence:\nan identifier the tree no longer declares is a reason a reader cannot check.",
          file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
