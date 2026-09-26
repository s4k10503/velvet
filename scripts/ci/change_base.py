#!/usr/bin/env python3
"""Print the commit a workflow job reads its change against, as the `sha=` line $GITHUB_OUTPUT takes.

Every step in test.yml that hands a script `--base` takes it from here.

- pull_request: the first parent of the checkout, which is GitHub's test merge of the head into the
  base branch. Not the event's `pull_request.base.sha`: measured on this repository's CI, it named the
  commit the head was cut from while the test merge's first parent was another pull request's merge
  made since, and the mutation plan and the base-red lane both counted that pull request's lines as
  this one's.
- merge_group: the event's `merge_group.base_sha`.
- anything else: nothing. What a push or a dispatch reads against is each caller's own answer.

The first parent is named only once the second is the event's head. Over a checkout of the head
itself, the first parent is the head's previous commit, and every job would read the head's last
commit as the whole change.

Run, in a workflow step: python3 scripts/ci/change_base.py >> "$GITHUB_OUTPUT"
"""

import json
import os
import subprocess
import sys


def rev_parse(revision):
    done = subprocess.run(["git", "rev-parse", "--verify", "--quiet", revision + "^{commit}"],
                          capture_output=True, text=True)
    return done.stdout.strip() if done.returncode == 0 else None


def resolve(event_name, payload):
    if event_name == "pull_request":
        head = payload["pull_request"]["head"]["sha"]
        merged = rev_parse("HEAD^2")
        if merged != head:
            raise SystemExit(
                "the checkout is not the test merge of {}: its second parent is {}. The first parent "
                "is the base only on that merge.".format(head, merged or "absent"))
        return rev_parse("HEAD^1")
    if event_name == "merge_group":
        return payload["merge_group"]["base_sha"]
    return ""


def main():
    event_name = os.environ.get("GITHUB_EVENT_NAME", "")
    path = os.environ.get("GITHUB_EVENT_PATH")
    payload = {}
    if path:
        with open(path, encoding="utf-8") as handle:
            payload = json.load(handle)
    base = resolve(event_name, payload)
    print("{}: change base {}".format(event_name or "no event", base or "(none)"), file=sys.stderr)
    print("sha={}".format(base))
    return 0


if __name__ == "__main__":
    sys.exit(main())
