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

import published_check

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


def released_in_order(text):
    """The dated headings newest first, which is what is_major_bump reads a version's place in."""
    return [named for named in VERSION_HEADING.findall(text or "")
            if named not in ("Unreleased", BREAKING)]


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


def closing(base, result, tags=()):
    """The versions this change closes, which is the whole of when the question is asked.

    A version the remote already tags is one this change records rather than closes -- carrying a
    maintenance line's released section forward brings it across, and asked without the tags that
    reads as a release, so the question goes to a body that decides nothing about it. The same
    reading published_check.drain_reason takes, for the same reason.
    """
    return sorted(named for named in released(at(result, CHANGELOG)) - released(at(base, CHANGELOG))
                  if "v" + named not in tags)


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

    The head is asked for by sha and fetched when it is missing: the shas come from a live listing
    and the objects from a checkout taken before it, so any pull request pushed since this run
    started names a commit that is not here. That is ordinary while a queue is draining, and it was
    reported as something the reader should go and fetch.
    """
    theirs = at(pull["headRefOid"], CHANGELOG)
    if theirs is None:
        run(["git", "fetch", "--quiet", "--depth", "1", "origin", pull["headRefOid"]], timeout=60)
        theirs = at(pull["headRefOid"], CHANGELOG)
    if theirs is None:
        return None
    return [entry for entry in section(theirs, BREAKING)
            if entry not in section(at(base, CHANGELOG), BREAKING)]


def lost_from_breaking(base, result):
    """Entries the breaking section held at the base that the result carries nowhere at all.

    Moving one out is legitimate -- reclassifying an entry as non-breaking is what moving the
    TabIndex one was -- and a move leaves it somewhere in the file. A removal leaves it nowhere, and
    the emptiness reading only ever saw a section emptied completely: measured, moving a single
    entry out moved no case at all.
    """
    before = section(at(base, CHANGELOG), BREAKING)
    after = (at(result, CHANGELOG) or "")
    return [entry for entry in before if entry.strip() not in after]


def left_in_breaking(result):
    """What the breaking section still holds."""
    return section(at(result, CHANGELOG), BREAKING)


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

    # Asked of the remote rather than of a local tag list, for the reason published_check gives. A
    # checkout with no remote to ask reads as no tags, which is the stricter direction: every
    # version then counts as closing, which is what this did before it could ask at all.
    try:
        tags = published_check.remote_tags(Path.cwd())
    except Exception:
        tags = set()
    # Before the version reading, because it holds whatever this change closes and also when it
    # closes nothing: an entry moved out of the section and into no other is lost, and the reading
    # that watched the section only ever saw it emptied completely.
    lost = lost_from_breaking(args.base, args.result)
    if lost:
        sys.stderr.write(
            "{} entries left '{}' and the result carries them nowhere:\n{}\n\n"
            "Reclassifying one is a move -- it lands in another section and is still in the file.\n"
            "Nothing here removes a break from the record, so this is a move that lost its\n"
            "destination.\n".format(len(lost), BREAKING, "\n".join("  " + one for one in lost)))
        return UNNAMED

    versions = closing(args.base, args.result, tags)
    if not versions:
        print("closes no version, so nothing is asked about work in flight")
        return 0

    # A major ships the breaks written for it, and the heading guard cannot see where the entries
    # went. Measured: a 3.0.0 closed with all ten entries left behind passes every other reading,
    # and its note describes none of the breaks it ships. Left behind on purpose is a decision --
    # an entry can wait for the major after this one -- so it is asked for rather than refused.
    majors = [named for named in versions
              if published_check.is_major_bump(
                  released_in_order(at(args.result, CHANGELOG)), named)]
    behind = left_in_breaking(args.result) if majors else []
    if behind:
        body_text = ""
        if args.body_file:
            try:
                body_text = Path(args.body_file).read_text()
            except OSError:
                body_text = ""
        if BREAKING not in body_text:
            sys.stderr.write(
                "{} closes a major and leaves {} entr(y|ies) in '{}':\n{}\n\n"
                "A major moves the breaking entries up into the section it closes. One left behind\n"
                "waits for the major after this, which is a decision -- say in the body which of\n"
                "these this version carries and which it does not, naming the section.\n".format(
                    ", ".join(majors), len(behind), BREAKING,
                    "\n".join("  " + one for one in behind)))
            return UNNAMED

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
                "Could not read the CHANGELOG on #{}, so what it adds went unread. Fetching its\n"
                "head did not bring it either, so this is not the ordinary case of a branch pushed\n"
                "since the checkout.\n".format(pull["number"]))
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
        "'not this one' is a decision, and going without one is what this refuses. The body is read\n"
        "when this runs, so re-running it after the edit is enough.\n")
    return UNNAMED


if __name__ == "__main__":
    sys.exit(main())
