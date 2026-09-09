#!/usr/bin/env python3
"""Report unreapable processes from `ps ax -o stat=,rss=,comm=` on stdin, above a memory gate in MB.

Separated from its caller so the classification can be fed a process table rather than only the
machine's own. WedgedProcessFilterTests pins which STAT values count: E is looked for anywhere among
the flags, because anchoring it to the second character dropped every wedged process that carried
another flag first.

Exits 1 when there is nothing to report, which is how the caller stays silent.
"""

import argparse
import sys
from collections import Counter


def wedged(line):
    """A run state of uninterruptible wait, carrying the exiting flag anywhere among the others."""
    return line.startswith("U") and "E" in line


def summarize(rows):
    """Return (count, kilobytes, per-command counts) for the wedged rows of a `ps` table."""
    count = 0
    kilobytes = 0
    held = Counter()
    for row in rows:
        fields = row.split()
        if len(fields) < 3 or not wedged(fields[0]):
            continue
        count += 1
        kilobytes += int(fields[1]) if fields[1].isdigit() else 0
        held[" ".join(fields[2:]).rsplit("/", 1)[-1]] += 1
    return count, kilobytes, held


def report(count, kilobytes, held):
    megabytes = kilobytes / 1024
    lines = [
        f"{count} processes cannot be reaped, holding {megabytes:.0f} MB. "
        "Only a reboot clears them.",
        "",
    ]
    for name, times in sorted(held.items(), key=lambda item: (-item[1], item[0])):
        lines.append(f"  {times:4d}  {name}")
    lines += [
        "",
        "Each is in uninterruptible wait while exiting, where no signal reaches it. A run",
        "that is only slow is a different thing; CONTRIBUTING.md separates the two.",
    ]
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--limit", type=float, default=0, help="Memory gate in MB")
    args = parser.parse_args(argv)

    count, kilobytes, held = summarize(sys.stdin.read().splitlines())
    if count == 0 or kilobytes / 1024 < args.limit:
        return 1

    print(report(count, kilobytes, held))
    return 0


if __name__ == "__main__":
    sys.exit(main())
