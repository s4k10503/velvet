#!/usr/bin/env python3
"""Fail when repeated six-line blocks in the package rise above a checked-in baseline.

A block count ceiling would fail today on deliberate sibling pairs kept parallel on purpose. The
ratchet only asks whether new duplication was added: the total count may stay high, but it must not
grow without an intentional baseline update.
"""

import argparse
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


def count_repeated_blocks(package_root: Path) -> int:
    blocks: dict[str, set[str]] = defaultdict(set)
    for path in package_files(package_root):
        relative = str(path.relative_to(package_root))
        lines = non_comment_lines(path.read_text())
        for index in range(len(lines) - BLOCK_LINES + 1):
            chunk = lines[index:index + BLOCK_LINES]
            if len(set(chunk)) < MIN_DISTINCT_LINES:
                continue
            blocks["\n".join(chunk)].add(relative)
    return sum(1 for locations in blocks.values() if len(locations) > 1)


def read_baseline(path: Path) -> int:
    text = path.read_text().strip()
    if not text:
        raise ValueError("baseline file is empty")
    return int(text.splitlines()[0].strip())


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

    count = count_repeated_blocks(package_root)
    file_count = len(package_files(package_root))
    print(f"files: {file_count}")
    print(f"repeated blocks: {count}")

    if args.write_baseline:
        target = Path(args.write_baseline).resolve()
        target.write_text(f"{count}\n")
        print(f"baseline written to {target}")
        return 0

    baseline_path = (project / args.baseline).resolve()
    if not baseline_path.is_file():
        print(f"error: baseline file not found: {baseline_path}", file=sys.stderr)
        print(f"  {sys.executable} scripts/test_quality/duplication_check.py "
              f"--write-baseline {args.baseline}", file=sys.stderr)
        return BASELINE_DRIFT_EXIT

    baseline = read_baseline(baseline_path)
    if count > baseline:
        print(f"\nRepeated-block count rose from {baseline} to {count}.", file=sys.stderr)
        print("Update the baseline only when duplication was added deliberately:", file=sys.stderr)
        print(f"  {sys.executable} scripts/test_quality/duplication_check.py "
              f"--write-baseline {args.baseline}", file=sys.stderr)
        return 1

    if count < baseline:
        print(f"\nRepeated-block count fell from {baseline} to {count}.", file=sys.stderr)
        print("Ratchet the baseline down so the next deliberate addition is visible:", file=sys.stderr)
        print(f"  {sys.executable} scripts/test_quality/duplication_check.py "
              f"--write-baseline {args.baseline}", file=sys.stderr)
        return BASELINE_DRIFT_EXIT

    return 0


if __name__ == "__main__":
    sys.exit(main())
