#!/usr/bin/env python3
"""Print the commit a workflow job reads its change against, as the `sha=` line $GITHUB_OUTPUT takes.

Every workflow step that hands a script `--base` takes it from here.

- pull_request: the first parent of the checkout, which is GitHub's test merge of the head into the
  base branch. Not the event's `pull_request.base.sha`: measured on this repository's CI, it named the
  commit the head was cut from while the test merge's first parent was another pull request's merge
  made since, and the base-red lane counted that pull request's lines as this one's.
- merge_group: the event's `merge_group.base_sha`.
- workflow_dispatch with a `base` input: the merge base of the checkout with origin's copy of that
  branch, which is what mutation.yml is dispatched with, onto a checkout of the head itself.
- with $MUTATION_SCOPE set, any other event, or a dispatch whose `base` is empty: the mutation
  campaign's scope (`campaign_scope`), which mutation.yml's record runs and the release gate read
  against.
- anything else: nothing. What a push or another dispatch reads against is each caller's own answer.

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


# The file whose first-parent addition is the campaign's cutover: what merged before it the
# per-pull-request campaign measured, and the record starts after it.
CUTOVER_FILE = "scripts/ci/mutation_record.py"

# What a published release is tagged with on the branch it was cut from.
RELEASE_TAGS = "v*-main"


def git_out(*arguments):
    done = subprocess.run(["git", *arguments], capture_output=True, text=True)
    return done.stdout.strip() if done.returncode == 0 else ""


def campaign_scope():
    """The later of the cutover and the newest release tag in HEAD's history.

    A pure reading of git: the same history names the same commit. The cutover is the oldest
    first-parent commit adding `CUTOVER_FILE`; a history without one starts at the tag alone.
    """
    added = git_out("log", "--first-parent", "--diff-filter=A", "--format=%H", "HEAD", "--",
                    CUTOVER_FILE).split()
    cutover = added[-1] if added else ""
    tag = git_out("describe", "--tags", "--abbrev=0", "--match", RELEASE_TAGS, "HEAD")
    tagged = git_out("rev-parse", tag + "^{commit}") if tag else ""
    if cutover and tagged:
        later = subprocess.run(["git", "merge-base", "--is-ancestor", cutover, tagged],
                               capture_output=True).returncode == 0
        return tagged if later else cutover
    if cutover or tagged:
        return cutover or tagged
    raise SystemExit("neither a commit adding {} nor a {} tag is in HEAD's history, so the campaign "
                     "has no scope to read against".format(CUTOVER_FILE, RELEASE_TAGS))


def resolve(event_name, payload, scope=False):
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
    branch = (payload.get("inputs") or {}).get("base") if event_name == "workflow_dispatch" else None
    if branch:
        done = subprocess.run(["git", "merge-base", "origin/" + branch, "HEAD"],
                              capture_output=True, text=True)
        if done.returncode != 0:
            raise SystemExit("no merge base of HEAD with origin/{}: {}".format(
                branch, done.stderr.strip() or "git answered nothing"))
        return done.stdout.strip()
    return campaign_scope() if scope else ""


def main():
    event_name = os.environ.get("GITHUB_EVENT_NAME", "")
    path = os.environ.get("GITHUB_EVENT_PATH")
    payload = {}
    if path:
        with open(path, encoding="utf-8") as handle:
            payload = json.load(handle)
    base = resolve(event_name, payload, bool(os.environ.get("MUTATION_SCOPE")))
    print("{}: change base {}".format(event_name or "no event", base or "(none)"), file=sys.stderr)
    print("sha={}".format(base))
    return 0


if __name__ == "__main__":
    sys.exit(main())
