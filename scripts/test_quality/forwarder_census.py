#!/usr/bin/env python3
"""List the forwarding members of the package's mutable sources, and the cut each one would take.

A forwarding member hands its arguments to another member and does nothing else, and a campaign
writes no mutant inside one, so whether a test notices it forwarding to nothing is asked by no
campaign; a cut asks it. This is the inventory a sweep of those cuts starts from: each row names a
member, its lines, whether a generic and a non-generic type of one name both declare it, and the cut
that replaces what it forwards. What a member is, and what each cut is, is
`mutation_check.forwarding_members`' reading, so the inventory and a campaign's reach agree on it.

It reads the tree, starts no editor and writes no file, and the commit it read is printed first,
since the members move with the tree.

    python3 scripts/test_quality/forwarder_census.py
    python3 scripts/test_quality/forwarder_census.py --paired
    python3 scripts/test_quality/forwarder_census.py Packages/com.velvet.core/Runtime/Async/VelvetTaskSources.cs
"""

import argparse
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import mutation_check  # noqa: E402


def sources(project, names):
    if names:
        return [path for path in (Path(name).resolve() for name in names)
                if mutation_check.mutable(path, project)]
    return [path for path in sorted((project / mutation_check.PACKAGE).rglob("*.cs"))
            if mutation_check.mutable(path, project)]


def census(project, paths):
    """(path, forwarder, mutants on its lines, mutants inside its forwarded body) per member."""
    rows = []
    for path in paths:
        text = path.read_text()
        for forwarder in mutation_check.forwarding_members(text):
            lines = set(range(forwarder.first, forwarder.last + 1))
            mutants = mutation_check.mutations_for(path, text, lines)
            inside = mutation_check.inside_forwarded_bodies(text, [forwarder], mutants)
            rows.append((path, forwarder, len(mutants), len(inside)))
    return rows


def render(project, rows, paired_only):
    paired = mutation_check.paired_forwards([forwarder for _, forwarder, _, _ in rows])
    lines = []
    for path, forwarder, on_lines, inside in rows:
        pair = (forwarder.type_name, forwarder.member) in paired
        if paired_only and not pair:
            continue
        cut = forwarder.cut if forwarder.cut else "none ({})".format(forwarder.declined)
        lines.append("\t".join((
            "{}:{}-{}".format(mutation_check.relative_to(path, project).as_posix(), forwarder.first,
                              forwarder.last),
            forwarder.name, "accessor" if forwarder.accessor else "member",
            "paired" if pair else "-", cut, str(on_lines), str(inside))))
    shown = [row for row in rows if not paired_only
             or (row[1].type_name, row[1].member) in paired]
    members = [row for row in shown if not row[1].accessor]
    lines.append("{} member(s) and {} accessor(s) in {} file(s); {} with a mutant on their lines, "
                 "{} mutant(s) inside a forwarded body; {} in a generic/non-generic pair".format(
                     len(members), len(shown) - len(members), len({row[0] for row in shown}),
                     sum(1 for row in shown if row[2]), sum(row[3] for row in shown),
                     sum(1 for row in shown if (row[1].type_name, row[1].member) in paired)))
    return lines


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="Unity project root (default: cwd)")
    parser.add_argument("--paired", action="store_true",
                        help="only the members a generic and a non-generic type of one name both declare")
    parser.add_argument("files", nargs="*", help="sources to read; default is every mutable one")
    args = parser.parse_args(argv)
    project = Path(args.project).resolve()
    head = subprocess.run(["git", "-C", str(project), "rev-parse", "HEAD"],
                          capture_output=True, text=True).stdout.strip() or "(no commit)"
    dirty = subprocess.run(["git", "-C", str(project), "status", "--porcelain", "--",
                            mutation_check.PACKAGE], capture_output=True, text=True).stdout.strip()
    print("# {}{}".format(head, " with uncommitted changes to the package" if dirty else ""))
    print("# path:lines\tmember\tkind\tpair\tcut\tmutants on its lines\tmutants inside its body")
    for line in render(project, census(project, sources(project, args.files)), args.paired):
        print(line)
    return 0


if __name__ == "__main__":
    sys.exit(main())
