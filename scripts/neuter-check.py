#!/usr/bin/env python3
"""Report every test that still passes with the mechanism it is named for disabled.

Mutating a line asks whether any test depends on that line. Disabling a named mechanism at a named
layer asks the question a fixture's name makes: with clip-path resolving nothing, does a test called
"the element is wrapped" still pass? A test that does was never measuring the thing it claims.

Which layer the cut is made at decides the answer, and not by a little. Every vacuous test found so
far has been green at the applier cut and red at the parser cut, because a gate read one layer up
survives a neuter one layer down.

So scope is declared rather than assumed, in neuter-cuts.json, at two granularities — and both are
needed, which the first run of this harness established by getting the second one wrong. A fixture is
asked only the cuts it reaches, or a parser-only fixture reports its whole body as holes under an
applier cut. And a fixture holding cases for two mechanisms declares which of its cases belong to
which, or every ring case in a clip fixture reports as a hole when the clip applier dies: true, and
about nothing.

This does not replace mutation-check.py. That one mutates syntax across a diff and asks whether any
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
# Anchored at the editor binary so that a wait on this pattern does not match itself and report a
# busy machine forever on an idle one.
UNITY_RUNNING = "^/Applications/.*/MacOS/Unity -runTests"

CUTS_FILE = "scripts/neuter-cuts.json"


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


def report_pair(entry, name, cut, baseline, cut_results, elapsed, peak):
    print(f"\n  cut '{name}' — {cut['summary']}")
    if cut_results is None:
        print("    NO RESULTS — the run produced no readable XML; the cut may not compile")
        return None
    scoped = [test for test in cut_results if in_scope(entry, cut, test.rsplit(".", 1)[-1])]
    holes = sorted(
        test for test in scoped
        if cut_results[test] == "Passed" and baseline.get(test) == "Passed"
    )
    missing = sorted(set(baseline) - set(cut_results))
    print(f"    {len(scoped)} of {len(cut_results)} cases in scope, {len(holes)} still passing, "
          f"{elapsed:.0f}s, peak other runs {peak}")
    for test in holes:
        print(f"      HOLE {test.rsplit('.', 1)[-1]}")
    for test in missing:
        print(f"      NOT RUN {test.rsplit('.', 1)[-1]}")
    return holes


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="Unity project root (default: cwd)")
    parser.add_argument("--fixtures", nargs="*",
                        help="fixtures to sweep; default is every one in the cut map")
    parser.add_argument("--platform", default="EditMode", choices=["EditMode", "PlayMode"])
    parser.add_argument("--validate", action="store_true",
                        help="check every anchor still matches exactly once, then exit")
    parser.add_argument("--timeout", type=int, default=900, help="seconds per run (default: 900)")
    parser.add_argument("--busy-timeout", type=int, default=1800,
                        help="seconds to wait for a quiet machine (default: 1800)")
    parser.add_argument("--output", default="", help="directory for the per-run logs and XML")
    parser.add_argument("--unity", default=DEFAULT_UNITY, help="editor binary")
    args = parser.parse_args()

    project = Path(args.project).resolve()
    cuts = load_cuts(project)

    problems = validate(project, cuts)
    if problems:
        for problem in problems:
            print(f"error: {problem}", file=sys.stderr)
        return 1
    if args.validate:
        edits = sum(len(cut["edits"]) for cut in cuts["cuts"].values())
        print(f"{edits} anchors across {len(cuts['cuts'])} cuts each match exactly once")
        return 0

    dirty = dirty_cut_files(project, cuts)
    if dirty:
        print("error: a cut file is already modified; revert it before sweeping", file=sys.stderr)
        for path in dirty:
            print(f"  {path}", file=sys.stderr)
        return 1

    out = Path(args.output).resolve() if args.output else project / "Logs" / "neuter-check"
    out.mkdir(parents=True, exist_ok=True)

    fixtures = args.fixtures or sorted(cuts["fixtures"])
    unknown = [fixture for fixture in fixtures if fixture not in cuts["fixtures"]]
    if unknown:
        print(f"error: no cut map for {', '.join(unknown)}", file=sys.stderr)
        return 1

    total_holes = 0
    for fixture in fixtures:
        short = fixture.rsplit(".", 1)[-1]
        print(f"\n{fixture}")
        if not wait_for_quiet(args.busy_timeout):
            print("error: the machine did not go quiet", file=sys.stderr)
            return 1
        elapsed, killed, peak = run_suite(
            args.unity, project, args.platform, fixture,
            out / f"{short}-baseline.xml", out / f"{short}-baseline.log", args.timeout)
        baseline = outcomes(out / f"{short}-baseline.xml")
        if killed or baseline is None:
            print("error: the baseline run produced no results", file=sys.stderr)
            return 1
        red = sorted(test for test, result in baseline.items() if result != "Passed")
        if red:
            # A test failing on its own accord is indistinguishable from one the cut killed, and it
            # scores as coverage the fixture does not have. That direction hides holes, so it stops
            # the sweep rather than being noted in the output.
            print("error: the baseline is not green; every cut below would read as covered",
                  file=sys.stderr)
            for test in red:
                print(f"  {test}", file=sys.stderr)
            return 1
        print(f"  baseline {len(baseline)} passed, {elapsed:.0f}s, peak other runs {peak}")

        entry = cuts["fixtures"][fixture]
        for name in entry["cuts"]:
            cut = cuts["cuts"][name]
            if not wait_for_quiet(args.busy_timeout):
                print("error: the machine did not go quiet", file=sys.stderr)
                return 1
            originals = apply_cut(project, cut)
            try:
                elapsed, killed, peak = run_suite(
                    args.unity, project, args.platform, fixture,
                    out / f"{short}-{name}.xml", out / f"{short}-{name}.log", args.timeout)
            finally:
                revert(originals)
            holes = report_pair(entry, name, cut, baseline,
                                None if killed else outcomes(out / f"{short}-{name}.xml"),
                                elapsed, peak)
            if holes is None:
                return 1
            total_holes += len(holes)

    print(f"\n{total_holes} hole(s) across {len(fixtures)} fixture(s)")
    return 1 if total_holes else 0


if __name__ == "__main__":
    sys.exit(main())
