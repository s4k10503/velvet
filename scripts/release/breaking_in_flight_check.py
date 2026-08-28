#!/usr/bin/env python3
"""Refuse a version-closing change that says nothing about the breaking work still in flight.

Every other reading in the release path is of one tree. `published_check.py` asks what this change
does to `## [Unreleased — breaking]`, `is_major_bump` asks what the CHANGELOG already holds, and the
skill calls that file the single source of truth for the note. A pull request that has not merged is
invisible to all of them, so the question "what is this major for" has no reader at all.

That is how v3.0.0 shipped twelve of the thirteen breaking entries written for it. The thirteenth was
PR #377, whose body said its entry sat "beside the twelve breaking entries already waiting for the
next major" -- the set was recorded, on a branch, where nothing in the release path looks.

So a change that closes a version is asked to name every open pull request adding to that section,
and to say for each whether the version carries it. Naming is the whole of what is required: the
answer "not this one" is a decision somebody made, and it is the not-deciding this exists to stop.

Exit 2 when a pull request goes unnamed, 1 when the state could not be read, 0 otherwise. A read that
did not happen is not a read that found nothing, and it must not pass.
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

CHANGELOG = "Packages/com.velvet.core/CHANGELOG.md"
BREAKING = "Unreleased — breaking"
VERSION_HEADING = re.compile(r"^## \[(\d+\.\d+\.\d+)\]", re.M)

UNNAMED = 2
UNREADABLE = 1


def run(args, timeout=20):
    try:
        done = subprocess.run(args, capture_output=True, text=True, timeout=timeout)
    except (OSError, subprocess.SubprocessError) as failure:
        return None, str(failure)
    if done.returncode != 0:
        return None, done.stderr.strip() or f"exit {done.returncode}"
    return done.stdout, None


def released(text):
    return set(VERSION_HEADING.findall(text or ""))


def section(text, heading):
    start = (text or "").find(f"## [{heading}]")
    if start < 0:
        return []
    end = text.find("\n## [", start + 5)
    body = text[start:] if end < 0 else text[start:end]
    return [line for line in body.splitlines() if line.startswith("- ")]


def at(rev, path):
    out, _ = run(["git", "show", f"{rev}:{path}"])
    return out


def closing(base, result):
    """The versions this change closes, which is the whole of when the question is asked."""
    return sorted(released(at(result, CHANGELOG)) - released(at(base, CHANGELOG)))


def open_pull_requests(repo):
    out, failure = run(["gh", "pr", "list", "--state", "open", "--limit", "100",
                        "--json", "number,title,headRefName,headRefOid"]
                       + (["--repo", repo] if repo else []), timeout=30)
    if out is None:
        return None, failure
    try:
        return json.loads(out), None
    except ValueError as failure:
        return None, str(failure)


def adds_breaking(pull, base):
    """Whether the branch holds a breaking entry the base does not.

    Read as a set difference rather than as a diff, so an entry the base already carries is not
    charged to whichever branch happens to touch the file beside it.
    """
    theirs = at(pull["headRefOid"], CHANGELOG)
    if theirs is None:
        return None
    return [entry for entry in section(theirs, BREAKING)
            if entry not in section(at(base, CHANGELOG), BREAKING)]


def unnamed(pulls, body):
    """Pull requests the body does not name, by number."""
    said = set(re.findall(r"#(\d+)", body or ""))
    return [pull for pull in pulls if str(pull["number"]) not in said]


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--base", required=True, help="revision this change is measured against")
    parser.add_argument("--result", default="HEAD", help="revision this change produces")
    parser.add_argument("--body-file", help="file holding the pull request body")
    parser.add_argument("--repo", help="owner/name, when gh cannot infer it")
    args = parser.parse_args()

    versions = closing(args.base, args.result)
    if not versions:
        print("closes no version, so nothing is asked about work in flight")
        return 0

    pulls, failure = open_pull_requests(args.repo)
    if pulls is None:
        sys.stderr.write(
            "Could not list open pull requests, so nothing read what is in flight: {}\n"
            "{} closes here, and whether the breaking work written for it is in or out went\n"
            "unasked. A read that did not happen is not a read that found nothing.\n".format(
                failure, ", ".join(versions)))
        return UNREADABLE

    carrying = []
    for pull in pulls:
        entries = adds_breaking(pull, args.base)
        if entries is None:
            sys.stderr.write(
                "Could not read the CHANGELOG on #{}, so what it adds went unread: fetch it and\n"
                "run this again.\n".format(pull["number"]))
            return UNREADABLE
        if entries:
            carrying.append((pull, entries))

    if not carrying:
        print("{} closes with no breaking entry in flight".format(", ".join(versions)))
        return 0

    body = ""
    if args.body_file:
        try:
            body = Path(args.body_file).read_text()
        except OSError as failure:
            sys.stderr.write("Could not read the body at {}: {}\n".format(args.body_file, failure))
            return UNREADABLE

    missing = unnamed([pull for pull, _ in carrying], body)
    if not missing:
        print("{} closes and names every open pull request adding a breaking entry".format(
            ", ".join(versions)))
        return 0

    sys.stderr.write(
        "{} closes here, and {} open pull request(s) add an entry to "
        "'## [{}]' that this body does not name:\n".format(
            ", ".join(versions), len(missing), BREAKING))
    for pull in missing:
        entries = dict((p["number"], e) for p, e in carrying)[pull["number"]]
        first = entries[0][2:].strip()
        sys.stderr.write("  #{} {}\n      {}{}\n".format(
            pull["number"], pull["title"][:78], first[:92], "…" if len(first) > 92 else ""))
    sys.stderr.write(
        "\nA breaking entry is written for the next major, so the major closing here is the one it\n"
        "was written for. Say in the body which of these the version carries and which it does not —\n"
        "'not this one' is a decision, and going without one is what this refuses.\n")
    return UNNAMED


if __name__ == "__main__":
    sys.exit(main())
