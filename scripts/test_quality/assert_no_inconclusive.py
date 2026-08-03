#!/usr/bin/env python3
"""Fail a Unity test run that reported an Inconclusive case.

Nothing else treats Inconclusive as a failure. Unity's runner exits 0, and game-ci's reporter
reads only passed/failed/skipped off the <test-run> element, so a test whose Assume stopped
holding reports green over the behavior it exists to pin.
"""

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

USAGE = "usage: assert_no_inconclusive.py RESULTS_XML_OR_DIRECTORY [...]"


def result_files(arguments):
    files = []
    for argument in arguments:
        path = Path(argument)
        if path.is_dir():
            files.extend(sorted(path.rglob("*.xml")))
        elif path.is_file():
            files.append(path)
        else:
            print("{}: no such file or directory".format(path), file=sys.stderr)
    return files


def inconclusive_in(path):
    """None when the file is not a test run. The root tally decides; the names are for the report."""
    root = ET.parse(str(path)).getroot()
    if root.tag != "test-run":
        return None
    count = int(root.get("inconclusive", "0"))
    names = [
        case.get("fullname") or case.get("name") or "<unnamed>"
        for case in root.iter("test-case")
        if case.get("result") == "Inconclusive"
    ]
    return count, names


def main(arguments):
    if not arguments:
        print(USAGE, file=sys.stderr)
        return 2

    runs = 0
    total = 0
    offenders = []
    for path in result_files(arguments):
        try:
            parsed = inconclusive_in(path)
        except ET.ParseError as error:
            print("{}: unreadable test results ({})".format(path, error), file=sys.stderr)
            return 2
        if parsed is None:
            continue
        runs += 1
        count, names = parsed
        if count:
            total += count
            offenders.append((path, names))

    # A run that wrote no results is the same hole as a silent inconclusive: nothing was verified.
    if runs == 0:
        print("no test run found under: {}".format(" ".join(arguments)), file=sys.stderr)
        return 2

    if not offenders:
        print("checked {} test run(s): no inconclusive cases".format(runs))
        return 0

    print("{} test(s) reported Inconclusive:".format(total), file=sys.stderr)
    for path, names in offenders:
        print("  {}".format(path), file=sys.stderr)
        for name in names:
            print("    {}".format(name), file=sys.stderr)
    print(
        "An Assume that gates the behavior under test belongs inside the assertion, comparing the "
        "gated state and the state under test at once, so a regression fails instead of skipping.",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
