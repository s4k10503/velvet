#!/usr/bin/env python3
"""Check the `shard.json` each per-case shard wrote against the others, and say which shards failed.

`.github/workflows/per-case-coverage.yml`'s report job runs this over the folder holding every shard's
records, whatever the suite jobs ended with, so that a failed shard is named beside the lines read off
the others rather than costing the whole run's records.

    python3 scripts/ci/per_case_manifest.py DIR

A suite is in order where its manifests name the same shard count, cover each shard from 1 to that
count once, agree on the length of the roster they were cut from, and report no failed launch. Exits 0
where every suite is, and 1 with a line for each fault where not, including where DIR holds no manifest.

Run: python3 scripts/ci/test_per_case_manifest.py
"""

import json
import sys
from pathlib import Path

MANIFEST = "shard.json"


def faults(directory):
    """One line for each way the manifests under `directory` disagree or report a failure."""
    found = []
    for path in sorted(Path(directory).rglob(MANIFEST)):
        try:
            found.append(json.loads(path.read_text()))
        except (OSError, ValueError):
            return ["{} is unreadable".format(path)]
    if not found:
        return ["no {} under {}: no shard wrote its records".format(MANIFEST, directory)]
    lines = []
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
    lines = faults(argv[0])
    for line in lines:
        print(line)
    if not lines:
        print("every shard read agrees and reports no failure")
    return 1 if lines else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
