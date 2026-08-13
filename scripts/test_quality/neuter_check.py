#!/usr/bin/env python3
"""Report every test that still passes with the mechanism it is named for disabled.

Mutating a line asks whether any test depends on that line. Disabling a named mechanism at a named
layer asks the question a fixture's name makes: with clip-path resolving nothing, does a test called
"the element is wrapped" still pass? A test that does was never measuring the thing it claims.

Which layer the cut is made at decides the answer. Some vacuous tests are green at the applier cut and
red at the parser cut, because a gate read one layer up survives a neuter one layer down; others are
green at every cut of their mechanism, having no term any of them can move. A single cut therefore
undercounts, and a count means nothing without the cut beside it.

So scope is declared rather than assumed, in neuter_cuts.json, at two granularities — and both are
needed, which the first run of this harness established by getting the second one wrong. A fixture is
asked only the cuts it reaches, or a parser-only fixture reports its whole body as holes under an
applier cut. And a fixture holding cases for two mechanisms declares which of its cases belong to
which, or every ring case in a clip fixture reports as a hole when the clip applier dies: true, and
about nothing.

This does not replace mutation_check.py. That one mutates syntax across a diff and asks whether any
test noticed; this one disables a feature and asks whether the tests named after it noticed. A
mutation lands inside one method, so it cannot answer a question about a mechanism spread over two
files, and a cut cannot answer one about a single boundary condition.
"""

import argparse
import json
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

DEFAULT_UNITY = "/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity"
UNITY_RUNNING = "^/Applications/.*/MacOS/Unity -runTests"

CUTS_FILE = "scripts/test_quality/neuter_cuts.json"
UNCOVERED_FILE = "scripts/test_quality/neuter_uncovered.txt"
HOLES_FILE = "scripts/test_quality/neuter_holes.txt"
PACKAGE_ROOT = "Packages/com.velvet.core"

# A parser and its applier are the two halves a class-driven payload passes through, and both are named
# by convention, which is what lets the uncovered record be derived rather than listed. What sits outside
# these two globs is required by nothing to have a cut: a manipulator, a build step and FiberNodePatcher
# are all cut today and would each be recorded by no glob if the cut were dropped.
MECHANISM_GLOBS = (
    ("Runtime/Styling", "Style*Class.cs"),
    ("Runtime/Reconciler", "Fiber*Applier.cs"),
)

# Every comparison the audit makes is a set difference, and each one agrees with an empty reading: a
# moved directory globs nothing, a renamed JSON key parses to no cut, and neither disagrees with any
# record. So the floors are what separate an audit that read the repository from one that read nothing
# and exited 0. They are floors rather than exact counts, so a cut added tomorrow needs no edit here.
MECHANISM_FLOOR = 25
CUT_FLOOR = 25
FIXTURE_FLOOR = 15

# Failed is the one result no hole may carry: report_pair keeps a case only when the cut did not turn it
# red, so a line saying otherwise came from something other than a sweep.
HOLE_RESULTS = ("Passed", "Inconclusive", "Skipped")


def unity_processes():
    result = subprocess.run(["ps", "-Ao", "command="], capture_output=True, text=True)
    return sum(1 for line in result.stdout.splitlines() if re.match(UNITY_RUNNING, line))


def wait_for_quiet(seconds):
    """Waits before the cut is applied, never after.

    A wait that happens with a neutered file already in the tree leaves the working copy mutated for
    as long as the wait lasts, and anything that commits or builds meanwhile picks the cut up.
    """
    deadline = time.time() + seconds
    announced = False
    while unity_processes():
        if time.time() > deadline:
            return False
        if not announced:
            print("  another Unity run is in flight; waiting for the machine", flush=True)
            announced = True
        time.sleep(5)
    return True


def load_cuts(project):
    """The map, keyed for lookup. It is stored as arrays because the guard that pins these anchors in
    CI reads it through Unity's JsonUtility, which cannot deserialise an object keyed by name."""
    path = project / CUTS_FILE
    if not path.exists():
        sys.exit(f"error: {CUTS_FILE} not found under {project}")
    raw = json.loads(path.read_text())
    return {
        "cuts": {cut["name"]: cut for cut in raw["cuts"]},
        "fixtures": {entry["fixture"]: entry for entry in raw["fixtures"]},
    }


def in_scope(entry, cut, name):
    """Whether a case is one this cut can say anything about.

    A fixture holding cases for two mechanisms — a ring case and a clip case in one file — hands back
    every ring case as still passing when the clip applier is cut, which is true and means nothing. So
    a fixture whose cuts span more than one mechanism declares which of its cases belong to which, and
    a fixture whose cuts are all one mechanism declares nothing and takes all of them.
    """
    scopes = entry.get("caseScopes") or []
    if not scopes:
        return True
    patterns = [scope["pattern"] for scope in scopes if scope["mechanism"] == cut["mechanism"]]
    return any(re.search(pattern, name) for pattern in patterns)


def locate(project, edit):
    """The line index of the anchor and of the brace its neuter goes after.

    An anchor matching twice is as broken as one matching zero times: the harness would neuter one of
    the two and report the other's tests as holes.
    """
    path = project / edit["file"]
    if not path.exists():
        return None, f"{edit['file']} does not exist"
    lines = path.read_text().splitlines()
    hits = [i for i, line in enumerate(lines) if line.strip() == edit["anchor"]]
    if len(hits) != 1:
        return None, f"{edit['file']}: anchor matched {len(hits)} times, expected 1: {edit['anchor']}"
    brace = next((i for i in range(hits[0] + 1, len(lines)) if lines[i].strip()), None)
    if brace is None or lines[brace].strip() != "{":
        return None, f"{edit['file']}: the line after the anchor is not a body brace"
    return brace, None


def git_porcelain(project):
    result = subprocess.run(["git", "-C", str(project), "status", "--porcelain"],
                            capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or "git status failed")
    entries = {}
    for line in result.stdout.splitlines():
        if not line.strip():
            continue
        status = line[:2]
        path = line[3:]
        if " -> " in path:
            path = path.split(" -> ", 1)[1]
        entries[path] = status
    return entries


def path_in_harness_output(project, path, output_dir):
    try:
        output_dir.resolve().relative_to(project.resolve())
    except ValueError:
        return False
    resolved = (project / path).resolve()
    output_resolved = output_dir.resolve()
    try:
        resolved.relative_to(output_resolved)
        return True
    except ValueError:
        pass
    try:
        output_resolved.relative_to(resolved)
        return True
    except ValueError:
        return False


def restore_foreign_dirty(project, cut, before_dirty, output_dir):
    """A cut's revert() only puts back files listed in the cut map.

    The cut runs first, so the fixture exercises disabled code and its teardown cannot undo what that
    run wrote. Cutting the revert of BundledShaderBuildInclusion and BundledStyleSheetBuildInclusion left
    ProjectSettings/GraphicsSettings.asset and ProjectSettings/ProjectSettings.asset modified after the
    sweep finished.
    """
    try:
        after_dirty = git_porcelain(project)
    except RuntimeError as exc:
        print(f"    failed to read git status: {exc}", flush=True)
        return
    cut_files = {edit["file"] for edit in cut["edits"]}
    foreign = sorted(
        path for path in set(after_dirty) - set(before_dirty) - cut_files
        if not path_in_harness_output(project, path, output_dir)
    )
    for path in foreign:
        if after_dirty[path] == "??":
            command = ["git", "-C", str(project), "clean", "-fd", "--", path]
        else:
            command = ["git", "-C", str(project), "restore", "--source=HEAD",
                         "--staged", "--worktree", "--", path]
        result = subprocess.run(command, capture_output=True, text=True)
        if result.returncode != 0:
            detail = (result.stderr or result.stdout).strip()
            print(f"    failed to restore {path}: {detail}", flush=True)
        else:
            print(f"    restored {path}", flush=True)


def dirty_cut_files(project, cuts):
    """Cut files git already reports as modified.

    The revert runs in a `finally`, which a killed process does not reach — one interrupted sweep left a
    neutered parser in the tree. Starting on top of that measures a mutation nobody declared and reports
    its survivors as holes.
    """
    files = sorted({edit["file"] for cut in cuts["cuts"].values() for edit in cut["edits"]})
    result = subprocess.run(["git", "-C", str(project), "status", "--porcelain", "--"] + files,
                            capture_output=True, text=True)
    return [line[3:] for line in result.stdout.splitlines() if line.strip()]


def validate(project, cuts):
    problems = []
    for name, cut in cuts["cuts"].items():
        for edit in cut["edits"]:
            _, problem = locate(project, edit)
            if problem:
                problems.append(f"{name}: {problem}")
    for fixture, entry in cuts["fixtures"].items():
        for name in entry["cuts"]:
            if name not in cuts["cuts"]:
                problems.append(f"{fixture} names an undeclared cut '{name}'")
    return problems


def read_record(path):
    """The non-blank lines of a checked-in record, stripped."""
    return [line.strip() for line in path.read_text().splitlines() if line.strip()]


def mechanisms(project):
    """Every mechanism the uncovered record answers for, as its path under the package."""
    found = []
    for folder, pattern in MECHANISM_GLOBS:
        found += [f"{folder}/{path.name}" for path in (project / PACKAGE_ROOT / folder).glob(pattern)]
    return sorted(found)


def cut_files(cuts):
    """The mechanisms some cut disables, in the same spelling mechanisms() returns."""
    prefix = PACKAGE_ROOT + "/"
    return {edit["file"][len(prefix):] if edit["file"].startswith(prefix) else edit["file"]
            for cut in cuts["cuts"].values() for edit in cut["edits"]}


def coverage_drift(found, cut, recorded):
    """Both directions the uncovered record can disagree with the sources in.

    An arrival is a mechanism nothing can disable, which is the one this exists for: a class-driven
    mechanism fails by being ignored, and an ignored utility class reads exactly like a class nobody
    wrote. A departure is a cut somebody has since written, and a record keeping the line stops meaning
    what it says.
    """
    uncovered = {path for path in found if path not in cut}
    return ([f"{UNCOVERED_FILE}: {path} has no cut and is not recorded — write one in {CUTS_FILE}, "
             "or record it as uncovered" for path in sorted(uncovered - recorded)]
            + [f"{UNCOVERED_FILE}: {path} is recorded as uncovered and a cut disables it — drop the line"
               for path in sorted(recorded - uncovered)])


def coverage_problems(project, cuts):
    path = project / UNCOVERED_FILE
    if not path.is_file():
        return [f"{UNCOVERED_FILE} not found; nothing answers for a mechanism with no cut"]
    found = mechanisms(project)
    recorded = set(read_record(path))
    problems = []
    if len(found) < MECHANISM_FLOOR:
        problems.append(f"the mechanism glob found {len(found)} files under {PACKAGE_ROOT}, "
                        f"fewer than {MECHANISM_FLOOR}")
    problems += coverage_drift(found, cut_files(cuts), recorded)
    problems += [f"{UNCOVERED_FILE}: {entry} names no file under {PACKAGE_ROOT}"
                 for entry in sorted(recorded) if not (project / PACKAGE_ROOT / entry).is_file()]
    return problems


def declaring_sources(project, short_name):
    """The test sources declaring a fixture by that name.

    Sought as a class declaration rather than as a file name: one file may declare several fixtures, and
    the file sharing its name with one of them is not the file the others live in.
    """
    pattern = re.compile(rf"\bclass\s+{re.escape(short_name)}\b")
    return [path for path in sorted(project.glob("Packages/**/Tests/**/*.cs"))
            if pattern.search(path.read_text())]


def declared_cases(project, fixture):
    """The case names a fixture's source declares."""
    found = set()
    for path in declaring_sources(project, fixture.rsplit(".", 1)[-1]):
        found |= set(re.findall(r"public\s+(?:void|IEnumerator)\s+(Given_\w+)", path.read_text()))
    return found


def hole_problems(lines, cuts, cases):
    """Why a recorded hole cannot be compared against a sweep. `cases` maps a fixture to its case names.

    An entry naming a fixture, a cut or a case that no longer exists is matched by no sweep, and
    compare_baseline hands it back as a hole that closed rather than as a line to delete.
    """
    problems = []
    for number, line in lines:
        fields = line.split("\t")
        if len(fields) != 4:
            problems.append(f"{HOLES_FILE}:{number}: {len(fields)} tab-separated fields, expected 4")
            continue
        fixture, name, case, result = fields
        entry = cuts["fixtures"].get(fixture)
        if entry is None:
            problems.append(f"{HOLES_FILE}:{number}: no fixture '{fixture}' in {CUTS_FILE}")
        elif name not in entry["cuts"]:
            problems.append(f"{HOLES_FILE}:{number}: {fixture} is not registered against cut '{name}'")
        elif case not in cases.get(fixture, set()):
            problems.append(f"{HOLES_FILE}:{number}: {fixture} declares no case {case}")
        if result not in HOLE_RESULTS:
            problems.append(f"{HOLES_FILE}:{number}: '{result}' is not a result a hole can carry")
    return problems


def holes_problems(project, cuts):
    path = project / HOLES_FILE
    if not path.is_file():
        return [f"{HOLES_FILE} not found; a sweep has nothing to be read as a diff against"]
    lines = [(number, line) for number, line in enumerate(path.read_text().splitlines(), start=1)
             if line.strip()]
    cases = {fixture: declared_cases(project, fixture) for fixture in cuts["fixtures"]}
    return hole_problems(lines, cuts, cases)


def audit(project, cuts):
    """Everything a sweep rests on that can be decided without an editor."""
    problems = validate(project, cuts)
    if len(cuts["cuts"]) < CUT_FLOOR or len(cuts["fixtures"]) < FIXTURE_FLOOR:
        problems.append(f"the cut map parsed to {len(cuts['cuts'])} cuts and {len(cuts['fixtures'])} "
                        f"fixtures, fewer than {CUT_FLOOR} and {FIXTURE_FLOOR}")
    return problems + coverage_problems(project, cuts) + holes_problems(project, cuts)


def apply_cut(project, cut):
    """Returns the original text of every file touched, for the revert."""
    originals = {}
    for edit in cut["edits"]:
        path = project / edit["file"]
        originals.setdefault(path, path.read_text())
        lines = path.read_text().splitlines(keepends=True)
        brace, problem = locate(project, edit)
        if problem:
            raise RuntimeError(problem)
        indent = " " * (len(lines[brace]) - len(lines[brace].lstrip()) + 4)
        lines.insert(brace + 1, f"{indent}{edit['neuter']}\n")
        path.write_text("".join(lines))
    return originals


def revert(originals):
    for path, text in originals.items():
        path.write_text(text)


def run_suite(unity, project, platform, fixture, results, log, timeout):
    """Returns (wall clock, killed, peak concurrent other runs).

    Concurrency is sampled for the duration rather than once at launch: an editor that starts after
    this run does is exactly the one that would make its failures ambiguous, and a single reading at
    the start cannot see it.
    """
    command = [
        unity, "-runTests", "-batchmode", "-projectPath", str(project),
        "-testPlatform", platform, "-testFilter", fixture,
        "-testResults", str(results), "-logFile", str(log),
    ]
    start = time.time()
    peak = 0
    process = subprocess.Popen(command)
    while process.poll() is None:
        if time.time() - start > timeout:
            process.kill()
            process.wait()
            return time.time() - start, True, peak
        peak = max(peak, max(0, unity_processes() - 1))
        time.sleep(3)
    return time.time() - start, False, peak


def outcomes(results):
    """Per-test result keyed by full name, or None when the run produced no readable XML."""
    if not results.exists():
        return None
    root = ET.parse(str(results)).getroot()
    if root.tag != "test-run":
        return None
    return {
        case.get("fullname") or case.get("name") or "<unnamed>": case.get("result")
        for case in root.iter("test-case")
    }


BASELINE_DRIFT_EXIT = 2
# Holes already force exit 1; a second meaning on 1 would make CI unable to tell drift from an ordinary hole count.


def read_hole_lines(path):
    return [line for line in path.read_text().splitlines() if line.strip()]


def baseline_problem(fixture, baseline):
    """Why this fixture's baseline cannot support a sweep, and which cases say so.

    Both answers read as full coverage in the output, which is why neither may pass. A filter that named
    nothing reports no holes because it ran no cases. A case already red is indistinguishable from one
    the cut killed, and scores as coverage the fixture does not have.
    """
    if not baseline:
        return f"the filter '{fixture}' ran no cases; every cut below would read as covered", []
    red = sorted(test for test, result in baseline.items() if result != "Passed")
    if red:
        return "the baseline is not green; every cut below would read as covered", red
    return None, []


def baseline_arg_problems(args):
    """Arguments that would make --baseline meaningless, checked before the first editor run."""
    problems = []
    if not args.baseline:
        return problems
    baseline = Path(args.baseline).resolve()
    if not baseline.is_file():
        problems.append(f"baseline file not found: {baseline}")
    if args.report and Path(args.report).resolve() == baseline:
        problems.append(
            "--report and --baseline name the same file; --report writes the sweep holes before "
            "--baseline compares them, so the comparison can only pass")
    return problems


def compare_baseline(sweep_lines, baseline_path):
    """Returns an exit code when the baseline drifts or is absent; None when it matches."""
    path = Path(baseline_path).resolve()
    if not path.is_file():
        print(f"error: baseline file not found: {path}", file=sys.stderr)
        return BASELINE_DRIFT_EXIT
    baseline_lines = read_hole_lines(path)
    sweep_set = set(sweep_lines)
    baseline_set = set(baseline_lines)
    only_sweep = sorted(sweep_set - baseline_set)
    only_baseline = sorted(baseline_set - sweep_set)
    if not only_sweep and not only_baseline:
        return None
    print(f"\nBaseline comparison against {path}:", flush=True)
    if only_sweep:
        print("\n  Only in this sweep — passes with its mechanism disabled though it did not before:",
              flush=True)
        for line in only_sweep:
            print(f"    {line}", flush=True)
    if only_baseline:
        print("\n  Only in baseline — started failing its cut, or the case or cut left the map;"
              " confirm which before treating the baseline as stale:", flush=True)
        for line in only_baseline:
            print(f"    {line}", flush=True)
    print("\n  Regenerate the baseline after an intended change:", flush=True)
    print(f"    {sys.executable} scripts/test_quality/neuter_check.py --report {path}", flush=True)
    return BASELINE_DRIFT_EXIT


def report_pair(entry, name, cut, baseline, cut_results, elapsed, peak):
    print(f"\n  cut '{name}' — {cut['summary']}", flush=True)
    if cut_results is None:
        print("    NO RESULTS — the run produced no readable XML; the cut may not compile", flush=True)
        return None
    scoped = [test for test in cut_results if in_scope(entry, cut, test.rsplit(".", 1)[-1])]
    # Only Failed is evidence that a case noticed the cut. A case that went Inconclusive, Skipped or
    # Ignored asked nothing under it, and scoring that as killed is the direction that hides holes —
    # the same reason the baseline must be green before any cut is applied.
    holes = sorted(
        test for test in scoped
        if cut_results[test] != "Failed" and baseline.get(test) == "Passed"
    )
    inconclusive = {test: cut_results[test] for test in holes if cut_results[test] != "Passed"}
    missing = sorted(set(baseline) - set(cut_results))
    print(f"    {len(scoped)} of {len(cut_results)} cases in scope, {len(holes)} not failing, "
          f"{elapsed:.0f}s, peak other runs {peak}", flush=True)
    for test in holes:
        mark = f" ({inconclusive[test]})" if test in inconclusive else ""
        print(f"      HOLE{mark} {test.rsplit('.', 1)[-1]}", flush=True)
    if missing:
        # A case the baseline ran and this one did not is not a case the cut killed — it is a case the
        # run never reached, and a cut that stops the assembly loading produces a whole fixture of them.
        # Reported as a hole it would read as coverage; dropped from the report it reads as one too, so
        # it stops the sweep the way a red baseline does.
        print(f"error: the '{name}' run reached {len(missing)} fewer cases than the baseline; "
              "every one of them would read as covered", file=sys.stderr)
        for test in missing:
            print(f"  {test}", file=sys.stderr)
        return None
    return [(test, cut_results[test]) for test in holes]


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="Unity project root (default: cwd)")
    parser.add_argument("--fixtures", nargs="*",
                        help="fixtures to sweep; default is every one in the cut map")
    parser.add_argument("--platform", default="", choices=["", "EditMode", "PlayMode"],
                        help="override the platform every fixture declares; default is to use each one's")
    parser.add_argument("--validate", action="store_true",
                        help="check every anchor still matches exactly once, then exit")
    parser.add_argument("--audit", action="store_true",
                        help="check the anchors, the uncovered record and the hole baseline against the "
                             "sources, then exit; no editor needed")
    parser.add_argument("--timeout", type=int, default=900, help="seconds per run (default: 900)")
    parser.add_argument("--busy-timeout", type=int, default=1800,
                        help="seconds to wait for a quiet machine (default: 1800)")
    parser.add_argument("--output", default="", help="directory for the per-run logs and XML")
    parser.add_argument("--unity", default=DEFAULT_UNITY, help="editor binary")
    parser.add_argument("--report", metavar="FILE",
                        help="write every hole as one stable line (fixture, cut, case), sorted")
    parser.add_argument("--baseline", metavar="FILE",
                        help="compare holes from this sweep against an approved baseline file")
    args = parser.parse_args()

    project = Path(args.project).resolve()
    cuts = load_cuts(project)

    if args.audit:
        problems = audit(project, cuts)
        for problem in problems:
            print(f"error: {problem}", file=sys.stderr)
        if problems:
            return 1
        print(f"{len(mechanisms(project))} mechanisms, {len(cuts['cuts'])} cuts across "
              f"{len(cuts['fixtures'])} fixtures, "
              f"{len(read_record(project / UNCOVERED_FILE))} recorded uncovered, "
              f"{len(read_record(project / HOLES_FILE))} recorded holes", flush=True)
        return 0

    problems = validate(project, cuts)
    if problems:
        for problem in problems:
            print(f"error: {problem}", file=sys.stderr)
        return 1
    if args.validate:
        edits = sum(len(cut["edits"]) for cut in cuts["cuts"].values())
        print(f"{edits} anchors across {len(cuts['cuts'])} cuts each match exactly once", flush=True)
        return 0

    dirty = dirty_cut_files(project, cuts)
    if dirty:
        print("error: a cut file is already modified; revert it before sweeping", file=sys.stderr)
        for path in dirty:
            print(f"  {path}", file=sys.stderr)
        return 1

    for problem in baseline_arg_problems(args):
        print(f"error: {problem}", file=sys.stderr)
        return BASELINE_DRIFT_EXIT

    out = Path(args.output).resolve() if args.output else project / "Logs" / "neuter_check"
    out.mkdir(parents=True, exist_ok=True)

    fixtures = args.fixtures or sorted(cuts["fixtures"])
    unknown = [fixture for fixture in fixtures if fixture not in cuts["fixtures"]]
    if unknown:
        print(f"error: no cut map for {', '.join(unknown)}", file=sys.stderr)
        return 1

    total_holes = 0
    report_lines = []
    for fixture in fixtures:
        short = fixture.rsplit(".", 1)[-1]
        # The platform is a property of where the fixture lives, not of how the sweep was invoked: a
        # PlayMode fixture asked under EditMode selects no case, and before that was refused it reported
        # zero holes for every cut it was registered against.
        platform = args.platform or cuts["fixtures"][fixture].get("platform", "EditMode")
        print(f"\n{fixture} ({platform})", flush=True)
        if not wait_for_quiet(args.busy_timeout):
            print("error: the machine did not go quiet", file=sys.stderr)
            return 1
        elapsed, killed, peak = run_suite(
            args.unity, project, platform, fixture,
            out / f"{short}-baseline.xml", out / f"{short}-baseline.log", args.timeout)
        baseline = outcomes(out / f"{short}-baseline.xml")
        if killed or baseline is None:
            print("error: the baseline run produced no results", file=sys.stderr)
            return 1
        problem, red = baseline_problem(fixture, baseline)
        if problem:
            print(f"error: {problem}", file=sys.stderr)
            for test in red:
                print(f"  {test}", file=sys.stderr)
            return 1
        print(f"  baseline {len(baseline)} passed, {elapsed:.0f}s, peak other runs {peak}", flush=True)

        entry = cuts["fixtures"][fixture]
        for name in entry["cuts"]:
            cut = cuts["cuts"][name]
            if not wait_for_quiet(args.busy_timeout):
                print("error: the machine did not go quiet", file=sys.stderr)
                return 1
            before_dirty = git_porcelain(project)
            originals = apply_cut(project, cut)
            try:
                elapsed, killed, peak = run_suite(
                    args.unity, project, platform, fixture,
                    out / f"{short}-{name}.xml", out / f"{short}-{name}.log", args.timeout)
            finally:
                revert(originals)
                restore_foreign_dirty(project, cut, before_dirty, out)
            holes = report_pair(entry, name, cut, baseline,
                                None if killed else outcomes(out / f"{short}-{name}.xml"),
                                elapsed, peak)
            if holes is None:
                return 1
            total_holes += len(holes)
            for test, result in holes:
                # The result rides in the line because Inconclusive and Passed are different answers
                # filed under one word: an Inconclusive case stopped at an Assume the cut falsified, so
                # it DID notice, while a Passed one did not. Recorded, deleting that Assume and letting
                # the assertion pass on a default value drifts the baseline; without it the two emit the
                # same bytes and the set comparison sees nothing.
                report_lines.append(f"{fixture}\t{name}\t{test.rsplit('.', 1)[-1]}\t{result}")

    if args.report:
        Path(args.report).write_text(
            "\n".join(sorted(report_lines)) + ("\n" if report_lines else ""))

    if args.baseline:
        drift = compare_baseline(report_lines, args.baseline)
        if drift is not None:
            print(f"\n{total_holes} hole(s) across {len(fixtures)} fixture(s)", flush=True)
            return drift

    print(f"\n{total_holes} hole(s) across {len(fixtures)} fixture(s)", flush=True)
    return 1 if total_holes else 0


if __name__ == "__main__":
    sys.exit(main())
