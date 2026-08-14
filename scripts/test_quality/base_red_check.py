#!/usr/bin/env python3
"""Run this branch's changed test cases against the merge base and report every one that passes there.

A test is written to separate a behaviour from its absence. The only reading that shows it does is the
one taken before the change it pins: green on the branch and green on the base together mean the case
would have passed whatever the branch did to production code. Nothing in this repository asked that
question, so it stayed a matter of attentiveness. Three of the four branches this was first run over
had a case that passed on the base and had been reported as pinning something.

What runs is the branch's test file over the base's production code: the base commit is checked out,
the branch's test-side files are copied onto it, and the changed cases are executed there.

**Success here means every changed case was measured on a base tree that demonstrably answers.** Not
that nothing failed: most of what can go wrong with a run like this ends in a reading nobody took,
and a reading nobody took must never be a pass. That is one sentence and it decides the shape of
everything below, because the verdict that carries the evidence -- a case the base could not build --
is indistinguishable, from the results file alone, from a base that built nothing, ran nothing, or
was never asked. So each of those is closed separately:

*The tree is read by cases the branch did not carry.* `canary_fixtures` picks fixtures of the base's
own for a platform and `python_canaries` picks cases of the base's own for that lane, and where either
found any, at least one has to pass. A tree where nothing built reports every case as uncompilable,
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

*A case that cannot compile is evidence, not an error* -- it names something the branch adds -- and
that reading holds only where the same file compiles on the branch, which is the condition running
this after the branch's own suite satisfies. A round that reports no case of a fixture is one where
something the branch carried did not build: that file is withdrawn and the run repeated, so the rest
is still measured rather than lost behind it. Which file is picked off the editor log, over every
carried file rather than only the ones holding cases, since a shared helper takes its whole assembly
down with it. A runner that hands back a results file and nothing else can still take every verdict
-- one round of it, without the withdrawing.

*A case that stopped before it disagreed answered nothing.* Red on the base means the base ran the
case and the case said no. A case that dies reaching for what the branch adds said nothing at all,
in either lane and under either lane's spelling of it: a Python import or attribute, and -- since
reflecting from the test assembly for private production state is this repository's convention -- a
C# fixture that compiles on the base and throws where it would have compared. A case reported
Inconclusive or Skipped there did not run to a verdict either, nor did one the runner refused as
non-runnable or one whose run was cancelled. Counting any of those as red hands back the evidence the
gate exists to demand, in the exact shape it was written to refuse: the branch adds a helper, every
case in the module that reaches for it dies there, and the run reports them all as pinning something.
So they take a verdict of their own, which says the base could not answer and leaves the red count to
the cases that were answered.

*An exception is not that reading by itself.* A branch that fixes a crash leaves the base throwing
inside the production code the fix repairs, which is the base disagreeing in the plainest way there
is -- and it arrives under the same label as the case that died in its own scaffolding. What
separates them is the first frame of the throw that names a file of this tree: production code, or
the test side. A throw naming no such file keeps the non-answer, so what this reads as red is bounded
by what the results file carries.

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
import json
import re
import shutil
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from pathlib import Path

DEFAULT_UNITY = "/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity"
# Anchored at the editor binary so that a shell waiting on this pattern does not match itself and
# report a busy machine forever on an idle one.
UNITY_RUNNING = "^/Applications/.*/MacOS/Unity -runTests"

PASSED_ON_BASE = "passed on the base"
RED_ON_BASE = "red on the base"
COULD_NOT_COMPILE = "could not compile there"
COULD_NOT_ANSWER = "could not answer there"
DECLARED_KEPT = "declared, and green as declared"
DECLARED_STALE = "declared, and not as declared"
NOT_REPORTED = "no result was written"
BASE_UNSOUND = "the base tree cannot answer"

FAILING_VERDICTS = (PASSED_ON_BASE, DECLARED_STALE, NOT_REPORTED, BASE_UNSOUND)

# What a runner reports for a case that stopped before it disagreed with anything, and what to print
# for each. `reading_of` and `python_outcome` each name a non-answer out of this one dict, so a
# reading is worded the same whichever lane took it.
NOT_AN_ANSWER = {
    "Error": "it raised there rather than failing an assertion",
    "Inconclusive": "an assumption of its own was false there",
    "Skipped": "it was skipped there",
    "Invalid": "the runner would not run it there",
    "Cancelled": "its run was cancelled there",
}

# Which of those a results file spells as a `label` beside the result rather than as the result
# itself. Kept apart from the readings so that there is a set to hold against NUnit's own, which the
# dict above is not: its keys are results and labels together. A label missing from here reads as the
# result beside it, which for all three is `Failed` -- a disagreement, and over a declaration a
# correct declaration told to delete itself. `ResultStateVocabularyTests` fails when this stops
# matching the labels NUnit pairs with a failing status.
NOT_A_VERDICT_LABEL = ("Cancelled", "Error", "Invalid")

CATEGORIES = ("characterization", "refactor")

# The reason has to say something a reviewer can disagree with, and a word count is the only part of
# that a script can hold. Four words rules out "n/a" and "see above" without pretending to judge prose.
MINIMUM_REASON_WORDS = 4

DECLARATION = re.compile(r"GREEN_ON_BASE\(([A-Za-z]*)\)\s*:\s*(.*)")

# The Python lane has no -testPlatform to be named by, and the soundness plumbing keys on one.
PYTHON_LANE = "python"

# unittest's closing line, which is where it separates an assertion that disagreed from an exception
# that stopped the case.
UNITTEST_SUMMARY = re.compile(r"^(OK|FAILED)(?:\s*\((.*)\))?\s*$", re.MULTILINE)


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
# How a declaration's reason is read comes from there for the same reason: CONTRIBUTING.md says the
# two markers are read one way, which a second copy of the fold here would be free to stop being.
_mutation_check = _sibling("mutation_check")
code_mask = _mutation_check.code_mask
line_spans = _mutation_check.line_spans
folded_reason = _mutation_check.folded_reason


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

CSHARP_TEST_DIR = re.compile(r"/Tests/(Editor|PlayMode)/")
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


def code_lines(text):
    """Each line with every comment, string and character literal blanked to spaces.

    Blanked rather than removed, so a code line is as long as the raw one and the two can be indexed
    together -- which they are, since a declaration lives in a comment and everything else does not,
    and `MaskedLineTests` fails when one stops matching. Stripping the terminator off the end instead
    of cutting at the raw line's length does not hold that: a mask that swallows the terminator -- a
    line comment on a CRLF file, or a verbatim string or block comment crossing to the next line --
    leaves a space in its place, which no strip removes and which moves every offset after it.
    """
    mask = code_mask(text)
    return ["".join(text[start + offset] if mask[start + offset] else " "
                    for offset in range(len(raw)))
            for raw, (start, _) in zip(text.splitlines(), line_spans(text))]


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


def comment_block_start(lines, index):
    """The first line of the contiguous comment block directly above `index`, or `index` itself.

    Directly above and nothing between: a blank line or any code ends the block. A comment further up
    belongs to whatever sits under it, and reaching past the gap would let one case's prose cover a
    neighbour nobody wrote it for. The block counts as part of the case, so editing the declaration
    below re-poses the question the declaration answers.
    """
    probe = index
    while probe > 0 and lines[probe - 1].strip().startswith(("//", "#")):
        probe -= 1
    return probe


def leading_declaration(lines, index):
    """The declaration in the comment block `comment_block_start` delimits, if there is one."""
    for probe in range(comment_block_start(lines, index), index):
        match = DECLARATION.search(lines[probe].strip())
        if match:
            claim, reason = folded_reason(match.group(2), lines[probe + 1:index])
            return Declaration(match.group(1), reason, claim=claim, line=probe + 1, through=index)
    return None


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
                case = Case(qualified, path, comment_block_start(lines, block) + 1,
                            member_end(code, ends, index) + 1, leading_declaration(lines, block))
                case.abstract_owner = types[-1][0] if types and types[-1][2] else None
                cases.append(case)
            continue
        index += 1
    return cases


def python_cases(text, path="?"):
    """Every unittest case in a Python test module, named as `python3 -m unittest` takes it."""
    try:
        tree = ast.parse(text)
    except SyntaxError:
        return []
    lines = text.splitlines()
    cases = []
    for node in ast.walk(tree):
        if not isinstance(node, ast.ClassDef):
            continue
        for member in node.body:
            if not isinstance(member, (ast.FunctionDef, ast.AsyncFunctionDef)):
                continue
            if not member.name.startswith("test"):
                continue
            declared = min([member.lineno] + [decorator.lineno for decorator in member.decorator_list])
            cases.append(Case("{}.{}".format(node.name, member.name), path,
                              comment_block_start(lines, declared - 1) + 1, member.end_lineno,
                              leading_declaration(lines, declared - 1)))
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

def clone_tree(source, destination):
    """Copies a Library into the base tree, asking the filesystem to share the blocks if it will.

    The base tree is a checkout the machine has never imported, and the import is most of what a base
    run costs. A byte copy of a Library this size is its own kind of slow, so `cp -c` is tried first
    and the plain copy is what happens when it is refused.
    """
    if sys.platform == "darwin":
        result = subprocess.run(["cp", "-Rc", str(source), str(destination)],
                                capture_output=True, text=True)
        if result.returncode == 0:
            return
    shutil.copytree(str(source), str(destination), symlinks=True, dirs_exist_ok=True)


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
    """Waits rather than sharing the machine, for the reason mutation_check.py waits."""
    deadline = time.time() + seconds
    while unity_busy():
        if time.time() > deadline:
            return False
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


def threw_in_production(case):
    """Whether the throw that stopped this case came from production code rather than its own body.

    Two shapes arrive under one label and only one of them answered nothing. A fixture reflecting
    from the test assembly for private production state the base has not got gets null back and
    throws where it would have compared: that reached for what the branch adds. A branch that fixes
    a crash leaves the base throwing inside the production code the fix repairs: that is the base
    disagreeing.

    The separator is the first frame that names a file of this tree. Frames naming none are skipped
    rather than decided on, since they place the throw on neither side. A throw naming no file of
    this tree at all keeps the non-answer, because the verdicts that fail a run are not ones to take
    from a reading nobody could complete.
    """
    trace = case.find("./failure/stack-trace")
    text = (trace.text or "") if trace is not None else ""
    for match in STACK_FRAME_SOURCE.finditer(text):
        return not is_test_side(match.group(1))
    return False


def reading_of(case):
    """One reported case's reading: its label where that names one, and its result otherwise.

    The label is preferred so that a case which never reached a verdict is not read as the base
    disagreeing with it -- the same line `python_outcome` draws off a unittest trailer. Which throws
    those are is `threw_in_production`'s question.

    Never over a passing case: nothing a label names can reach `passed on the base`, so a label read
    there could only ever turn that verdict into silence.
    """
    result = case.get("result")
    label = case.get("label")
    if result == "Passed" or label not in NOT_A_VERDICT_LABEL:
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


def run_python(tree, cases, transcript):
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
        outcome[case.key] = python_outcome(printed)
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


def as_plan(since, cases, control, shared, canaries):
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


def report(cases, control, reported, canaries=None, wrote=True):
    """Prints each case's verdict and returns the ones that fail the run.

    `wrote` is whether the run produced a results file at all, and it is read before anything in it.
    A run that wrote nothing measured nothing, and the verdict for a case nothing measured is not
    COULD_NOT_COMPILE -- that reading says the base built the rest and not this, which takes a base
    that built something.
    """
    unsound = unsound_fixtures(control, reported)
    withdrawn = unsound_platforms(canaries or {}, reported)
    if not wrote:
        for platform in {platform_of(case.path) for case in cases
                         if kind_of(case.path) == "csharp"}:
            withdrawn[platform] = "the run wrote no results file, so nothing was measured"
    ran = fixtures_that_ran(reported)
    for case in cases:
        if case.fixture in unsound:
            case.verdict, case.detail = BASE_UNSOUND, unsound[case.fixture]
        elif measured_by(case.path) in withdrawn:
            case.verdict, case.detail = BASE_UNSOUND, withdrawn[measured_by(case.path)]
        else:
            case.verdict, case.detail = decide(case, outcome_for(case.key, reported),
                                               case.fixture in ran)
    print("\n--- what the base said ---")
    for case in cases:
        print("{:<32} {}  ({})".format(case.verdict, case.name, case.detail))
    # Two counts, never one. A case the base could not build names a symbol the branch adds, which is
    # the strongest pin this takes, and folding it into the readings nobody took would tell the author
    # of a correct test that the run measured nothing.
    silent = [case for case in cases if case.verdict == COULD_NOT_ANSWER]
    if silent:
        print("\nthe base could not answer for {} of {} case(s), so they carry no reading either "
              "way".format(len(silent), len(cases)))
    unbuilt = [case for case in cases if case.verdict == COULD_NOT_COMPILE]
    if unbuilt:
        print("\n{} of {} case(s) sit in a fixture the base built none of, so the reading is that "
              "fixture's rather than each case's".format(len(unbuilt), len(cases)))
    offenders = [case for case in cases if case.verdict in FAILING_VERDICTS]
    if offenders:
        print("\n{} case(s) the base already answers, or cannot be answered for. Green on both sides "
              "separates\nnothing: sharpen the case until it goes red without this branch, or say why "
              "it belongs there:".format(len(offenders)))
        print("  // GREEN_ON_BASE({}): <why>".format("|".join(CATEGORIES)))
    return offenders


def corpus_of(project):
    """Every tracked C# test file's text, for the questions one file cannot answer about itself."""
    names = git(project, "ls-files").stdout.splitlines()
    return {relative: (project / relative).read_text(encoding="utf-8", errors="replace")
            for relative in names
            if kind_of(relative) == "csharp" and (project / relative).exists()}


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
    for relative, lines in sorted(by_file.items()):
        if kind_of(relative) is None or (lane != "both" and kind_of(relative) != lane):
            continue
        source = project / relative
        if not source.exists():
            continue
        cases = cases_in(relative, source.read_text())
        for case in cases:
            if case.declaration is not None and lines is not None:
                case.declaration.written_here = case.declaration.written_in(lines)
        wanted = {case.name for case in touched(cases, lines)}
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

    if args.verdict:
        plan = json.loads(Path(args.verdict).read_text())
        if not plan["cases"]:
            print("no changed test case in the reading")
            return 0
        if not args.results:
            raise SystemExit("--verdict needs --results")
        reported, wrote = results_from(args.results)
        if not wrote:
            print("the base run wrote no result, so nothing it was asked was measured")
        return 1 if report(from_plan(plan["cases"]), from_plan(plan["control"]), reported,
                           plan["canaries"], wrote) else 0

    project = Path(args.project).resolve()
    since, cases, control, shared, shared_helper = collect(project, args.base, args.lane)
    if not cases:
        print("no changed test case in scope of --lane {}".format(args.lane))
        return 0

    print("merge base {}".format(since[:12]))
    for case in cases:
        print("  {}{}".format(case.name,
                              " [{}]".format(case.declaration.category)
                              if case.declaration and case.declaration.written_here else ""))
    print("  ({} control case(s) alongside)".format(len(control)))
    for relative, count in sorted(shared.items()):
        print("  no control: {} line(s) of {} sit in no case -- a SetUp, a field, a helper, a using. "
              "The\n              cases beside them are this branch's text too, so none of them reads "
              "the base tree.".format(count, relative))
    for relative in shared_helper:
        print("  no control: {} is carried onto the base, so every case that calls it is this "
              "branch's\n              text there. The base's own fixtures are what read the tree."
              .format(relative))
    if args.plan:
        return 0

    changed = changed_lines_by_file(project, since)
    carry = sorted(name for name in changed if is_test_side(name) and (project / name).exists())
    drop = sorted(name for name in deleted_files(project, since) if is_test_side(name))

    if args.emit:
        if not args.base_tree:
            raise SystemExit("--emit needs --base-tree to say where the base is built")
        base_tree = Path(args.base_tree).resolve()
        build_base_tree(project, since, base_tree, carry, drop, args.warm_library)
        canaries = canaries_for(base_tree, cases, carry, args.platform)
        Path(args.emit).write_text(
            json.dumps(as_plan(since, cases, control, shared, canaries), indent=2))
        # The fixtures, not the cases: a run over the fixture brings the control cases with it, and
        # they are what says whether the tree it built can answer at all.
        wanted = {case.fixture for case in cases + control if kind_of(case.path) == "csharp"
                  and (not args.platform or platform_of(case.path) in args.platform)}
        for chosen in canaries.values():
            wanted.update(chosen)
        print("fixtures={}".format(";".join(sorted(wanted))))
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
    try:
        build_base_tree(project, since, base_tree, carry, drop, args.warm_library)

        python_lane = [case for case in cases + control if kind_of(case.path) == "python"]
        if python_lane:
            guards = python_canaries(base_tree, carry)
            canaries[PYTHON_LANE] = [case.fixture for case in guards]
            reported.update(run_python(base_tree, python_lane + guards, transcript))

        platforms = sorted({platform_of(case.path) for case in cases
                            if kind_of(case.path) == "csharp"})
        if args.platform:
            platforms = [name for name in platforms if name in args.platform]
        if platforms and not wait_for_quiet(args.busy_timeout):
            raise SystemExit("another Unity test run is still in flight")
        canaries.update(canaries_for(base_tree, cases, carry, platforms))
        for platform in platforms:
            wanted = [case for case in cases + control
                      if kind_of(case.path) == "csharp" and platform_of(case.path) == platform]
            withdrawn = set()
            for attempt in range(1, args.max_rounds + 1):
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
                    platform, attempt, len(seen), len(fixtures), wall))

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
        print("no round wrote a result, so nothing any of them was asked was measured")
    offenders = report(cases, control, reported, canaries, ever_wrote)
    print("\nlogs: {}".format(output))
    return 1 if offenders else 0


if __name__ == "__main__":
    sys.exit(main())
