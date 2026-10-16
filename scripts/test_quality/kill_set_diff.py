#!/usr/bin/env python3
"""Fail when a mutant the base tree's tests killed survives on the head tree.

A pull request that only deletes tests places no mutant of its own, so the mutation campaign has
nothing to measure for it. What it claims instead is that the cases it removed were redundant: every
mutant they killed is killed by what remains. That is a statement about two trees over the same
production source, and this reads it off two `mutation_check.py --run-out --output` directories, one
measured on each tree over the same `--files`.

    python3 scripts/test_quality/kill_set_diff.py BASE_DIR HEAD_DIR

Mutants are matched by what they are -- file, line, column and the rewrite -- and never by index,
because an index is only a mutant's while the list is the same list. The two lists must be the same:
a mutant on one side only was generated from different production source, and a comparison over the
rest says nothing about it. A verdict the head did not take, or took without killing, is a lost kill
as a survivor is: a kill that was not measured is not one that stands.

Exit status: 0 where no kill was lost, 1 where one was, 2 where the comparison itself cannot be made
(a list that differs, a record that cannot be read, or a base that killed nothing and so proves
nothing).
"""

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import mutation_check  # noqa: E402

LOST = 1
UNCOMPARABLE = 2

# What each kind of base kill needs of the head: a mutant a case killed has to be killed by a case, and
# one the suite hung or crashed on is held by any of the three stopped readings.
STOPPED = (mutation_check.KILLED, mutation_check.HUNG, mutation_check.CRASHED)
ACCEPTED = {
    mutation_check.KILLED: (mutation_check.KILLED,),
    mutation_check.HUNG: STOPPED,
    mutation_check.CRASHED: STOPPED,
}

KILLERS_LISTED = 5


def read_records(directory):
    """{(mutant, column): record} for every verdict record in `directory`, and the problems met reading.

    Only `mutant-NNN.json`, the name `mutation_check.verdict_path` writes: a directory also holds the
    run's logs and results beside them. A key two records share is a problem, since which of them
    stands cannot be told.
    """
    records, problems = {}, []
    for path in sorted(Path(directory).glob("mutant-*.json")):
        try:
            record = json.loads(path.read_text())
            key = (record["mutant"], record["column"])
            if "verdict" not in record:
                raise KeyError("verdict")
        except (OSError, ValueError, KeyError, TypeError):
            problems.append("{} is not a verdict record".format(path))
            continue
        if key in records:
            problems.append("{} describes {} at column {}, which another record in {} does already".format(
                path.name, key[0], key[1], directory))
            continue
        records[key] = record
    return records, problems


def describe(key):
    return "{} (column {})".format(*key)


def compare(base, head):
    """(lost, gained, only_base, only_head, base_kills): the keys of each reading, over two record maps."""
    lost, gained = [], []
    base_kills = 0
    for key in sorted(set(base) & set(head)):
        was, now = base[key]["verdict"], head[key]["verdict"]
        if was in ACCEPTED:
            base_kills += 1
            if now not in ACCEPTED[was]:
                lost.append(key)
        elif now in ACCEPTED:
            gained.append(key)
    return lost, gained, sorted(set(base) - set(head)), sorted(set(head) - set(base)), base_kills


def killers_line(record):
    killers = record.get("killers") or []
    shown = ", ".join(name.split(".")[-1] for name in killers[:KILLERS_LISTED])
    more = " and {} more".format(len(killers) - KILLERS_LISTED) if len(killers) > KILLERS_LISTED else ""
    return "{} killer(s){}".format(len(killers), ": " + shown + more if killers else "")


def report(base, head):
    """(exit status, the report's lines)."""
    lines = []
    lost, gained, only_base, only_head, base_kills = compare(base, head)
    if only_base or only_head:
        lines.append("The two trees do not generate the same mutants, so this compares nothing:")
        lines.extend("  only in base: " + describe(key) for key in only_base)
        lines.extend("  only in head: " + describe(key) for key in only_head)
        return UNCOMPARABLE, lines
    if not base_kills:
        lines.append("The base killed none of its {} mutant(s), so nothing here can be lost. Read the "
                     "files and assemblies the run was given.".format(len(base)))
        return UNCOMPARABLE, lines
    for key in lost:
        lines.append("LOST KILL  {}".format(describe(key)))
        lines.append("  base: {} ({})".format(base[key]["verdict"], killers_line(base[key])))
        lines.append("  head: {} ({})".format(head[key]["verdict"], head[key].get("detail") or "-"))
    for key in gained:
        lines.append("extra kill {} (survived on the base; {})".format(describe(key), killers_line(head[key])))
    lines.append("{} mutant(s); killed on the base: {}, lost on the head: {}, killed only on the head: {}".format(
        len(base), base_kills, len(lost), len(gained)))
    return (LOST if lost else 0), lines


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("base", help="the --output directory of the run on the base tree")
    parser.add_argument("head", help="the --output directory of the run on the head tree")
    args = parser.parse_args(argv)
    base, base_problems = read_records(args.base)
    head, head_problems = read_records(args.head)
    problems = base_problems + head_problems
    if problems:
        print("\n".join(problems))
        return UNCOMPARABLE
    status, lines = report(base, head)
    print("\n".join(lines))
    return status


if __name__ == "__main__":
    sys.exit(main())
