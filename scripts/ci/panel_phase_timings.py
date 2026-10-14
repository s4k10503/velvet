#!/usr/bin/env python3
"""Totals per phase from the panel fixtures' timing CSV, and the panel share of the EditMode suite.

`PanelTestBase` records, for each case, the milliseconds spent creating and showing its window
(`create_show`), in forced layout/styles passes (`layout`), closing and destroying the window
(`teardown`), and across the whole of its setup and teardown (`case`), into `panel-phase-timings.csv`
beside the run's results XML. `layout` runs inside the case body or its setup, so it is a share of the
time around it and is not added to the others.

The panel share needs no assumption about whether the results XML's per-case duration includes setup
and teardown: panel cases are taken from the `case` rows, the rest of the suite from the XML.

Usage: panel_phase_timings.py RESULTS_DIRECTORY_OR_CSV [--results XML_OR_DIRECTORY]
"""

import argparse
import csv
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path

FILE_NAME = "panel-phase-timings.csv"
HEADER = ["fixture", "case", "phase", "milliseconds"]
PHASES = ["create_show", "layout", "teardown", "case"]


def read_rows(csv_text):
    """(fixture, case, phase, milliseconds) per row; a header line anywhere in the text is skipped."""
    rows = []
    for record in csv.reader(csv_text.splitlines(keepends=True)):
        if not record or record == HEADER:
            continue
        fixture, case, phase, milliseconds = record
        rows.append((fixture, case, phase, float(milliseconds)))
    return rows


def totals_by_phase(rows):
    totals = defaultdict(lambda: [0, 0.0])
    for _, _, phase, milliseconds in rows:
        totals[phase][0] += 1
        totals[phase][1] += milliseconds
    return totals


def panel_cases(rows):
    """The (fixture, case) pairs that recorded a `case` row."""
    return {(fixture, case) for fixture, case, phase, _ in rows if phase == "case"}


def non_panel_milliseconds(xml_text, panel):
    """The results XML's summed duration, in milliseconds, over the cases that are not in `panel`."""
    total = 0.0
    for case in ET.fromstring(xml_text).iter("test-case"):
        if (case.get("classname"), case.get("name")) not in panel:
            total += float(case.get("duration", "0")) * 1000.0
    return total


def overhead_by_fixture(rows):
    """Window creation plus teardown per fixture, largest first."""
    per_fixture = defaultdict(float)
    for fixture, _, phase, milliseconds in rows:
        if phase in ("create_show", "teardown"):
            per_fixture[fixture] += milliseconds
    return sorted(per_fixture.items(), key=lambda item: -item[1])


def report(rows, non_panel_ms=None, top=10):
    totals = totals_by_phase(rows)
    cases = len(panel_cases(rows))
    lines = [f"{cases} panel cases", "", f"{'phase':<12}{'rows':>8}{'total ms':>14}{'mean ms':>12}"]
    for phase in PHASES + sorted(set(totals) - set(PHASES)):
        count, total = totals.get(phase, (0, 0.0))
        mean = total / count if count else 0.0
        lines.append(f"{phase:<12}{count:>8}{total:>14.1f}{mean:>12.3f}")
    window_ms = totals["create_show"][1] + totals["teardown"][1]
    case_ms = totals["case"][1]
    lines += ["", f"window creation + teardown: {window_ms:.1f} ms"]
    if case_ms:
        lines.append(f"  share of the panel cases' setup-to-teardown time: {100 * window_ms / case_ms:.1f}%")
    if non_panel_ms is not None:
        suite_ms = case_ms + non_panel_ms
        if suite_ms:
            lines.append(f"panel cases: {case_ms:.1f} ms of {suite_ms:.1f} ms suite ({100 * case_ms / suite_ms:.1f}%)")
            lines.append(f"window creation + teardown: {100 * window_ms / suite_ms:.1f}% of the suite")
    lines += ["", f"fixtures by window creation + teardown (top {top}):"]
    lines += [f"  {milliseconds:>10.1f} ms  {fixture}" for fixture, milliseconds in overhead_by_fixture(rows)[:top]]
    return "\n".join(lines) + "\n"


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("timings", type=Path, help="the CSV, or the directory holding it")
    parser.add_argument("--results", type=Path, help="the results XML, or a directory of them, for the suite share")
    args = parser.parse_args(argv)

    csv_path = args.timings / FILE_NAME if args.timings.is_dir() else args.timings
    if not csv_path.is_file():
        print(f"{csv_path} does not exist: the run recorded no panel timings", file=sys.stderr)
        return 1
    rows = read_rows(csv_path.read_text(encoding="utf-8"))
    if not rows:
        print(f"{csv_path} holds no rows", file=sys.stderr)
        return 1

    non_panel = None
    if args.results is not None:
        documents = sorted(args.results.glob("*.xml")) if args.results.is_dir() else [args.results]
        panel = panel_cases(rows)
        non_panel = sum(non_panel_milliseconds(path.read_text(encoding="utf-8"), panel) for path in documents)
    sys.stdout.write(report(rows, non_panel))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
