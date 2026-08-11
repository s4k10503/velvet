#!/usr/bin/env python3
"""Report every mutation of this branch's changed lines that no test failed on.

A test that asserts nothing passes whether or not the code under test works, and the suite is
green either way, so nothing in CI can see it. Mutating the code the branch touched and rerunning
the suite is the only check that asks the question directly: change the behaviour, and if the
suite still passes, no test was measuring it.

A mutant surviving is a question, not a verdict: it is either a test that stopped asking, or a
mutation the behaviour does not depend on. Both need a person to read them.

The default scope is the whole platform suite rather than the fixtures nearest the mutated file,
so that nothing is reported as surviving merely because the fixture that would have killed it was
out of scope.
"""

import argparse
import hashlib
import json
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

DEFAULT_UNITY = "/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity"
# Anchored at the editor binary so that a shell waiting on this pattern does not match itself and
# report a busy machine forever on an idle one.
UNITY_RUNNING = "^/Applications/.*/MacOS/Unity -runTests"

KILLED = "killed"
SURVIVED = "survived"
INCONCLUSIVE = "survived (inconclusive)"
UNCOMPILABLE = "uncompilable"
NOT_BUILT = "not rebuilt"


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


# The furthest offset from an opening quote at which a closing one can still sit: `'\U0001F600'` is
# the longest character literal C# can spell.
CHARACTER_LITERAL_REACH = len("'\\U0001F600'") - 1


def code_mask(text):
    """True at every offset that the compiler sees as code.

    Mutating a comment or a string literal produces a mutant that cannot change behaviour, and
    each one still costs a full compile-and-run cycle.
    """
    mask = [True] * len(text)
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
            for j in range(i, end):
                mask[j] = False
            i = end
        elif two == "//":
            end = text.find("\n", i)
            end = n if end < 0 else end
            for j in range(i, end):
                mask[j] = False
            i = end
        elif two == "/*":
            end = text.find("*/", i + 2)
            end = n if end < 0 else end + 2
            for j in range(i, end):
                mask[j] = False
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
            for j in range(start, min(i, n)):
                mask[j] = False
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
            for j in range(start, min(i, n)):
                mask[j] = False
        else:
            i += 1
    return mask


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

# A call whose value is discarded is there for its side effect, so deleting the statement removes
# exactly one behaviour and leaves the rest of the method intact.
VOID_CALL = re.compile(r"^[A-Za-z_][A-Za-z0-9_.]*(\.[A-Za-z_][A-Za-z0-9_]*)*\s*\([^;]*\)\s*;$")

# Every operator above keeps the clause it lands in participating in the condition, so a clause no test
# reaches survives all of them: swapping its comparison or its join still leaves some test's own clause
# deciding the outcome. Removing the clause is the only mutation that asks whether anything depends on
# that condition existing, and the same holds one level out for a guard statement, whose condition the
# operators above mutate while none of them deletes the guard.
LOGIC_JOINS = (" && ", " || ")
GUARD_STATEMENT = re.compile(r"^if \(.+\)\s*(?:return[^;]*|continue|break);$")


def encloses_its_own_groups(line, start, mask, limit):
    """Whether every parenthesis the line opens it also closes, and it closes none it did not open."""
    depth = 0
    for index in range(limit):
        if not mask[start + index]:
            continue
        if line[index] == "(":
            depth += 1
        elif line[index] == ")":
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
        if character == "(":
            depth += 1
        elif character == ")":
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
                if character == "(":
                    level += 1
                elif character == ")":
                    if level == depth:
                        break
                    level -= 1
                elif level == depth and any(line.startswith(c, probe) for c in LOGIC_JOINS):
                    break
            probe += 1
        text = line[column:probe]
        if text.strip() != join.strip():
            cuts.append((column, text))
    return cuts


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
        for before, after, operator in OPERATORS:
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
        if VOID_CALL.match(stripped) and not stripped.startswith(("return", "throw", "yield")):
            found.append(Mutant(path, number, 0, stripped, ";", "void call removed"))
        limit = len(line.rstrip())
        for column, text in clause_cuts(line, start, mask, limit):
            found.append(Mutant(path, number, column, text, "", "clause removed"))
        if GUARD_STATEMENT.match(stripped) and all(mask[start:start + limit]):
            found.append(Mutant(path, number, line.index(stripped), stripped, "", "guard removed"))
    return found


def apply_mutation(text, mutant):
    spans = line_spans(text)
    start, end = spans[mutant.line - 1]
    line = text[start:end]
    if mutant.operator in ("clause removed", "guard removed"):
        mutated = line[:mutant.column] + line[mutant.column + len(mutant.before):]
    elif mutant.operator == "void call removed":
        mutated = line.replace(mutant.before, ";", 1)
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


def changed_files_and_lines(project, base):
    merge_base = subprocess.run(
        ["git", "-C", str(project), "merge-base", base, "HEAD"],
        capture_output=True, text=True,
    )
    if merge_base.returncode != 0:
        raise SystemExit("cannot resolve a merge base with {}: {}".format(base, merge_base.stderr.strip()))
    # Diffing the merge base against the working tree rather than against HEAD, so a branch whose
    # change is not committed yet is still measured.
    diff = subprocess.run(
        ["git", "-C", str(project), "diff", "--unified=0", merge_base.stdout.strip()],
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


def run_suite(unity, project, platform, scope, results, log, timeout):
    """Returns the wall clock and whether the editor had to be killed.

    A mutation can turn a loop bound into one that never terminates, and the run would otherwise
    wait on it for as long as the machine is left alone.
    """
    command = [
        unity, "-runTests", "-batchmode", "-projectPath", str(project),
        "-testPlatform", platform, "-testResults", str(results), "-logFile", str(log),
    ]
    command += scope
    start = time.time()
    try:
        subprocess.run(command, timeout=timeout)
        return time.time() - start, False
    except subprocess.TimeoutExpired:
        return time.time() - start, True


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
    root = ET.parse(str(results)).getroot()
    if root.tag != "test-run":
        return None
    return {key: int(root.get(key, "0")) for key in ("total", "passed", "failed", "inconclusive")}


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
                        help="seconds before a mutant run is killed and counted as killed (default: 900)")
    parser.add_argument("--busy-timeout", type=int, default=1800,
                        help="seconds to wait for another Unity run to finish (default: 1800)")
    parser.add_argument("--output", default="", help="directory for the per-mutant logs and XML")
    parser.add_argument("--unity", default=DEFAULT_UNITY, help="editor binary (default: the pinned macOS one)")
    args = parser.parse_args()

    project = Path(args.project).resolve()
    output = Path(args.output).resolve() if args.output else project / "Logs" / "mutation_check"
    output.mkdir(parents=True, exist_ok=True)
    scope = []
    if args.assemblies:
        scope += ["-assemblyNames", args.assemblies]
    if args.filter:
        scope += ["-testFilter", args.filter]

    if args.files:
        targets = {}
        for name in args.files:
            path = Path(name).resolve()
            if mutable(path, project):
                targets[path] = set(range(1, len(path.read_text().splitlines()) + 1))
            else:
                print("skipping {}: not a mutable package source".format(name))
    else:
        targets = {
            path: lines
            for path, lines in changed_files_and_lines(project, args.base).items()
            if mutable(path, project)
        }

    mutants = []
    for path, lines in sorted(targets.items()):
        mutants.extend(mutations_for(path, path.read_text(), lines))
    if not mutants:
        print("no mutable change found")
        return 0
    if args.list:
        for mutant in mutants:
            print(mutant.describe(project))
        print("{} mutant(s); a run covers the first {}".format(len(mutants), args.max))
        return 0

    truncated = len(mutants) > args.max
    mutants = mutants[:args.max]

    if not wait_for_quiet(args.busy_timeout):
        raise SystemExit("another Unity test run is still in flight after {}s".format(args.busy_timeout))

    print("{} mutant(s) over {} file(s)".format(len(mutants), len(targets)))
    if truncated:
        print("(capped by --max; raise it to cover the rest)")

    baseline_results = output / "baseline.xml"
    baseline_wall, baseline_timed_out = run_suite(args.unity, project, args.platform, scope,
                                                  baseline_results, output / "baseline.log", args.timeout)
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
            mutant.path.write_text(apply_mutation(originals[mutant.path], mutant))
            results = output / "mutant-{:03d}.xml".format(index)
            log = output / "mutant-{:03d}.log".format(index)
            if results.exists():
                results.unlink()
            wait_for_quiet(args.busy_timeout)
            wall, timed_out = run_suite(args.unity, project, args.platform, scope, results, log,
                                        args.timeout)
            mutant.path.write_text(originals[mutant.path])

            counts = read_counts(results)
            dll = assemblies_dir / "{}.dll".format(assembly_of(mutant.path))
            if timed_out:
                mutant.verdict = KILLED
                mutant.detail = "the run did not finish; the mutation left something not terminating"
            elif "error CS" in (log.read_text(errors="replace") if log.exists() else ""):
                mutant.verdict = UNCOMPILABLE
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
        for path, text in originals.items():
            path.write_text(text)

    print("\n--- mutants no test killed ---")
    survivors = [m for m in mutants if m.verdict in (SURVIVED, INCONCLUSIVE)]
    for mutant in survivors:
        print("{}  [{}] {}".format(mutant.describe(project), mutant.verdict, mutant.detail))
    if not survivors:
        print("(none)")

    unmeasured = [m for m in mutants if m.verdict == NOT_BUILT]
    if unmeasured:
        print("\n--- mutants the editor never compiled; nothing was asked of the suite ---")
        for mutant in unmeasured:
            print("{}  {}".format(mutant.describe(project), mutant.detail))

    # Counts of what this run did, and deliberately no ratio: a mutation score over a diff is a
    # different denominator every branch, and a percentage is the part that gets quoted after the
    # run it came from is forgotten. The survivors above are the output; this is the receipt.
    tally = {}
    for mutant in mutants:
        tally[mutant.verdict] = tally.get(mutant.verdict, 0) + 1
    print("\n" + ", ".join("{}: {}".format(key, value) for key, value in sorted(tally.items())))
    print("logs: {}".format(output))
    return 1 if survivors or unmeasured else 0


if __name__ == "__main__":
    sys.exit(main())
