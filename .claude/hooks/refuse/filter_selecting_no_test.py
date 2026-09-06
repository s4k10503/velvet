#!/usr/bin/env python3
"""Refuse a `-testFilter` value that selects no test, and name the fixtures a selecting one leaves out.

A filter written from the names of the files a change touched runs a set nobody chose, and neither way
it goes wrong is loud. A value that selects nothing leaves the run green over whatever is left of the
filter; a value that selects one of a file's several fixtures leaves it green over the smaller set,
with a count that reads like a count over the whole file. Both shapes are in this repository's own
sources: `Runtime/Styling/Tests/Editor/VariantGatedTypographyPassTests.cs` declares three fixtures and
none of its own name.

What a value IS decides which question this may ask, and `CLAUDE.md` states that beside the flag.
"Does any file declare this class" is not the question: `StarterSample` is declared by nothing and
selects a whole fixture. "Does this value select any test at all" is, and it is what this answers,
against the names the tree's own test sources give the runner.

Which tree is the command's to settle, and where its text does not settle it this says nothing rather
than reading the directory the tool call started in. A session drives sibling worktrees, so the two
are different trees, and a fixture the other holds reads as a name nothing declares.

Five values it declines to answer for, each because answering would mean guessing:

- one starting with `!`, which excludes rather than selects. An exclusion matching nothing leaves the
  run LARGER than asked, which the count shows, and the failure here is a run smaller than asked.
- one carrying anything but `[A-Za-z0-9_.]` and whitespace, outside a leading `^` and a trailing `$`.
  Within that alphabet the only pattern character is `.`, which this matches as the wildcard it is,
  and whitespace is a literal the runner keeps; a value carrying more is a pattern whose author is
  writing one. It is also where the reading below is weakest, a case name assembled from arguments
  being spelled with brackets and quotes.
- one whose leading segments run out exactly where some fixture's name does, its last segment naming
  no case of that fixture that is here to be read. The fixture may be spelled short of its namespace
  or short of its own first characters, an unanchored value being free to start partway into a name.
- one carrying no separator at all and compared unanchored, which is that same gap with nothing in
  front of it to pin a fixture: a case name is then what the value may be matching, and case names
  are what this reads fewest of.
- one whose opening segment occurs in no source under either root, which is a value about an
  assembly this cannot see -- a test a package brings with it, or any filter posed where there are no
  test sources at all.

And two shapes of command it declines for, whatever their values, both about the tree the run will
find rather than about the filter. One is a segment ahead of the run that runs a program this cannot
place the writes of -- the word it opens with, or a word a substitution in it opens with -- with
`unread_writes` holding which names it can, and a backtick standing the segment down whole because
which of a pair opens the substitution is not read. The set is what is read, so a program nobody has
thought of costs a check rather than a wrong refusal against a tree it may have changed. The other
is a write this DOES place, onto a file the reading below would have taken names from: the hook is
posed before the command, so a fixture written in a heredoc is not on disk yet, and a refused hook
discards the whole command, so the refusal takes the source with the run it was for.

The last three declines are where the reading's under-approximation costs something. Of the 4328 case
names one EditMode run of this repository reports, 125 are missing from it: a name composed through a
variable or an interpolation, a case an abstract owner writes and each concrete heir reports as its
own, a name assembled from a case's arguments. Five of the 352 fixtures reporting a case are missing
too -- one a test a package brings with it, declared outside `Packages` and `Assets`, and four nested
fixtures, whose nesting the run spells with a `+` where this spells it with a `.`. Buying those back
is what the declines are for, and the price is measured rather than argued: over that run's 354 fixture
names and all three spellings of each of its 4328 case names, one value is refused -- the package's
fixture spelled without its namespace, a case of it behind, its opening segment landing inside an
unrelated type's name here so that the decline above does not fire.

Exit 2 refuses; exit 1 lets the tool through, so nothing here may raise.
"""

import hashlib
import importlib.util
import json
import os
import re
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "lib"))

from shell_commands import (comment_opens_at, leading_program, mask_shell_literals,  # noqa: E402
                            tokens_of, unexpanded, visible_segments)

HOOK_TOOLS = {"Bash"}

# A filter the shell will rewrite is not the text the runner receives. It is not among the spellings
# below either, so it stands down with them, and the note there is why refusing instead would be
# wrong: the check genuinely does not happen, and a wrong refusal costs the command.
UNEXPANDED_POLICY = "allow"
# Both probes name the tree, or they stand down over that rather than over what they are posed for.
UNEXPANDED_PROBE = 'Unity -runTests -batchmode -projectPath "$PWD" -testFilter "$FIXTURES"'

# The readings are a directory walk and a parse of the files in it. Neither git nor gh is consulted,
# so an unreadable repository state has no subject here.
UNREADABLE_POLICY = "none"
UNREADABLE_PROBE = {"command": 'Unity -runTests -batchmode -projectPath "$PWD" '
                               "-testFilter Velvet.Tests.NoSuchFixture"}

FILTER_FLAG = "-testFilter"

# The token that says the segment runs the test runner. Without it the operand after a `-testFilter`
# written as an argument to something else is read as a filter, and a `-projectPath` there settles a
# tree the run never opens. Measured over this project's transcripts, the only segment carrying the
# flag that requiring it costs is a grep for the flag's own name.
RUNNER_FLAG = "-runTests"

PROJECT_FLAG = "-projectPath"

# Programs a segment ahead of the run may be led by without this losing sight of what the command
# writes. Three groups, and nothing else: the ones `tracked_writes.py` places the writes of, as
# narrowly as the list it publishes of what it does not read; the ones carrying no way to name a file
# they write, so a redirect that module does place is the whole of it; and the ones whose writing
# creates no name for the reading below -- a directory, an empty file, a mode, a removal that leaves
# the tree smaller. `awk`, `find` and `xargs` are out for running a write from inside an operand, and
# `sed`, `sort` and `uniq` for `w`, `-o` and a second operand, each a way of naming a file to write
# that no published list carries. The set is what is read; every other name stands the command down,
# so one nobody has thought of costs a check rather than a wrong refusal.
#
# A loop's or a conditional's own words are here too, and the words that leave one, the body between
# them being a segment of its own that this asks the same question of;
# `shell_commands.LEADING_WORDS` carries the ones that open a segment rather than stand as one.
ACCOUNTED = {"cp", "mv",
             "cat", "echo", "printf", "grep", "head", "tail", "wc", "ls", "diff", "tr", "cut",
             "ps", "which", "pwd", "date", "basename", "dirname", "true", "false", "test", "[",
             "cd", "pushd", "popd", "mkdir", "rmdir", "rm", "touch", "chmod", "sleep",
             "for", "done", "fi", "case", "esac", "select", "exit", "break", "continue"}

# Where a substitution opens. `<(` and `>(` alongside `$(`, both running a program the word in
# front of them does not settle -- `diff <(python3 ...) x` opens with `diff`, which this places.
OPENS = re.compile(r"[$<>]\(")

# What separates one program from the next inside its body. `command_segments` splits at these too,
# so an unquoted substitution's later commands reach `unread_writes` as segments of their own; one
# written inside double quotes reaches it only through the reading below.
INSIDE = re.compile(r"[;&|\n]")

# A backtick opens and closes a substitution with the same character, so which occurrence a word
# follows takes a pairing this does not read. One anywhere ahead of the run stands the command down.
BACKTICK = "`"

# The two words above whose write `tracked_writes.py` finds by the word a segment opens with, which
# a substitution stands in front of -- so a copy written inside one is placed by nothing. Its
# redirect reading is per segment and does reach inside a substitution, which is why the rest of the
# set may open one.
PLACED_BY_A_LEADING_WORD = {"cp", "mv"}

# What the shell expands `-projectPath "$PWD"` to is where the command runs, which is read here rather
# than given up on: it is the spelling `CLAUDE.md` documents beside the flag, and standing down on it
# would stand down on the recipe.
PWD_SPELLINGS = {"$PWD", "${PWD}"}

# Where a Unity project keeps the sources it compiles. Walking the project root instead reaches
# `Library`, which is gigabytes, and the worktrees a session parks under `.claude`.
SOURCE_ROOTS = ("Packages", "Assets")

# Directories under those with no test source in them. `~` is the suffix Unity's own layout uses for a
# subtree it does not compile, which is where the Roslyn solution and its build output sit.
PRUNED = {"Library", "Temp", "obj", "bin", "node_modules", ".git"}

# The alphabet this answers for, once a leading `^` and a trailing `$` are off. Whitespace is in it
# because the runner splits its operand at the semicolons and trims nothing, so a space after one is
# part of the value beside it and has to be answered for rather than tidied away.
PLAIN = re.compile(r"[A-Za-z0-9_.\s]+")

# A case name written beside the case rather than taken from the method. Read as a whole literal only:
# one the fixture composes leaves no name here to add, which the case-name decline is what covers.
NAMED_CASE = re.compile(r'(?:\bTestName\s*=|\.SetName\s*\()\s*"([^"]*)"')


_HARNESS = []


def _harness():
    """base_red_check.py, which owns how a case's name is read off C#.

    Loaded by path because `scripts` holds no package, and lazily because this runs before every Bash
    command and all but a few of them never reach it. Kept once loaded, two readings here wanting it.
    """
    if not _HARNESS:
        path = Path(__file__).resolve().parents[3] / "scripts" / "test_quality" / "base_red_check.py"
        spec = importlib.util.spec_from_file_location("velvet_base_red_check", path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        _HARNESS.append(module)
    return _HARNESS[0]


def _writes():
    """tracked_writes.py, which owns where a command runs and which of its writes are readable.

    Imported here rather than beside the others for the reason `_harness` is loaded late: it pulls in
    two modules of its own, and importing it where they are put 6.8 ms on the median Bash command
    carrying no flag at all -- 44.3 ms against 37.5, 25 invocations each.
    """
    import tracked_writes
    return tracked_writes


def _cut_segments(command):
    """Each segment the mask left standing, cut where its comment opens.

    Visible, because `command_segments` hands back a heredoc body as a segment of its own text, and
    a body carrying this repository's own documented recipe was read as a run of it. Cut, because
    what follows an unquoted `#` never reaches a program.
    """
    found = []
    for segment in visible_segments(command):
        at = comment_opens_at(segment)
        found.append(segment if at is None else segment[:at])
    return found


def _operands_of(command, flag):
    """Each operand `flag` takes, over the segments that invoke the test runner.

    Both flags below are read the same way, so that neither can settle a question about a segment
    the other does not read: an operand written in a note beside the command, or handed to a program
    that is not the runner, decides nothing here.
    """
    found = []
    for segment in _cut_segments(command):
        tokens = tokens_of(segment)
        if RUNNER_FLAG not in tokens:
            continue
        for index, token in enumerate(tokens):
            if token == flag and index + 1 < len(tokens):
                found.append(tokens[index + 1])
    return found


def filters_in(command):
    """Every value a `-testFilter` in this command asks for, split at the semicolons that separate them.

    Split the way the runner splits it: empty parts dropped, and nothing trimmed. Trimming here reads
    `"A; B"` as two names and lets it through, where the runner keeps the space and the half behind it
    matches nothing -- a run green over one value of two, which is the failure this exists to catch.
    """
    return [part
            for operand in _operands_of(command, FILTER_FLAG)
            for part in operand.split(";") if part]


def _substitutions(segment):
    """(the segment with each substitution blanked, the masked body of each).

    Masked from just past the opening rather than through a mask of the whole segment: a
    substitution written inside double quotes has its opening blanked by that one, and this
    repository's own idle check is written `"$(ps ... | grep ...)"`. Masking at all is what keeps a
    separator inside a quoted operand from reading as one, a grep pattern being where those sit.

    A body that does not close inside this segment runs to its end, which is what the shell's own
    split leaves of `H=$(ps ... | grep ...)`: the half in front of the pipe. The half behind it is a
    segment of its own that the caller asks the same question of.
    """
    blanked = list(segment)
    bodies = []
    for opening in OPENS.finditer(segment):
        masked = mask_shell_literals(segment[opening.end():])
        depth, end = 0, len(masked)
        for index, character in enumerate(masked):
            if character == "(":
                depth += 1
            elif character == ")":
                if depth:
                    depth -= 1
                else:
                    end = index
                    break
        bodies.append(masked[:end])
        for offset in range(opening.start(), min(opening.end() + end + 1, len(segment))):
            blanked[offset] = " "
    return "".join(blanked), bodies


def _programs_run(segment):
    """(the word this segment opens with, the words its substitutions run programs by), or None.

    Two readings rather than one because the sets they are asked against differ, and separate from
    the segment's own word because a substitution stands in front of it: `H=$(ps ...)` opens with an
    assignment and runs `ps`.
    """
    if BACKTICK in segment:
        return None
    blanked, bodies = _substitutions(segment)
    tokens = tokens_of(blanked)
    index = leading_program(tokens)
    opened = []
    for body in bodies:
        for piece in INSIDE.split(body):
            words = piece.split()
            at = leading_program(words)
            if at < len(words):
                opened.append(words[at])
    return tokens[index] if index < len(tokens) else None, opened


def unread_writes(command):
    """Whether a segment ahead of the run could write a file this cannot place.

    Which names the tree carries when the run opens it is what every verdict below rests on, and a
    write this cannot see leaves that unknown. So the question is asked of the word the segment opens
    with and of each word a substitution in it runs one by, `ACCOUNTED` holding what those may be:
    every other name -- `python3`, `tee`, `git`, one nobody has thought of -- falls on the side that
    says nothing, and so does every other way of reaching a program, each of them a word that is not
    in the set. A refused hook discards the whole command, so a source written there is destroyed by
    the very refusal its absence produced.

    A substitution is one of those words, not a reason to stand down on the segment holding it. It
    was, and what that cost is the check `.claude/skills/unity-tests/SKILL.md` prescribes before a
    run: `H=$(ps -Ao command= | grep -c ...)` in front of the invocation left this guard silent on
    the shape its own evidence was gathered from.

    Ahead of the run, because the shell runs the segments in order and one behind the last of them
    cannot change the tree that run opened. A run of the runner ahead of another is not asked about:
    it is the invocation every verdict here is about, and a reading unable to place its writes could
    place no run at all.
    """
    segments = _cut_segments(command)
    words = [tokens_of(segment) for segment in segments]
    last = max((index for index, one in enumerate(words) if RUNNER_FLAG in one), default=-1)
    for segment, tokens in zip(segments[:last], words[:last]):
        if RUNNER_FLAG in tokens:
            continue
        programs = _programs_run(segment)
        if programs is None:
            return True
        own, opened = programs
        if own is not None and os.path.basename(own) not in ACCOUNTED:
            return True
        # The one spelling of those two that `tracked_writes.py` publishes as unread. Accounted for
        # by the word alone, a `cp -t` into a test directory is a source arriving that nothing here
        # places, and the refusal it earns discards the copy with the run it was for.
        if (own is not None and os.path.basename(own) in PLACED_BY_A_LEADING_WORD
                and _writes().names_a_target_directory(tokens)):
            return True
        if any(os.path.basename(one) not in ACCOUNTED - PLACED_BY_A_LEADING_WORD
               for one in opened):
            return True
    return False


def project_of(command, cwd):
    """The tree the run will read, or None where the command's own text does not settle which it is.

    Silence rather than the directory the tool call started in. A session drives sibling worktrees, so
    the two are different trees, and answering from the wrong one reads a fixture the other holds as a
    name nothing declares -- measured over this project's transcripts, refusing runs of fixtures that
    were there. Where a command moves partway through, `base_directory` gives that up rather than
    placing it, and nothing here reaches past it for a reading of its own.

    Two operands naming different trees is one of the shapes that goes unsettled: which of them the
    filter beside them is posed against wants a reading per segment, which is that same question.
    """
    named = set(_operands_of(command, PROJECT_FLAG))
    if len(named) != 1:
        return None
    value = named.pop()
    if value not in PWD_SPELLINGS:
        if unexpanded(value):
            return None
        if os.path.isabs(value):
            return value
    base = _writes().base_directory(command, cwd)
    if not base:
        return None
    return base if value in PWD_SPELLINGS else os.path.join(base, value)


def writes_a_source(command, cwd, project):
    """The files this command's own text writes that the tree below would read for the names it holds.

    The half of `unread_writes`'s question that a segment this DOES place still leaves open: a `cat`
    into a fixture is placed exactly and is still a name the tree cannot hold until the command runs.
    Asked over the whole command rather than ahead of the run alone, because a refused hook discards
    the command, so a source written behind the run is destroyed by the refusal too.
    """
    harness = _harness()
    found = []
    for path in _writes().literal_write_targets(command, cwd):
        relative = os.path.relpath(path, project)
        if relative.split(os.sep)[0] in SOURCE_ROOTS and reads_as_source(harness, relative):
            found.append(path)
    return found


def reads_as_source(harness, relative):
    """Whether the tree reads this path for the names it carries.

    One rule, because a file the command is about to write has to be recognised by the same reading
    that recognises the ones already on disk.
    """
    if relative.endswith(".asmdef"):
        return True
    return relative.endswith(".cs") and bool(harness.platform_of(relative))


# Where the readings below are kept between commands. Parsing this repository's test sources costs
# seconds, and a hook spends them in front of the command rather than while it runs: one that
# overruns its own timeout does not refuse, it goes quiet, and a quiet guard reads exactly like one
# with nothing to say.
#
# Keyed by a digest of a file's text rather than by its path and stamp. A worktree cut this morning
# then reads what a sibling parsed last week, which is the case that matters here -- a session drives
# several at once -- and there is no staleness to weigh against it, an edited file being a different
# key rather than a stale entry.
NAMES_KEPT = Path(tempfile.gettempdir()) / "velvet-testfilter-names"

# Nothing is dropped from it. One entry per distinct file text is 1.6 KB, this repository's whole
# test corpus 543 KB, and a bound would cost a directory listing on every entry written; the system
# temp directory is where it sits so that what accumulates is somebody else's to reap.


def _digest(text):
    return hashlib.blake2b(text.encode("utf-8"), digest_size=16).hexdigest()


def _kept_names(digest):
    """The parse kept for a text, or None where none is."""
    try:
        stored = json.loads((NAMES_KEPT / digest).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    if not isinstance(stored, dict):
        return None
    return {owner: set(cases) for owner, cases in stored.items()}


def _keep_names(digest, classes):
    """Keep a parse for the next command, or leave it unkept.

    Nothing here may fail the guard: a cache that cannot be written costs the next invocation the
    parse this one would have saved it, where a raise costs the command.
    """
    try:
        NAMES_KEPT.mkdir(parents=True, exist_ok=True)
        beside = NAMES_KEPT / "{}.{}".format(digest, os.getpid())
        beside.write_text(json.dumps({owner: sorted(cases) for owner, cases in classes.items()}),
                          encoding="utf-8")
        # Written beside and moved onto the name, so a second session reading the entry while this
        # one writes it takes either parse rather than half of one.
        os.replace(beside, NAMES_KEPT / digest)
    except OSError:
        return


class Tree:
    """The names Unity's test tree carries, read out of the project's test sources on demand.

    Parsing the whole corpus costs seconds and a filter asks about a handful of names, so the files a
    value could possibly match are picked by a raw-text scan first and only those are parsed. The scan
    is sound for the names this builds: each is spelled out of its file's own namespace, type and
    method declarations, so a file whose text does not hold one of the value's literal segments
    declares no name the value matches.
    """

    def __init__(self, harness, root):
        self.harness = harness
        self.root = root
        self.texts = {}
        self.parsed = {}
        # The suites above the fixtures, which a value may select whole subtrees by: one per namespace,
        # named for it, and one per assembly, named for the file Unity compiled it into.
        self.suites = set()
        for source in SOURCE_ROOTS:
            for directory, children, names in os.walk(os.path.join(root, source)):
                children[:] = [name for name in children
                               if name not in PRUNED and not name.endswith("~")]
                for name in names:
                    path = os.path.join(directory, name)
                    relative = os.path.relpath(path, root)
                    if not reads_as_source(harness, relative):
                        continue
                    if name.endswith(".asmdef"):
                        self._read_assembly(path)
                        continue
                    try:
                        text = Path(path).read_text(encoding="utf-8", errors="replace")
                    except OSError:
                        continue
                    self.texts[relative] = text
                    for found in harness.CSHARP_NAMESPACE.finditer(text):
                        declared = found.group(1)
                        self.suites |= {declared[:at] for at, character in enumerate(declared)
                                        if character == "."} | {declared}

    def _read_assembly(self, path):
        try:
            named = json.loads(Path(path).read_text(encoding="utf-8")).get("name")
        except Exception:  # noqa: BLE001 - an unreadable asmdef leaves one suite name unknown
            return
        if named:
            self.suites.add(named + ".dll")

    def classes_in(self, relative):
        """{fixture name, dotted as `base_red_check.py` writes it: the case names under it} for one file.

        A type declared abstract is left out and a class deriving from one is kept whether or not it
        declares a case of its own, because that is where its cases come from; `base_red_check.py`
        owns why a case written in an abstract fixture is reported under the heir.
        """
        if relative not in self.parsed:
            text = self.texts[relative]
            digest = _digest(text)
            found = _kept_names(digest)
            if found is None:
                found = self._parse(relative, text)
                _keep_names(digest, found)
            self.parsed[relative] = found
        return self.parsed[relative]

    def _parse(self, relative, text):
        """The reading kept under the text's digest, which every name below is a function of alone.

        `relative` reaches `base_red_check.py` as the path it hangs on a case for reporting; no name
        here is spelled out of it, which is what lets one file's reading answer for another holding
        the same bytes.
        """
        classes = {}
        for case in self.harness.csharp_cases(text, relative):
            if case.abstract_owner:
                continue
            classes.setdefault(case.name.rsplit(".", 1)[0], set()).add(case.name)
        # A heir is qualified by its namespace alone, so one declared inside another type is named
        # here as though it sat beside it. Its cases are already under the nesting spelling, so a
        # simple name the parse has settled is left alone rather than added twice under two names.
        settled = {owner.rsplit(".", 1)[-1] for owner in classes}
        for heirs in self.harness.concrete_heirs({relative: text}).values():
            for heir in heirs:
                if heir.rsplit(".", 1)[-1] not in settled:
                    classes.setdefault(heir, set())
        for found in NAMED_CASE.finditer(text):
            for owner in list(classes):
                classes[owner].add(owner + "." + found.group(1))
        return classes

    def candidates(self, value):
        """The files a value could match a name in -- the ones holding its rarest literal segment."""
        segments = [one for one in value.split(".") if one]
        if not segments:
            return list(self.texts)
        holding = [[relative for relative, text in self.texts.items() if segment in text]
                   for segment in segments]
        return min(holding, key=len)


class Value:
    """One `-testFilter` value, and what it selects."""

    def __init__(self, raw, anchored=None, separated=False):
        self.raw = raw
        self.excludes = raw.startswith("!")
        text = raw[1:] if self.excludes else raw
        written = len(text) > 1 and text.startswith("^") and text.endswith("$")
        self.text = text[1:-1] if written else text
        self.anchored = written if anchored is None else anchored
        self.readable = bool(PLAIN.fullmatch(self.text)) and not self.excludes
        # `separated` reads each `.` as the separator of a qualified name rather than as the wildcard
        # the runner reads it as. A value gets the runner's reading; the head below is not a value the
        # runner sees but a question about where one name ends and the next begins, and a wildcard
        # there lets a head run out in the MIDDLE of a name: one fixture spelled `Velvet` + a
        # character + `Tests` answers for every value spelled `Velvet.Tests.<anything>`.
        pattern = re.escape(self.text) if separated else self.text
        self.pattern = re.compile(pattern) if self.readable else None
        self.ending = re.compile(pattern + r"\Z") if self.readable else None
        self.reach = None

    def hits(self, name):
        probe = self.pattern.fullmatch if self.anchored else self.pattern.search
        return bool(probe(name))

    def tails(self, name):
        """Whether this runs out exactly where `name` does, starting wherever it may start in one."""
        probe = self.pattern.fullmatch if self.anchored else self.ending.search
        return bool(probe(name))

    def selected(self, tree):
        """{file: the fixtures in it this selects a case of}, or None where it selects the whole tree.

        None rather than every file, because a value matching a suite above the fixtures -- a
        namespace, an assembly -- selects each of them whole, and reading that as a set of files would
        mean parsing every one of them to say what nothing is left out of.
        """
        if self.reach is None:
            self.reach = self._reach(tree)
        return self.reach

    def _reach(self, tree):
        if any(self.hits(one) for one in tree.suites):
            return None
        found = {}
        for relative in tree.candidates(self.text):
            for owner, cases in tree.classes_in(relative).items():
                if self.hits(owner) or any(self.hits(one) for one in cases):
                    found.setdefault(relative, set()).add(owner)
        return found

    def selects(self, tree):
        reach = self.selected(tree)
        return reach is None or bool(reach)

    def may_be_outside_the_reading(self, tree):
        """Whether this value's opening segment occurs in no source under either root.

        Those two roots are the whole of what is read, and a test a package brings with it is
        compiled out of `Library`, which is gigabytes and is not walked. A name the tree does carry
        is written out in the file declaring it, and a value matching one carries its opening segment
        inside that name -- so a segment occurring in no file at all is a value about a tree this
        cannot see rather than a name that is not there. Containment rather than a comparison of
        names, which is what leaves a segment sitting inside an unrelated name answered for. A
        directory holding no test source is the same answer arrived at with nothing to occur in,
        which is why no separate reading of the tree's emptiness sits above this one.
        """
        opening = self.text.split(".")[0]
        return not any(opening in text for text in tree.texts.values())

    def may_be_a_case_name(self, tree):
        """Whether the case names this reads are too few to say this value names none of them.

        Not every case name is in the sources to be read: one composed from a literal handed to
        `SetName` through a variable, one an abstract owner writes and each heir elsewhere reports as
        its own. Whether that leaves this value unanswerable turns on what sits in front of its last
        segment. A full name joins a fixture to its case with a dot, so for the segment behind this
        value's last separator to be a case name at all, the segments in front of it have to run out
        exactly where some fixture's name does -- a comparison against the END of one rather than
        against the whole of one, an unanchored value being free to start partway into it. `HeadRemove`
        under `ReconcilerKeyedSuffixTrimTests` is the case this is for, and its fixture spelled
        without the namespace is the head that reaches no further than the fixture's last character.

        No head at all leaves an unanchored value matched against a whole name wherever it occurs in
        one, a case name included -- while an anchored value has to equal a full name, and a case's
        carries the fixture it runs under, which a value with no separator cannot.
        """
        if "." not in self.text:
            return not self.anchored
        head = Value(self.text.rsplit(".", 1)[0], anchored=self.anchored, separated=True)
        if not head.readable:
            return False
        return any(head.tails(owner)
                   for relative in tree.candidates(head.text)
                   for owner in tree.classes_in(relative))


def stem_declares(tree, value):
    """(the file this value is probably named for, the classes it declares), where the tree holds one."""
    stem = value.text.rsplit(".", 1)[-1]
    for relative in tree.texts:
        if Path(relative).stem != stem:
            continue
        classes = set(tree.classes_in(relative))
        if classes:
            return relative, sorted(classes)
    return None


def refusal(tree, unselecting):
    lines = [
        "Refusing this -testFilter: it asks for a value that selects no test, so the run reports "
        "green over whatever is left of the filter.",
        "",
    ]
    for value in unselecting:
        # Quoted, because a value the runner was handed with a space on it is otherwise indented
        # into the same shape as one without, and the line under it then reads as the tree
        # declaring the very name being refused.
        lines.append(f'  "{value.raw}"')
        if value.raw != value.raw.strip():
            lines.append("    the runner splits this operand at its semicolons and trims nothing, "
                         "so the whitespace is part of the value")
        named = stem_declares(tree, value)
        if named:
            relative, classes = named
            lines.append(f"    {relative} declares:")
            lines += [f"      {one}" for one in classes]
    lines += [
        "",
        "A -testFilter value is matched against each test's full name, never against a file's name: "
        "a test file here may declare several fixture classes, and some declare none matching their "
        "own stem, so a filter derived from the files a change touched selects a set nobody chose. "
        "Name a class a file declares.",
    ]
    return "\n".join(lines)


def notice(missed):
    lines = [
        "This -testFilter runs fewer fixtures than the files it selects from hold. The count it "
        "reports will be green over the smaller set, and nothing in the results says so.",
        "",
    ]
    for relative, unselected in missed:
        lines.append(f"  {relative} also declares:")
        lines += [f"    {one}" for one in unselected]
    lines += [
        "",
        "Add them if the change reaches them. A single-fixture run is a legitimate thing to ask for, "
        "so this is a notice rather than a refusal.",
    ]
    return "\n".join(lines)


def _unselected(tree, values):
    """(file, the fixtures in it no value selects), for each file some value selects from."""
    reached = {}
    for value in values:
        found = value.selected(tree)
        if found is None:
            # A value matching a suite above the fixtures selects the files under it whole, leaving
            # nothing out of them. The whole notice stands down rather than reporting over the values
            # beside it, whose gaps this one may be exactly what fills.
            return []
        for relative, owners in found.items():
            reached.setdefault(relative, set()).update(owners)
    missed = []
    for relative in sorted(reached):
        unselected = sorted(owner for owner in tree.classes_in(relative)
                            if owner not in reached[relative])
        if unselected:
            missed.append((relative, unselected))
    return missed


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    try:
        if not isinstance(event, dict) or event.get("tool_name") not in HOOK_TOOLS:
            return 0
        command = event.get("tool_input", {}).get("command") or ""
        if not isinstance(command, str) or FILTER_FLAG not in command:
            return 0
        asked = [Value(one) for one in filters_in(command)]
        if not asked:
            return 0
        project = project_of(command, event.get("cwd"))
        if not project:
            return 0
        if unread_writes(command) or writes_a_source(command, event.get("cwd"), project):
            return 0

        tree = Tree(_harness(), project)
        selecting, unselecting = [], []
        for value in asked:
            if not value.readable:
                continue
            if value.selects(tree):
                selecting.append(value)
            elif not value.may_be_a_case_name(tree) and not value.may_be_outside_the_reading(tree):
                unselecting.append(value)

        if unselecting:
            sys.stderr.write(refusal(tree, unselecting) + "\n")
            return 2
        if len(selecting) < len(asked):
            # One value this cannot read leaves the run's whole set unknown, and a notice over the
            # rest would name fixtures that value may well be selecting.
            return 0
        missed = _unselected(tree, selecting)
        if missed:
            # `systemMessage` and nothing beside it. Any `permissionDecision` answers the permission
            # question as well, and this guard has no answer to that one: an under-selecting filter is
            # not grounds for approving a command the permission system would otherwise put to
            # somebody, nor for refusing it.
            json.dump({"systemMessage": notice(missed)}, sys.stdout)
        return 0
    except Exception as failure:  # noqa: BLE001 - a raise here turns the guard off silently
        print(f"filter_selecting_no_test: {failure}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
