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

Two readings reach further back than the change, to the newest `vX.Y.Z-main` tag the result descends
from -- the tag `upm.yml` leaves on the commit each release was dispatched from. A break reached a
minor across two changes: the first moved the entries out of the section and closed nothing, so it
was asked nothing, and the next closed a minor over a section already empty. So a change closing a
version is asked about every commit since that tag, and an entry the section held at any of them and
no longer holds, that the result carries in no major closed since it, is refused. And a dated section
is the note its release shipped: one that differs from the tag's copy, or is gone against it, is
refused whatever the change closes, since the file cannot tell a correction from a deletion. Where the
remote tags no release the result descends from -- a repository before its first release -- the
breaking section is read one step deep from `--base`, no dated section is held, and the pass says so.
A remote that cannot be listed, and a checkout whose history is cut short of a tagged commit, are
refused as unread instead, since none is the reading that passes.

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
import release_notes

CHANGELOG = "Packages/com.velvet.core/CHANGELOG.md"
BREAKING = "Unreleased — breaking"
VERSION_HEADING = re.compile(r"^## \[(\d+\.\d+\.\d+)\]", re.M)
MAIN_TAG = re.compile(r"^v(\d+)\.(\d+)\.(\d+)-main$")
DATED = re.compile(r"^\s*-\s*\d{4}-\d{2}-\d{2}")

UNNAMED = 2
UNREADABLE = 1


def run(args, timeout=20, cwd=None):
    try:
        done = subprocess.run(args, capture_output=True, text=True, timeout=timeout, cwd=cwd)
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


def at(rev, path, cwd=None):
    out, _ = run(["git", "show", f"{rev}:{path}"], cwd=cwd)
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


def version_key(tag):
    """The version a `vX.Y.Z-main` tag names, as a sortable tuple, or None for any other name."""
    found = MAIN_TAG.match(tag)
    return tuple(int(part) for part in found.groups()) if found else None


class UnreadableRelease(Exception):
    """Whether the result descends from a `-main` tag could not be read: (the tag, why)."""


def reachable_release(result, tags, cwd=None):
    """The newest published release the result descends from, as (tag, revision), or None.

    `tags` maps each name to a revision `git show` accepts. Newest by version rather than nearest by
    distance, so this is not `git describe`; and only a `-main` tag, since the `vX.Y.Z` ones sit on
    split commits with no ancestor relation to any tree here.

    A commit the checkout has not got is one the result does not descend from, in a complete
    history; in one cut short it may be the release the reading was looking for, so that raises
    rather than reading as none, since none is what lets the fallback reading pass.
    """
    reachable = []
    for name, revision in tags.items():
        key = version_key(name)
        if key is None:
            continue
        try:
            done = subprocess.run(["git", "merge-base", "--is-ancestor", revision, result],
                                  capture_output=True, text=True, timeout=20, cwd=cwd)
        except (OSError, subprocess.SubprocessError) as failure:
            raise UnreadableRelease(name, str(failure))
        if done.returncode == 0:
            reachable.append((key, name, revision))
        elif done.returncode != 1:
            shallow, _ = run(["git", "rev-parse", "--is-shallow-repository"], cwd=cwd)
            if (shallow or "").strip() != "false":
                raise UnreadableRelease(
                    name, "this checkout's history is cut short of the commit the remote tags "
                    "({}); fetch it -- `git fetch --unshallow origin` -- and run this again".format(
                        done.stderr.strip() or f"exit {done.returncode}"))
    return max(reachable)[1:] if reachable else None


def held_in_breaking(release, result, cwd=None):
    """Every entry the breaking section held at the release or at any commit since it that touched
    the CHANGELOG, oldest sighting first, or None when that history could not be read.

    At the release itself as well as after it: an entry the first commit after the release moved out
    is in the release's copy and in no later one. Every parent of a merge is followed, so a sighting
    on the side a resolution dropped is read; `test_breaking_in_flight_check.py` holds that merge.
    """
    out, _ = run(["git", "log", "--full-history", "--format=%H", f"{release}..{result}",
                  "--", CHANGELOG], cwd=cwd)
    if out is None:
        return None
    held = []
    for revision in [release] + out.split()[::-1]:
        for entry in section(at(revision, CHANGELOG, cwd), BREAKING):
            if entry.strip() not in held:
                held.append(entry.strip())
    return held


def published_sections(text):
    """Each version heading in order: (version, whether dated, the lines published under it)."""
    lines = (text or "").splitlines()
    marks = [(index, match) for index, line in enumerate(lines)
             if (match := release_notes.VERSION_HEADING.match(line))]
    for order, (index, match) in enumerate(marks):
        end = marks[order + 1][0] if order + 1 < len(marks) else len(lines)
        yield (match.group("version"),
               bool(DATED.match(lines[index][match.end():])),
               published_lines(lines[index + 1:end]))


def published_lines(lines):
    """Every non-blank line of one section body, in the shape the note carries it.

    Indentation survives the collapse because it is what nests a line under another.
    """
    return [line[:len(line) - len(line.lstrip())] + " ".join(line.split())
            for line in release_notes.unwrap_soft_breaks(lines) if line.strip()]


def dated_sections(text):
    """The published lines of each dated section, by version."""
    return {version: lines for version, dated, lines in published_sections(text) if dated}


def drift(result_text, published_text):
    """The dated sections the result has lost against a release's copy, and the ones it has changed.

    Compared as ordered lines, so a reorder is a change too.
    """
    theirs = dated_sections(published_text)
    ours = dated_sections(result_text)
    return (sorted(version for version in theirs if version not in ours),
            sorted(version for version in theirs if version in ours and ours[version] != theirs[version]))


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
    parser.add_argument("--timeout", type=int, default=5,
                        help="seconds to allow the tag listing (default: published_check's)")
    args = parser.parse_args()

    # Asked of the remote rather than of a local tag list, for the reason published_check gives. A
    # listing that fails is refused rather than read as no tags, since none is what lets the two
    # readings that reach back to a release pass.
    try:
        tagged = published_check.remote_tag_shas(Path.cwd(), timeout=args.timeout)
    except (OSError, subprocess.SubprocessError) as failure:
        sys.stderr.write(
            "Could not list origin's tags, so nothing here read back to a release: {}\n"
            "A read that did not happen is not a read that found nothing.\n".format(failure))
        return UNREADABLE
    tags = set(tagged)
    try:
        release = reachable_release(args.result, tagged)
    except UnreadableRelease as failure:
        sys.stderr.write(
            "Whether {} descends from {} went unread, so nothing here read back to a release: {}\n"
            "A read that did not happen is not a read that found nothing.\n".format(
                args.result, failure.args[0], failure.args[1]))
        return UNREADABLE
    if release is None:
        print("no vX.Y.Z-main tag is reachable from {}, so the breaking section is read one step "
              "deep, from {}, and no published section is held to a release".format(
                  args.result, args.base))
    else:
        print("reading back to {}, the newest release {} descends from".format(
            release[0], args.result))
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

    # Whatever the change closes, since a change closing nothing can edit a published note, and the
    # write-time guard on the same reading fires only for the editor it hooks.
    if release is not None:
        gone, changed = drift(at(args.result, CHANGELOG), at(release[1], CHANGELOG))
        if gone or changed:
            sys.stderr.write(
                "{} is the newest release {} descends from, and {} dated section(s) differ from what "
                "it carries:\n{}\n\n"
                "A dated section is the note its release shipped, and a file cannot tell a correction\n"
                "from a deletion, so neither is made past the tag. Restore each as {} carries it:\n"
                "  git show {}:{}\n"
                "and put what this change has to say under '## [Unreleased]'.\n".format(
                    release[0], args.result, len(gone) + len(changed),
                    "\n".join(["  ## [{}]: gone".format(one) for one in gone]
                              + ["  ## [{}]: changed".format(one) for one in changed]),
                    release[0], release[0], CHANGELOG))
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

    # Every commit since the release rather than the one step from the base: the step that moved
    # an entry out of the section closed nothing and was asked nothing, and the base of the step
    # that closes is a section already empty, which is what a correct minor after a major looks
    # like. A major answers here too, for an entry that left the section before the change closing
    # it and is in no major closed since the release.
    if release is not None:
        held = held_in_breaking(release[1], args.result)
        if held is None:
            sys.stderr.write(
                "Could not read the CHANGELOG's history from {} to {}, so what '{}' has held since\n"
                "that release went unread. A read that did not happen is not a read that found\n"
                "nothing.\n".format(release[0], args.result, BREAKING))
            return UNREADABLE
        still = {entry.strip() for entry in left_in_breaking(args.result)}
        # Every major closed since the release rather than the ones this change closes: a major
        # closed, merged and not yet published is the section its entries went to.
        result_text = at(args.result, CHANGELOG)
        order = released_in_order(result_text)
        opened = [named for named in dated_sections(result_text)
                  if named not in dated_sections(at(release[1], CHANGELOG))
                  and named in order and published_check.is_major_bump(order, named)]
        carried = {entry.strip() for named in opened for entry in section(result_text, named)}
        astray = [entry for entry in held if entry not in still and entry not in carried]
        if astray:
            sys.stderr.write(
                "{} closes here, and since {} '{}' has held {} entr(y|ies) that this result carries "
                "in no major:\n{}\n\n"
                "A break leaves that section for a major and for nothing else, however many changes\n"
                "the leaving takes: one moved out by an earlier change ships here with nothing calling\n"
                "it a break, and one dropped ships undescribed. Compared as text, so one reworded on\n"
                "the way reads as dropped. Put each back under '## [{}]' as it was written, or close a\n"
                "major that carries it.\n".format(
                    ", ".join(versions), release[0], BREAKING, len(astray),
                    "\n".join("  " + one for one in astray), BREAKING))
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
