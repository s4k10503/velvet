#!/usr/bin/env python3
"""Assemble a GitHub release body from a version's CHANGELOG section.

The CHANGELOG is the single source of truth for what a release contains: this reads one
version's section and emits the published note, so nothing about a release is written twice.

Every failure here is loud. A release note that is missing its body is indistinguishable from
one for a release that genuinely changed little, so an absent section, an absent Highlights
block, or an empty one exits non-zero rather than emitting a shorter note.
"""

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

VERSION_HEADING = re.compile(r"^## +\[(?P<version>[^\]]+)\]")
SUBSECTION_HEADING = re.compile(r"^### +(?P<title>.+?)\s*$")
ANY_HEADING = re.compile(r"^#{1,6}(?: |$)")

HIGHLIGHTS_TITLE = "Highlights"

# The two sections that carry no version. CONTRIBUTING.md's release section owns which entry goes in
# which and what a release does with each.
OPEN_SECTION = "Unreleased"
BREAKING_SECTION = "Unreleased — breaking"

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
DEFAULT_CHANGELOG = REPO_ROOT / "Packages" / "com.velvet.core" / "CHANGELOG.md"
DEFAULT_PACKAGE_JSON = REPO_ROOT / "Packages" / "com.velvet.core" / "package.json"

# One file per change, filed under the open section its directory names, so a change writes a file of
# its own rather than lines of a shared one. CONTRIBUTING.md's release section owns the layout.
DEFAULT_FRAGMENTS = REPO_ROOT / "Packages" / "com.velvet.core" / "Changelog~"
FRAGMENT_PATH = DEFAULT_FRAGMENTS.relative_to(REPO_ROOT).as_posix()
FRAGMENT_SECTIONS = {"unreleased": OPEN_SECTION, "breaking": BREAKING_SECTION}
FRAGMENT_KINDS = ("Added", "Changed", "Deprecated", "Removed", "Fixed", "Security")
# A tree carrying this reads fragments. A maintenance line cut before fragments existed does not, and
# takes its entries inline; CONTRIBUTING.md's maintenance-line section owns that rule.
FRAGMENT_COMPILER = "scripts/release/compile_changelog.py"
# A line under a breaking entry naming the first line it rewrites, which breaking_in_flight_check.py
# reads as that entry carried. It stays in the fragment for that reading, and compose drops it, so
# compile_changelog.py never writes it into the file a note is built from.
CORRECTS_FORM = "<!-- corrects: - <the old first line, as written> -->"
CORRECTS = re.compile(r"^<!-- corrects: (?P<entry>- .*\S) -->$")


class ReleaseNotesError(Exception):
    """A CHANGELOG that cannot produce a complete note."""


def items_under_no_subsection(section_text):
    """Every item in a section that no `###` heading of that section covers.

    An entry written under a heading of the wrong depth is invisible to the readers that walk the
    section by its subsections, and stays invisible until the version closes -- at which point the
    note is built from what they can see. Measured: `#### Fixed` in a closed version surfaces here
    as "Highlights and nothing else", and in an open one as nothing at all, because nothing walks an
    open section.

    Read of any section, which is the point: `[Unreleased]` carries no Highlights and never will,
    so the readings that need them cannot be asked of it, and this one does not need them.
    """
    stray, covered = [], False
    lines = section_text.splitlines() if isinstance(section_text, str) else list(section_text)
    for line in lines:
        if VERSION_HEADING.match(line):
            continue
        if SUBSECTION_HEADING.match(line):
            covered = True
            continue
        if line.startswith("- ") and not covered:
            stray.append(line)
    return stray


def extract_version_section(changelog_text, version):
    """Return the lines under `## [version]`, excluding the heading itself."""
    lines = changelog_text.splitlines()
    start = None
    for index, line in enumerate(lines):
        match = VERSION_HEADING.match(line)
        if match and match.group("version") == version:
            start = index + 1
            break
    if start is None:
        raise ReleaseNotesError(
            f"CHANGELOG has no '## [{version}]' section. "
            f"Add one before releasing {version}."
        )

    end = len(lines)
    for index in range(start, len(lines)):
        if VERSION_HEADING.match(lines[index]):
            end = index
            break
    return lines[start:end]


def split_highlights(section_lines, version):
    """Separate the `### Highlights` block from the rest of a version section."""
    highlights = []
    remainder = []
    in_highlights = False
    seen_highlights = False

    for line in section_lines:
        match = SUBSECTION_HEADING.match(line)
        if match:
            in_highlights = match.group("title") == HIGHLIGHTS_TITLE
            if in_highlights:
                seen_highlights = True
                continue
        (highlights if in_highlights else remainder).append(line)

    if not seen_highlights:
        raise ReleaseNotesError(
            f"Version {version} has no '### {HIGHLIGHTS_TITLE}' block. "
            "Every version needs one — it is what the release note leads with."
        )
    if not any(line.startswith("- ") for line in highlights):
        raise ReleaseNotesError(
            f"Version {version}'s '### {HIGHLIGHTS_TITLE}' block lists nothing."
        )
    if not any(line.strip() for line in remainder):
        raise ReleaseNotesError(
            f"Version {version} carries Highlights and nothing else. "
            "The note would say the same thing twice."
        )

    return trim_blank_edges(highlights), trim_blank_edges(remainder)


def unwrap_soft_breaks(lines):
    """Join each list item's wrapped continuation lines back into one line.

    A release body renders a single newline as a line break, where a file view collapses it, so the
    CHANGELOG's own hard wrap would otherwise break every bullet mid-sentence at the column it was
    authored to rather than at the reader's window. A nested item opens a line of its own.
    """
    joined = []
    for line in lines:
        stripped = line.strip()
        continuation = (
            joined
            and joined[-1].strip()
            and line.startswith((" ", "\t"))
            and not stripped.startswith("- ")
        )
        if continuation:
            joined[-1] = f"{joined[-1].rstrip()} {stripped}"
        else:
            joined.append(line)
    return joined


def trim_blank_edges(lines):
    """Drop leading and trailing blank lines, keeping the interior spacing."""
    start = 0
    end = len(lines)
    while start < end and not lines[start].strip():
        start += 1
    while end > start and not lines[end - 1].strip():
        end -= 1
    return lines[start:end]


def split_entries(section_lines):
    """The top-level list items of one section body.

    An item stops at the heading below it as well as at the next item: where one of these is
    compared against the same item written elsewhere, a heading carried into one copy makes the two
    compare unequal. Any depth ends it rather than `### ` alone, so a sub-subheading cannot carry
    the same defect one level down.
    """
    entries = []
    current = None
    for line in section_lines:
        if ANY_HEADING.match(line):
            current = None
        elif line.startswith("- "):
            current = [line]
            entries.append(current)
        elif current is not None:
            current.append(line)
    return ["\n".join(entry) for entry in entries]


def normalize(text):
    """Collapse the wrapping a CHANGELOG line carries, so two spellings of one sentence match."""
    return " ".join(text.split())


RELATIVE_LINK = re.compile(r"\]\((?!https?://|#)(?P<target>[^)\s]+)\)")


def absolutize_links(text, repo, tag):
    """Point a CHANGELOG-relative link at the release tag.

    The tag is package-at-root, which is the directory the CHANGELOG sits in, so a target written
    relative to the CHANGELOG carries over unchanged.
    """
    return RELATIVE_LINK.sub(
        lambda match: f"](https://github.com/{repo}/blob/{tag}/{match.group('target')})", text
    )


def read_unity_requirement(package_json_path):
    """Return the release floor `package.json` declares: `unity` joined to `unityRelease`."""
    package = json.loads(Path(package_json_path).read_text(encoding="utf-8"))
    unity = package.get("unity")
    if not unity:
        raise ReleaseNotesError(f"{package_json_path} declares no 'unity' version.")
    release = package.get("unityRelease")
    return f"{unity}.{release}" if release else unity


def fragment_section(path):
    """The open section a fragment at `path`, relative to the fragment directory, is filed under, or
    None for a dotfile.

    A dotfile is `.gitkeep`, which keeps an empty directory in the tree, or something an editor or a
    file browser left beside the fragments. Any other file outside the layout is refused rather than
    passed over, since a fragment in a misspelt directory would otherwise ship nowhere.
    """
    parts = Path(path).parts
    if parts[-1].startswith("."):
        return None
    if len(parts) != 2 or parts[0] not in FRAGMENT_SECTIONS or not parts[1].endswith(".md"):
        raise ReleaseNotesError(
            f"{FRAGMENT_PATH}/{Path(path).as_posix()} is not a fragment: a fragment is a .md file "
            f"directly under one of {', '.join(f'{FRAGMENT_PATH}/{name}/' for name in FRAGMENT_SECTIONS)}.")
    return FRAGMENT_SECTIONS[parts[0]]


def parse_fragment(path, text):
    """One fragment's `(kind, lines)` blocks, in the order it writes them."""
    where = f"{FRAGMENT_PATH}/{Path(path).as_posix()}"
    blocks = []
    for number, line in enumerate(text.splitlines(), start=1):
        if line.lstrip().startswith("<!-- corrects:"):
            if not CORRECTS.match(line.rstrip()):
                raise ReleaseNotesError(
                    f"{where}:{number}: a correction is written '{CORRECTS_FORM}', the first line "
                    "opening with its '- '.")
            if not blocks or not any(one.startswith("- ") for one in blocks[-1][1]):
                raise ReleaseNotesError(
                    f"{where}:{number}: a correction sits under the entry that rewrites the one it "
                    "names, and no entry is above this one.")
            continue
        heading = SUBSECTION_HEADING.match(line)
        if heading:
            if heading.group("title") not in FRAGMENT_KINDS:
                raise ReleaseNotesError(
                    f"{where}:{number}: '### {heading.group('title')}' is not a kind a fragment can "
                    f"file under. Use one of {', '.join(FRAGMENT_KINDS)}; the Highlights a version "
                    "leads with are written when it closes.")
            blocks.append((heading.group("title"), []))
        elif ANY_HEADING.match(line):
            raise ReleaseNotesError(
                f"{where}:{number}: a fragment carries '### <kind>' headings and no other.")
        elif blocks:
            blocks[-1][1].append(line)
        elif line.strip():
            raise ReleaseNotesError(
                f"{where}:{number}: this sits above the fragment's first '### <kind>' heading, so no "
                "subsection would carry it.")
    blocks = [(kind, trim_blank_edges(lines)) for kind, lines in blocks]
    if not blocks:
        raise ReleaseNotesError(f"{where} files nothing: open it with a '### <kind>' heading.")
    for kind, lines in blocks:
        if not lines or not lines[0].startswith("- "):
            raise ReleaseNotesError(
                f"{where}: '### {kind}' does not open with a '- ' entry.")
    return blocks


def subsection_rank(title):
    """Where a subsection sits among the others of its section: `FRAGMENT_KINDS` order, Highlights
    ahead of every kind, and a title that is none of them after."""
    if title == HIGHLIGHTS_TITLE:
        return -1
    return FRAGMENT_KINDS.index(title) if title in FRAGMENT_KINDS else len(FRAGMENT_KINDS)


def file_under(lines, section, blocks):
    """`lines` with `blocks`, one per kind, appended to the subsections of `section`, opening that
    section and any subsection it lacks.

    The inline entries a section already holds keep their place ahead of the fragments' own.
    """
    headings = [(index, match.group("version")) for index, line in enumerate(lines)
                if (match := VERSION_HEADING.match(line))]
    open_order = list(FRAGMENT_SECTIONS.values())
    start = next((index for index, version in headings if version == section), None)
    if start is None:
        above = open_order[:open_order.index(section)]
        start = next((index for index, version in headings if version not in above), len(lines))
        opening = ([""] if start and lines[start - 1].strip() else []) + [f"## [{section}]", ""]
        lines = lines[:start] + opening + lines[start:]
        start += len(opening) - 2
        headings = [(index, match.group("version")) for index, line in enumerate(lines)
                    if (match := VERSION_HEADING.match(line))]
    end = next((index for index, _ in headings if index > start), len(lines))

    head, subsections = [], []
    for line in lines[start + 1:end]:
        match = SUBSECTION_HEADING.match(line)
        if match:
            subsections.append([match.group("title"), []])
        elif subsections:
            subsections[-1][1].append(line)
        else:
            head.append(line)

    for kind, entries in blocks:
        existing = next((held for held in subsections if held[0] == kind), None)
        if existing is not None:
            existing[1] = trim_blank_edges(existing[1]) + [""] + entries
            continue
        at = next((index for index, held in enumerate(subsections)
                   if subsection_rank(held[0]) > subsection_rank(kind)), len(subsections))
        subsections.insert(at, [kind, list(entries)])

    body = [""]
    if trim_blank_edges(head):
        body += trim_blank_edges(head) + [""]
    for title, held in subsections:
        body += [f"### {title}", ""] + trim_blank_edges(held) + [""]
    return lines[:start + 1] + body + lines[end:]


def compose(changelog_text, fragments):
    """The CHANGELOG with each fragment's entries filed under the open section its directory names.

    `fragments` is `(path relative to the fragment directory, text)` pairs, filed in path order. With
    none the text comes back as it was: a tree carrying no fragment reads as it always has, and writing
    the composed text over the CHANGELOG and deleting the fragments leaves this returning what it
    returned before.
    """
    placed = {}
    for path, text in sorted(fragments):
        section = fragment_section(path)
        if section is None:
            continue
        kinds = placed.setdefault(section, {})
        for kind, entries in parse_fragment(path, text):
            kinds[kind] = kinds[kind] + [""] + entries if kind in kinds else entries
    if not placed:
        return changelog_text
    lines = changelog_text.splitlines()
    for section in FRAGMENT_SECTIONS.values():
        if section in placed:
            lines = file_under(lines, section, list(placed[section].items()))
    return "\n".join(lines) + "\n"


def fragments_in(directory=DEFAULT_FRAGMENTS):
    """Every file under the fragment directory on disk, as `compose` takes them.

    A dotfile's text is not read: `compose` passes it over, and a file browser's is not text.
    """
    root = Path(directory)
    found = []
    for folder, _, names in os.walk(root):
        for name in names:
            path = Path(folder, name)
            text = "" if name.startswith(".") else path.read_text(encoding="utf-8")
            found.append((path.relative_to(root).as_posix(), text))
    return sorted(found)


def fragments_at(project, rev, timeout=5):
    """Every file under the fragment directory at a revision, as `compose` takes them.

    Raises what `subprocess.run` raises where git cannot answer, `CalledProcessError` for a revision
    it has not got among them; a revision with no fragment directory holds none.
    """
    def git(args, given=None):
        return subprocess.run(["git", "-C", str(project), *args], input=given,
                              capture_output=True, check=True, timeout=timeout).stdout

    blobs = []
    for record in git(["ls-tree", "--full-tree", "-r", "-z", rev, "--", FRAGMENT_PATH]).split(b"\0"):
        if not record:
            continue
        meta, path = record.split(b"\t", 1)
        _, kind, oid = meta.split()
        if kind == b"blob":
            blobs.append((path.decode("utf-8"), oid))
    if not blobs:
        return []
    batch = git(["cat-file", "--batch"], b"".join(oid + b"\n" for _, oid in blobs))
    found, cursor = [], 0
    for path, _ in blobs:
        header_end = batch.index(b"\n", cursor)
        size = int(batch[cursor:header_end].split()[2])
        body = batch[header_end + 1:header_end + 1 + size]
        cursor = header_end + 1 + size + 1
        found.append((path[len(FRAGMENT_PATH) + 1:], body.decode("utf-8")))
    return sorted(found)


def compare_link(repo, previous_tag, tag):
    """Link the diff between releases, falling back to the whole history for the first one."""
    if previous_tag:
        return f"https://github.com/{repo}/compare/{previous_tag}...{tag}"
    return f"https://github.com/{repo}/commits/{tag}"


def build_notes(
    changelog_text,
    version,
    repo,
    install_tag,
    unity_version,
    previous_compare_tag=None,
    compare_tag=None,
):
    section = extract_version_section(changelog_text, version)
    highlights, remainder = split_highlights(section, version)
    highlights, remainder = unwrap_soft_breaks(highlights), unwrap_soft_breaks(remainder)

    package_url = f"https://github.com/{repo}.git#{install_tag}"
    install_guide = f"https://github.com/{repo}/blob/{install_tag}/README.md#installation"
    parts = [
        f"## {HIGHLIGHTS_TITLE}",
        "",
        *highlights,
        "",
        "## Install",
        "",
        "Unity Package Manager ▸ **Add package from git URL**, or in `Packages/manifest.json`:",
        "",
        "```jsonc",
        f'"com.velvet.core": "{package_url}"',
        "```",
        "",
        f"Requires Unity {unity_version} or newer — see [Installation]({install_guide}) for details.",
        "",
        "<details>",
        "<summary><b>Full changelog</b></summary>",
        "",
        *remainder,
        "",
        "</details>",
        "",
        f"**Full Changelog**: {compare_link(repo, previous_compare_tag, compare_tag or install_tag)}",
        "",
    ]
    return absolutize_links("\n".join(parts), repo, install_tag)


def parse_args(argv):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True, help="Version to publish, e.g. 2.0.0")
    parser.add_argument("--repo", required=True, help="owner/name, e.g. s4k10503/velvet")
    parser.add_argument(
        "--tag",
        help="Release tag consumers install (default: v<version>)",
    )
    parser.add_argument(
        "--compare-tag",
        help="Tag the compare link ends at (default: the release tag)",
    )
    parser.add_argument(
        "--previous-compare-tag",
        help="Tag the compare link starts from; omit for the first release",
    )
    parser.add_argument("--changelog", default=str(DEFAULT_CHANGELOG))
    parser.add_argument("--package-json", default=str(DEFAULT_PACKAGE_JSON))
    parser.add_argument("--output", help="Write here instead of stdout")
    parser.add_argument(
        "--dispatch", action="store_true",
        help="Building the note a UPM dispatch publishes: refuse while a fragment remains",
    )
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_args(argv)
    tag = args.tag or f"v{args.version}"
    # The note is built from the file alone, so at a dispatch a fragment still beside it is an entry
    # the release ships and the note leaves out: one merged after the release pull request went green
    # lands here. Asked only there, because a preview or a repair of a published note is built from a
    # tree where fragments waiting for the next version are the ordinary state.
    waiting = [path for path, _ in fragments_in(Path(args.changelog).parent / DEFAULT_FRAGMENTS.name)
               if not Path(path).name.startswith(".")] if args.dispatch else []
    if waiting:
        print(f"error: {len(waiting)} CHANGELOG fragment(s) still sit beside the CHANGELOG, starting "
              f"with {waiting[0]}, and this note would describe none of them. Fold them into the "
              f"version with {FRAGMENT_COMPILER} before its section is renamed, as CONTRIBUTING.md's "
              f"release section gives, and dispatch from that commit.",
              file=sys.stderr)
        return 1
    try:
        notes = build_notes(
            Path(args.changelog).read_text(encoding="utf-8"),
            args.version,
            args.repo,
            tag,
            read_unity_requirement(args.package_json),
            previous_compare_tag=args.previous_compare_tag,
            compare_tag=args.compare_tag,
        )
    except ReleaseNotesError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1

    if args.output:
        Path(args.output).write_text(notes, encoding="utf-8")
    else:
        sys.stdout.write(notes)
    return 0


if __name__ == "__main__":
    sys.exit(main())
