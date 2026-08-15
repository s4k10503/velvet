#!/usr/bin/env python3
"""Refuse a pull-request body naming `script.symbol` where that script spells no such name.

The body becomes the squash commit message, so it is shipped prose, and it is prose no guard read:
`DocumentationDriftTests` resolves the names a document writes, and a body lives on GitHub rather
than in the tree it walks. One of that fixture's scans runs here instead, over the description at the
moment it is written.

The squash message of the change that added that scan says `neuter_check.run_suites` twice, and the
function is `run_suite`. `LiveDefectTests` in this guard's own test module fails when that span stops
resolving to nothing.

## Only one of the fixture's scans, and the corpus is why

Measured over this repository's own descriptions — the 67 merged pull-request bodies and the 115
earlier revisions of them GitHub's edit history holds — each resolved against its own head tree:

| scan | of the 67 it declines | its spans on those bodies |
|---|---|---|
| every backticked identifier | 38 | 173, of which 40 are quoted NUnit failure text |
| backticked paths | 12 | 19, of which 1 names a file neither tree holds |
| `Type.Member` against the tree's words | 4 | 10, of which none names a word neither tree holds |
| `script.symbol` against the script it names | 1 | 4, of which 2 name a function no file spells |

The first is unusable for the reason its own column gives: a description is asked to quote its RED
evidence, and NUnit's failure text is not code. The next two share one cause, and it is what makes a
body different from a guide: a description is written about a change, so it names the state before it
as well as the state after. Seven of the ten `Type.Member` spans name an upstream library's API and
the other three a member the same branch deleted; of the twelve path bodies, four spell a glob this
resolver cannot expand, three name build output, one a file the branch itself renamed, one a file on
another branch, and one a path invented for a worked example.

Resolving against the merge base as well as the head answers the deleted-member half and nothing
else: it takes `Type.Member` to no findings at all and leaves the paths declining four of the 67. So
neither ships, and the scan that does is the one whose findings were all real.

## What it cannot disagree with

Every span this declines, `DocumentationDriftTests` over the same tree would report if the sentence
were in a guide: the span walk is that fixture's, and a symbol is sought as a word ANYWHERE in the
script, which is weaker than the module-level definition the fixture asks for. So this is quieter
than the fixture and never louder, and a body cannot be refused for a sentence a guide is allowed.
The weakening is not free and was measured: reading the fixture's own rule instead declines
`test_settle.handle` and `test_mutation_check.hang`, both of which the named file does spell — one
inside a synthetic script in a string literal, one as a nested def.

The head tree is read from disk, from the worktree the command runs in. Whether the merge base is
read as well changed no verdict on any of the 182 descriptions above, so it is not read and the
guard consults neither git nor gh. The cost of reading a tree rather than a branch is that a
description posted from a checkout standing on something else is resolved against that: measured the
same way, the merge base in place of the head declines one further span across the 182, a function
the branch being described adds. A module the reading tree does not carry at all is silent either
way, so what that leaves is a new symbol on an old script.
"""

import json
import os
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from pr_body import effective_body, invocations

# Registered on the event in .claude/settings.json rather than narrowed to the agents expected to
# open pull requests, which would leave every other session unguarded. `HookWiringCoverageTests`
# reads this declaration to check that the registration is still there.
HOOK_SCOPE = "session"

# The tools this acts on, gated on below rather than spelled a second time there: two statements of
# the same set drift.
HOOK_TOOLS = {"Bash"}

# A body file whose path the shell has yet to expand is one this cannot open, and a description it
# never read holds no span to decline for. The sibling reading the same invocation refuses that
# spelling, so the tool call is not left unguarded by the silence here.
UNEXPANDED_POLICY = "allow"
UNEXPANDED_PROBE = 'gh pr create --title t --body-file $BODY'

UNREADABLE_POLICY = "none"
UNREADABLE_PROBE = {"command": "gh pr create --title t --body 'names `neuter_check.no_such_symbol`'"}

# The walk `DocumentationCorpus` performs, which is what makes a script here the same script the
# fixture resolves against. Rooted rather than filtered for the reason it gives: this repository's
# own workflow puts full checkouts of itself under .claude/worktrees while a suite runs.
WALKED_ROOTS = ("Packages", "Assets", ".github", "scripts", "ProjectSettings", "docs", ".claude")
UNWALKED = {".git", "Library", "Temp", "Logs", "Build", "UserSettings", "obj", "bin", "api",
            "_site", "StrykerOutput", "worktrees"}

# The span grammar. Each is the same string `DocumentationDriftTests` compiles, and
# test_pr_body_naming_no_such_symbol.py reads both sides and fails when the two part.
FENCED_BLOCK = re.compile(r"^```.*?^```", re.S | re.M)
BACKTICK_SPAN = re.compile(r"`((?:[^`\n]|\n(?!\s*\n))*)`")
MACHINE_PATH = re.compile(r"^(/|~/|[A-Za-z]:\\)")
PATH_REFERENCE = re.compile(
    r"^(\.{1,2}/)*(\.[A-Za-z0-9_-]+/)?[A-Za-z0-9_~@][A-Za-z0-9_./~*-]*"
    r"(\.(cs|uss|md|json|yml|py|sh|txt|asmdef|dll|asset|tss)|/)$")
DOTTED_SYMBOL = re.compile(r"^([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)(\(\))?$")

WORD = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")


def repository_root(cwd):
    """The worktree `cwd` sits in, or None.

    Taken from the command's directory rather than from this file's, because an agent here works in
    a `git worktree` under a scratchpad while the hook it runs is the project directory's. Reading
    this file's own tree would resolve the description of one branch against the sources of another.
    """
    directory = Path(cwd).resolve()
    for candidate in (directory, *directory.parents):
        if (candidate / ".git").exists():
            return candidate
    return None


def script_words(root):
    """Every Python source the walk reaches, keyed on its stem, as the set of words it spells.

    Stems are not unique across the tree and a document naming one means the name rather than a path,
    so a symbol found in any file carrying the stem answers for it — the same joining the fixture
    does.
    """
    found = {}
    for name in WALKED_ROOTS:
        base = os.path.join(root, name)
        if not os.path.isdir(base):
            continue
        for directory, children, files in os.walk(base):
            children[:] = [child for child in children if child not in UNWALKED]
            for entry in files:
                if not entry.endswith(".py"):
                    continue
                try:
                    text = Path(directory, entry).read_text(encoding="utf-8", errors="replace")
                except OSError:
                    continue
                found.setdefault(entry[:-3], set()).update(WORD.findall(text))
    return found


def spans(body):
    """Every backticked span in the body, the way the fixture walks a markdown file.

    Fenced samples go first: an inline span may wrap a line, so a fence's third backtick otherwise
    pairs with the closing fence's first and swallows the sample body. A path on the reader's own
    machine names nothing in the repository and is skipped outright.
    """
    prose = FENCED_BLOCK.sub("\n", body)
    for match in BACKTICK_SPAN.finditer(prose):
        reference = " ".join(match.group(1).split())
        if reference and not MACHINE_PATH.match(reference):
            yield reference


def unresolved(body, scripts):
    """Every `script.symbol` span in the body that the script it names does not spell."""
    found = []
    for reference in spans(body):
        dotted = DOTTED_SYMBOL.match(reference)
        # A file name is the path check's, which resolves it against the filesystem — the stronger
        # question. Without this, `mutation_check.py` reads here as a module and an extension.
        if not dotted or PATH_REFERENCE.match(reference):
            continue
        words = scripts.get(dotted.group(1))
        if words is not None and dotted.group(2) not in words:
            found.append(reference)
    return found


def refuse(command, message):
    print(f"Refusing `{command}`: {message}", file=sys.stderr)
    return 2


def naming(references):
    return ("the body names a symbol the script it names does not spell.\n\n"
            + "\n".join(f"  {reference}" for reference in dict.fromkeys(references))
            + "\n\nThe body becomes the squash commit message, so a name that resolves nowhere ships\n"
            "into main's history where nothing will fail for it. `DocumentationDriftTests` declines\n"
            "the same span in a guide. Name the symbol the script declares, or drop the backticks if\n"
            "the span is not a reference.")


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    try:
        if not isinstance(event, dict) or event.get("tool_name") not in HOOK_TOOLS:
            return 0
        command = event.get("tool_input", {}).get("command") or ""
        cwd = event.get("cwd") or "."
        if not isinstance(command, str):
            return 0
        # `gh pr edit` is asked alongside `gh pr create`, because a body corrected after the fact is
        # posted by it and reaches the squash message the same way.
        posted = []
        for words, operands, after_a_move in invocations(command, ("pr", "create"), ("pr", "edit")):
            body, obstruction, _ = effective_body(operands, cwd, after_a_move)
            subcommand = "gh pr " + words[1]
            if obstruction is not None:
                # `pr_body_of_another_branch` owns unreadable create bodies; edit has no sibling.
                if words[1] == "edit":
                    return refuse(
                        subcommand,
                        "the body file could not be read, so no description was checked.\n\n"
                        "Write the body to a readable file before posting it.")
                continue
            # The corpus is built only once a description is in hand, so a command that posts none
            # is neither read nor held to the tree the walk below wants.
            if body is not None:
                posted.append((words, body))
        if not posted:
            return 0
        subcommand = "gh pr " + posted[0][0][1]
        root = repository_root(cwd)
        scripts = script_words(root) if root else {}
        if not scripts:
            # Nothing found means the walk was pointed somewhere else, and a corpus that holds no
            # script resolves every span it is given — which reads exactly like a body with nothing
            # wrong in it.
            return refuse(
                subcommand,
                f"no script was found under {cwd}, so this would pass having read nothing.\n\n"
                "Run it from inside the worktree the pull request is for.")
        references = [reference for _, body in posted for reference in unresolved(body, scripts)]
        return refuse(subcommand, naming(references)) if references else 0
    except Exception as err:
        # Exit 1 is not a refusal — PreToolUse runs the tool anyway — so an unforeseen shape here
        # would let through exactly what this exists to stop.
        return refuse("gh pr create", f"this guard failed to reach a verdict ({err!r}).")


if __name__ == "__main__":
    sys.exit(main())
