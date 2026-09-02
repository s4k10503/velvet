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

Two readings reach further back than the change, to the `vX.Y.Z-main` tags -- the tag `upm.yml`
leaves on the commit each release was dispatched from. A break reached a minor across two changes:
the first moved the entries out of the section and closed nothing, so it was asked nothing, and the
next closed a minor over a section already empty. So a change closing a version is asked about every
commit of main from the newest such tag the result descends from to the change's base -- the base
rather than the result, since the result's own history is the pull request's, where a line is written
and corrected without reaching main -- and an entry the section held at any of them, that the result
carries neither there nor in a major closed since the tag, is refused. A change closing nothing is
asked nothing by this reading. And a dated section is the note its release shipped: one whose version
the remote tags, whichever line published it, has to be the base's but for a line of that tag's copy
put back where that copy has it -- the base rather than the copy, since main's older sections were
reworded and reordered after their releases and carry a Highlights block their tags' copies do not --
one the base has not got arrives as that copy and nothing else, the copy being the only text of it
here and an addition to a published note belonging in the release that follows, and one gone that the
base or the last release on this line carried is refused, whatever the change closes, since the file
cannot tell a correction from a deletion.
Where the remote tags no release the result descends from -- a repository before its first
release -- the breaking section is read one step deep from `--base`, and the pass says so. A remote
that cannot be listed is refused as unread instead, since none is the reading that passes, and so is
a history cut short under the result, where a tag the result is not found to descend from may sit
below the cut. A tag whose commit is not here is otherwise passed over, as a maintenance line's is,
until the result carries a dated section for its version -- reading that release's note is then what
fails, and that is refused as unread too.

Exit 2 when a pull request goes unnamed, 1 when the state could not be read, 0 otherwise. A read that
did not happen is not a read that found nothing, and it must not pass.
"""

import argparse
import json
import re
import subprocess
import sys
from collections import Counter
from itertools import takewhile
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
    """A release the remote tags that could not be read here: (the tag, why)."""


def cut_below(result, cwd=None):
    """Whether this checkout's history is cut short somewhere under `result`: a shallow boundary the
    result descends from.

    Under one, a commit `git merge-base` finds no path to may sit below the cut, so "not an ancestor"
    is unreadable there and an answer everywhere else -- a maintenance line fetched shallow cuts the
    history, and not under the result.
    """
    where, _ = run(["git", "rev-parse", "--git-path", "shallow"], cwd=cwd)
    if where is None:
        return False
    path = Path(where.strip())
    if not path.is_absolute():
        path = Path(cwd or ".") / path
    try:
        boundaries = path.read_text().split()
    except OSError:
        return False
    return any(run(["git", "merge-base", "--is-ancestor", boundary, result], cwd=cwd)[0] is not None
               for boundary in boundaries)


def reachable_release(result, tags, cwd=None):
    """The newest published release the result descends from, as (tag, revision), or None.

    `tags` maps each name to a revision `git show` accepts. Newest by version rather than nearest by
    distance, so this is not `git describe`; and only a `-main` tag, since the `vX.Y.Z` ones sit on
    split commits with no ancestor relation to any tree here.

    A tag the result is not found to descend from -- a maintenance line's, or one whose commit this
    checkout has not got -- is passed over where the history under the result is whole, and raises
    where it is cut short there, since the cut may be what hides the path; `cut_below` tells the two
    apart. Raising rather than reading as none, since none is what lets the fallback reading pass.
    """
    reachable = []
    cut = None
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
            continue
        if cut is None:
            cut = cut_below(result, cwd)
        if cut:
            raise UnreadableRelease(
                name, "this checkout's history is cut short under {}, so whether {} sits below the "
                "cut is not readable ({}); fetch the rest -- `git fetch --unshallow origin` -- and "
                "run this again".format(result, name, done.stderr.strip() or f"exit {done.returncode}"))
    return max(reachable)[1:] if reachable else None


def held_in_breaking(release, base, cwd=None):
    """Every entry the breaking section held at the release, at the base, or at any commit between
    them that touched the CHANGELOG, oldest sighting first, or None when that history could not be
    read.

    The base rather than the result: the result's own history is the pull request's, where a line is
    written and corrected without ever reaching main, and the memory here is main's. The release
    itself is read as well as what follows it, since an entry the first commit after the release
    moved out is in the release's copy and in no later one. Every parent of a merge on the range is
    followed, so a sighting on the side a resolution dropped is read; the tests hold that merge.
    """
    out, _ = run(["git", "log", "--full-history", "--format=%H", f"{release}..{base}",
                  "--", CHANGELOG], cwd=cwd)
    if out is None:
        return None
    held = []
    for revision in [release] + out.split()[::-1] + [base]:
        for entry in section(at(revision, CHANGELOG, cwd), BREAKING):
            if entry.strip() not in held:
                held.append(entry.strip())
    return held


def published_sections(text):
    """Each version heading in order: (version, whether dated, the heading as published, the lines
    published under it)."""
    lines = (text or "").splitlines()
    marks = [(index, match) for index, line in enumerate(lines)
             if (match := release_notes.VERSION_HEADING.match(line))]
    for order, (index, match) in enumerate(marks):
        end = marks[order + 1][0] if order + 1 < len(marks) else len(lines)
        yield (match.group("version"),
               bool(DATED.match(lines[index][match.end():])),
               " ".join(lines[index].split()),
               published_lines(lines[index + 1:end]))


def published_lines(lines):
    """Every non-blank line of one section body, in the shape the note carries it.

    Indentation survives the collapse because it is what nests a line under another.
    """
    return [line[:len(line) - len(line.lstrip())] + " ".join(line.split())
            for line in release_notes.unwrap_soft_breaks(lines) if line.strip()]


def dated_sections(text):
    """Each dated section's published lines by version, the heading first, so its date is compared
    with the rest."""
    return {version: [heading] + lines
            for version, dated, heading, lines in published_sections(text) if dated}


def added_positions(lines, before):
    """Which of `lines` are not `before`'s own, by index, or None where `before` is not carried in
    order at all -- a line lost, reworded or moved."""
    reached, added = 0, []
    for index, line in enumerate(lines):
        if reached < len(before) and line == before[reached]:
            reached += 1
        else:
            added.append(index)
    return added if reached == len(before) else None


def puts(copy, line, side):
    """Whether `copy` carries `line` somewhere in `side`, a range of its indices, or does not carry
    it at all -- a line the copy never held says nothing about which side of anything it belongs
    on."""
    return line not in copy or any(copy[where] == line for where in side)


def touching(lines, index, copy, where):
    """Whether the nearest line here that `copy` carries, on one side of `index` or the other, is
    the one the copy puts immediately there.

    Lines the copy has not got are stepped over rather than read, so whatever a section gained
    after its release neither satisfies this nor stands in the way of a put-back.
    """
    above = next((one for one in lines[index - 1::-1] if one in copy), None) if index else None
    below = next((one for one in lines[index + 1:] if one in copy), None)
    return ((where > 0 and above == copy[where - 1])
            or (where + 1 < len(copy) and below == copy[where + 1]))


def under(lines, index):
    """The heading `lines[index]` is published under, or None where no heading of the section is
    above it."""
    return next((one for one in lines[index - 1::-1]
                 if release_notes.SUBSECTION_HEADING.match(one)), None) if index else None


def block(lines, index):
    """The lines `lines[index]` heads, up to the next heading of the section."""
    return takewhile(lambda one: not release_notes.SUBSECTION_HEADING.match(one),
                     lines[index + 1:])


def files_as_published(lines, index, copy, where):
    """Whether the note goes on filing each line under the heading that published it: the put-back
    under the heading the copy files it under, and, where the put-back is itself a heading, at least
    one line and none whose published name it changes.

    The order readings admit both `## [1.4.0]`'s repair -- a heading put back above the entry the
    copy ends its block with, against a base that merged the two blocks -- and an entry put back
    under the heading above the one the copy files it under, against a base that moved that heading
    up. Which heading the note then publishes each line under is what separates them, and none of
    the three reads it.

    A heading put back under one of the same name leaves what it comes to file reading as it did, so
    a line the section gained after its release does not stand in the way of it, as it does not of
    `touching`. A heading put back that heads nothing is refused ahead of both. The question is
    asked of the put-back's own line, so it does not answer for every empty block a result can
    carry: one the put-back empties above itself passes, and so does the first of two identical
    headings it makes adjacent, since `added_positions` charges only one of them. What it costs a
    contributor is the heading-first half of a two-step restore, which the refusal says to make in
    one edit with an entry it heads.
    """
    if not release_notes.SUBSECTION_HEADING.match(lines[index]):
        return under(lines, index) == under(copy, where)
    comes = list(block(lines, index))
    if not comes:
        return False
    if under(lines, index) == lines[index]:
        return True
    filed = {one for at, one in enumerate(copy) if under(copy, at) == copy[where]}
    return all(one in filed for one in comes)


def in_its_place(lines, index, copy):
    """Whether the line at `index` may go back there: four readings of the copy, each refusing what
    the others admit.

    Either side of the one line rather than over the section, since asking the section to carry the
    copy in order would refuse every change to a section reordered against its copy.

    The lines the copy puts either side of it bound where it may go, walking outward past any this
    section has not got: a bound that is absent bounds nothing, and a line missing beside another
    was then free of that side and could go back under another heading. Between the bound above and
    the put-back, every line has to be one the copy puts above it as well, which is what catches a
    copy line belonging below the put-back sitting over it. `touching` asks what it comes to rest
    against, which neither of those can ask of the copy's last line: the whole copy is above that,
    so nothing bounds it below and nothing over it contradicts, and it could be appended under a
    heading the copy never files it under. And `files_as_published` asks what the note then files
    where, which all three read past.
    """
    for where, published in enumerate(copy):
        if published != lines[index]:
            continue
        above = next((one for one in copy[where - 1::-1] if one in lines), None) if where else None
        below = next((one for one in copy[where + 1:] if one in lines), None)
        if above is not None:
            if above not in lines[:index]:
                continue
            start = max(at for at in range(index) if lines[at] == above)
            if not all(puts(copy, lines[at], range(where)) for at in range(start + 1, index)):
                continue
        if below is not None and below not in lines[index + 1:]:
            continue
        if touching(lines, index, copy, where) and files_as_published(lines, index, copy, where):
            return True
    return False


def only_put_back(lines, before, copy):
    """Whether `lines` are `before` with nothing lost or moved, and nothing added but a line of
    `copy` that `before` is short of, put back where `copy` has it: the one kind of change a section
    already here takes, since the note the release shipped is what a repair restores.

    Short of the base as well as in the copy, because a line the section already carries is in the
    copy too -- allowed on the copy alone, a second one arrives and the note ships the bullet twice.
    """
    added = added_positions(lines, before)
    if added is None:
        return False
    if Counter(lines[index] for index in added) - (Counter(copy) - Counter(before)):
        return False
    return all(in_its_place(lines, index, copy) for index in added)


def published_copy(version, tags, read):
    """(the `-main` tag naming this version's release, that tag's copy of the dated section), or None
    where the remote tags no such version, which is one not yet published.

    `read(revision)` is the CHANGELOG at a revision as (text, None), or (None, why) where the
    checkout could not show it -- which raises, as does a copy carrying no dated section for the
    version: the note exists and could not be compared, which is not the same as there being none.
    """
    tag = f"v{version}-main"
    if tag not in tags:
        return None
    text, why = read(tags[tag])
    if text is None:
        raise UnreadableRelease(tag, why)
    copy = dated_sections(text).get(version)
    if copy is None:
        raise UnreadableRelease(tag, f"its CHANGELOG carries no dated `## [{version}]`")
    return tag, copy


def drifted(result_text, base_text, tags, read):
    """The result's dated sections that a release tags and that do not carry its note, as
    (version, tag, how).

    A section the base carries has to be the base's but for a line of its tag's copy put back where
    that copy has it, heading and date included, so a line deleted, reworded, reordered or put back
    elsewhere is refused by the change that does it. The base rather than the tag's copy: main's
    older sections were reworded and reordered after their releases and carry a Highlights block
    their tags' copies do not, so the copy says which lines may go back and the base says what is
    there. A section the base does not carry -- a maintenance line's, brought in -- arrives as its
    tag's copy and nothing else: that copy is the only text of it here, and an addition to a note
    already published belongs in the release that follows it.
    """
    ours, theirs = dated_sections(result_text), dated_sections(base_text)
    found = []
    for version, lines in ours.items():
        published = published_copy(version, tags, read)
        if published is None:
            continue
        tag, copy = published
        if version in theirs:
            if lines != theirs[version] and not only_put_back(lines, theirs[version], copy):
                found.append((version, tag, f"changed against the base, and {tag} shipped it"))
        elif lines != copy:
            found.append((version, tag, f"brought in changed against {tag}'s copy"))
    return found


def gone_sections(result_text, base_text, copy_text, tags):
    """Dated sections the base or the last release on this line carried, that the remote tags, and
    the result has not got, as (version, tag)."""
    ours = dated_sections(result_text)
    carried = set(dated_sections(base_text)) | set(dated_sections(copy_text))
    return sorted((version, f"v{version}-main") for version in carried
                  if f"v{version}-main" in tags and version not in ours)


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
            "Could not list origin's tags, so nothing here read back to a release: {}\n{}"
            "A read that did not happen is not a read that found nothing.\n".format(
                failure, (getattr(failure, "stderr", "") or "").strip() + "\n"
                if (getattr(failure, "stderr", "") or "").strip() else ""))
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
              "deep, from {}; a dated section whose version the remote tags is still held to that "
              "tag".format(args.result, args.base))
    else:
        print("reading '{}' back to {}, the newest release {} descends from, and each dated "
              "section to its own release's tag".format(BREAKING, release[0], args.result))
    base_text, result_text = at(args.base, CHANGELOG), at(args.result, CHANGELOG)
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
    try:
        changed = drifted(result_text, base_text, tagged,
                          lambda revision: run(["git", "show", f"{revision}:{CHANGELOG}"]))
    except UnreadableRelease as failure:
        sys.stderr.write(
            "The remote tags {} and its CHANGELOG could not be read here: {}\n"
            "A dated section of {} is that release's to hold, and whether it still carries the note\n"
            "went unread. A read that did not happen is not a read that found nothing.\n".format(
                failure.args[0], failure.args[1], args.result))
        return UNREADABLE
    gone = gone_sections(result_text, base_text,
                         at(release[1], CHANGELOG) if release else None, tagged)
    if gone or changed:
        sys.stderr.write(
            "{} dated section(s) of {} do not carry the note their release shipped:\n{}\n\n"
            "A file cannot tell a correction from a deletion, so neither is made past the tag: a\n"
            "dated section is the base's but for a line its tag's copy has and the base is short\n"
            "of, put back where that copy has it, and one the base has not got arrives as that copy\n"
            "entire -- an addition to a note already published belongs in the release that follows\n"
            "it. Read each copy from the commit the remote tags:\n{}\n"
            "and put what this change has to say under '## [Unreleased]'.\n".format(
                len(gone) + len(changed), args.result,
                "\n".join(["  ## [{}]: gone, and {} carries it".format(version, tag)
                           for version, tag in gone]
                          + ["  ## [{}]: {}".format(version, how)
                             for version, _, how in changed]),
                "\n".join(sorted(
                    "  git show {}:{}   # {}".format(tagged[tag], CHANGELOG, tag)
                    for tag in {tag for _, tag in gone} | {tag for _, tag, _ in changed}))))
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
              if published_check.is_major_bump(released_in_order(result_text), named)]
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

    # Every commit of main since the release rather than the one step from the base: the step that
    # moved an entry out of the section closed nothing and was asked nothing, and the base of the
    # step that closes is a section already empty, which is what a correct minor after a major looks
    # like. A major answers here too, for an entry that left the section before the change closing
    # it and is in no major closed since the release.
    if release is not None:
        held = held_in_breaking(release[1], args.base)
        if held is None:
            sys.stderr.write(
                "Could not read the CHANGELOG's history from {} to {}, so what '{}' has held since\n"
                "that release went unread. A read that did not happen is not a read that found\n"
                "nothing.\n".format(release[0], args.base, BREAKING))
            return UNREADABLE
        still = {entry.strip() for entry in left_in_breaking(args.result)}
        # Every major closed since the release rather than the ones this change closes: a major
        # closed, merged and not yet published is the section its entries went to.
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
