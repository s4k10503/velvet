#!/usr/bin/env python3
"""Fail when the set of repeated six-line blocks in the package differs from a checked-in baseline.

A block count ceiling would fail today on deliberate sibling pairs kept parallel on purpose, so what
is checked is which blocks repeat rather than how many rules are broken. The baseline records the set,
because a count nets out: deleting five duplicated blocks while introducing five passes, and new
duplication in a newly written area — the case this exists to catch — is exactly what a total cannot
see. neuter_holes.txt one directory over is the same shape for the same reason.

An entry is a digest of the block's normalized text beside the files it appears in, so a block that
moves between two files is a departure and an arrival rather than a silent no-op.
"""

import argparse
import hashlib
import re
import sys
from collections import defaultdict
from pathlib import Path

PACKAGE_REL = Path("Packages/com.velvet.core")
DEFAULT_BASELINE = "scripts/test_quality/duplication_baseline.txt"
BLOCK_LINES = 6
MIN_DISTINCT_LINES = 3

BASELINE_DRIFT_EXIT = 2


def package_files(package_root: Path) -> list[Path]:
    files = []
    for path in sorted(package_root.rglob("*.cs")):
        parts = path.parts
        if "Tests" in parts or "Generators~" in parts or path.name.endswith(".g.cs"):
            continue
        files.append(path)
    return files


def normalize(line: str) -> str:
    return re.sub(r"\s+", " ", line.strip())


def non_comment_lines(text: str) -> list[str]:
    lines = []
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("//"):
            continue
        normalized = normalize(line)
        if normalized:
            lines.append(normalized)
    return lines


def repeated_blocks(package_root: Path) -> set[str]:
    """Every block appearing in more than one file, as `<digest>\tfile,file` lines.

    The digest stands in for the block text, which runs to six lines and would make the baseline
    unreadable; the files stand beside it so a reviewer can see what pairs up without rerunning this.
    """
    blocks: dict[str, set[str]] = defaultdict(set)
    for path in package_files(package_root):
        relative = str(path.relative_to(package_root))
        lines = non_comment_lines(path.read_text())
        for index in range(len(lines) - BLOCK_LINES + 1):
            chunk = lines[index:index + BLOCK_LINES]
            if len(set(chunk)) < MIN_DISTINCT_LINES:
                continue
            blocks["\n".join(chunk)].add(relative)

    entries = set()
    for text, locations in blocks.items():
        if len(locations) > 1:
            digest = hashlib.sha1(text.encode("utf-8")).hexdigest()[:12]
            entries.add("{}\t{}".format(digest, ",".join(sorted(locations))))
    return entries


def block_id(entry: str) -> str:
    """The hash an entry opens with, which is the block itself rather than where it repeats."""
    return entry.split(None, 1)[0]


def read_baseline(path: Path) -> set[str]:
    entries = {line.rstrip("\n") for line in path.read_text().splitlines() if line.strip()}
    if not entries:
        raise ValueError("baseline file is empty")
    return entries


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="Unity project root (default: cwd)")
    parser.add_argument("--baseline", metavar="FILE", default=DEFAULT_BASELINE,
                        help=f"compare against this baseline (default: {DEFAULT_BASELINE})")
    parser.add_argument("--write-baseline", metavar="FILE",
                        help="write the current repeated-block count to FILE and exit")
    args = parser.parse_args()

    project = Path(args.project).resolve()
    package_root = project / PACKAGE_REL
    if not package_root.is_dir():
        print(f"error: package not found at {package_root}", file=sys.stderr)
        return 1

    current = repeated_blocks(package_root)
    file_count = len(package_files(package_root))
    print(f"files: {file_count}")
    print(f"repeated blocks: {len(current)}")

    if args.write_baseline:
        target = Path(args.write_baseline).resolve()
        target.write_text("".join(f"{entry}\n" for entry in sorted(current)))
        print(f"baseline written to {target}")
        return 0

    baseline_path = (project / args.baseline).resolve()
    if not baseline_path.is_file():
        print(f"error: baseline file not found: {baseline_path}", file=sys.stderr)
        print(f"  {sys.executable} scripts/test_quality/duplication_check.py "
              f"--write-baseline {args.baseline}", file=sys.stderr)
        return BASELINE_DRIFT_EXIT

    baseline = read_baseline(baseline_path)
    added = sorted(current - baseline)
    removed = sorted(baseline - current)

    # An entry is a block id and the files it repeats in, so a block that gained a file leaves one on
    # each side. Read as added alone it says a block started repeating, which is what a reader acts
    # on -- measured, as a design question raised on a pull request over a block that had repeated in
    # ten files for as long as the baseline has existed.
    started = [entry for entry in added if block_id(entry) not in {block_id(e) for e in removed}]
    stopped = [entry for entry in removed if block_id(entry) not in {block_id(e) for e in added}]
    moved = [entry for entry in added if entry not in started]

    # Both directions are reported before either decides the exit code, because a change that swaps one
    # block for another is the case a count cannot see and is the reason this reads a set.
    for entry in added:
        print(f"  + {entry}", file=sys.stderr)
    for entry in removed:
        print(f"  - {entry}", file=sys.stderr)

    if started:
        print(f"\n{len(started)} block(s) now repeat that did not before.", file=sys.stderr)
    if moved:
        print(f"\n{len(moved)} block(s) already repeated and now repeat in different files. The "
              f"repeated-block count is what it was; what moved is where.", file=sys.stderr)
        print("Update the baseline only when duplication was added deliberately:", file=sys.stderr)
        print(f"  {sys.executable} scripts/test_quality/duplication_check.py "
              f"--write-baseline {args.baseline}", file=sys.stderr)
        return 1

    if removed:
        print(f"\n{len(removed)} block(s) no longer repeat.", file=sys.stderr)
        print("Ratchet the baseline down so the next deliberate addition is visible:", file=sys.stderr)
        print(f"  {sys.executable} scripts/test_quality/duplication_check.py "
              f"--write-baseline {args.baseline}", file=sys.stderr)
        return BASELINE_DRIFT_EXIT

    return 0


if __name__ == "__main__":
    sys.exit(main())
