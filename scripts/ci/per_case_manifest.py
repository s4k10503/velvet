#!/usr/bin/env python3
"""Check the `shard.json` each per-case shard wrote against the others, and say which shards failed.

`.github/workflows/per-case-coverage.yml`'s report job runs this over the folder holding every shard's
records, whatever the suite jobs ended with, so that a failed shard is named beside the lines read off
the others rather than costing the whole run's records.

    python3 scripts/ci/per_case_manifest.py DIR [--expect PLATFORM=SHARDS ...]

A suite is in order where its manifests name the same shard count, cover each shard from 1 to that
count once, agree on the length of the roster they were cut from, and report no failed launch. Each
`--expect` names a suite that has to be there, with the shard count it has to have, so that a suite no
manifest reached is a fault too. Exits 0
where every suite is, and 1 with a line for each fault where not, including where DIR holds no manifest.

Run: python3 scripts/ci/test_per_case_manifest.py
"""

import json
import sys
from pathlib import Path

MANIFEST = "shard.json"


def faults(directory, expected=None):
    """One line for each way the manifests under `directory` disagree or report a failure, or fall short
    of `expected`, a map of each platform that has to be there to its shard count."""
    found = []
    for path in sorted(Path(directory).rglob(MANIFEST)):
        try:
            found.append(json.loads(path.read_text()))
        except (OSError, ValueError):
            return ["{} is unreadable".format(path)]
    if not found:
        return ["no {} under {}: no shard wrote its records".format(MANIFEST, directory)]
    lines = []
    for platform, count in sorted((expected or {}).items()):
        counts = {entry["shards"] for entry in found if entry["platform"] == platform}
        if not counts:
            lines.append("{}: no manifest, so none of its {} shard(s) wrote records".format(platform, count))
        elif counts != {count}:
            lines.append("{}: expected {} shard(s), manifests say {}".format(platform, count, sorted(counts)))
    for platform in sorted({entry["platform"] for entry in found}):
        entries = [entry for entry in found if entry["platform"] == platform]
        counts = sorted({entry["shards"] for entry in entries})
        if len(counts) != 1:
            lines.append("{}: shards disagree on their count: {}".format(platform, counts))
            continue
        present = sorted(entry["shard"] for entry in entries)
        missing = sorted(set(range(1, counts[0] + 1)) - set(present))
        if missing or present != sorted(set(present)):
            lines.append("{}: shards {} of {} are missing or doubled in what was read".format(
                platform, missing or present, counts[0]))
        rosters = sorted({entry["roster"] for entry in entries})
        if len(rosters) != 1:
            lines.append("{}: shards were cut from rosters of different lengths: {}".format(platform, rosters))
        for entry in sorted(entries, key=lambda item: item["shard"]):
            if entry["status"] or entry["problems"]:
                lines.append("{} {}/{} failed with status {}: {}".format(
                    platform, entry["shard"], entry["shards"], entry["status"],
                    "; ".join(entry["problems"]) or "no launch named"))
    return lines


def main(argv):
    expected = {}
    for flag, given in zip(argv[1::2], argv[2::2]):
        platform, _, count = given.partition("=")
        if flag != "--expect" or not count.isdigit():
            print("cannot read {} {}: expected --expect PLATFORM=SHARDS".format(flag, given))
            return 2
        expected[platform] = int(count)
    lines = faults(argv[0], expected)
    for line in lines:
        print(line)
    if not lines:
        print("every shard read agrees and reports no failure")
    return 1 if lines else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
