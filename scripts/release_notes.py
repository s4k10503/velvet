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
import re
import sys
from pathlib import Path

VERSION_HEADING = re.compile(r"^## +\[(?P<version>[^\]]+)\]")
SUBSECTION_HEADING = re.compile(r"^### +(?P<title>.+?)\s*$")

HIGHLIGHTS_TITLE = "Highlights"

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_CHANGELOG = REPO_ROOT / "Packages" / "com.velvet.core" / "CHANGELOG.md"
DEFAULT_PACKAGE_JSON = REPO_ROOT / "Packages" / "com.velvet.core" / "package.json"


class ReleaseNotesError(Exception):
    """A CHANGELOG that cannot produce a complete note."""


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
    """Return the `unity` field, which is the minimum editor version a consumer needs."""
    package = json.loads(Path(package_json_path).read_text(encoding="utf-8"))
    unity = package.get("unity")
    if not unity:
        raise ReleaseNotesError(f"{package_json_path} declares no 'unity' version.")
    return unity


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
    # UniTask is a peer dependency package.json deliberately does not declare, so an install from
    # this snippet alone does not compile. The README owns the reason and the full manifest.
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
        f"Requires Unity {unity_version} or newer, and [UniTask](https://github.com/Cysharp/UniTask) "
        f"already in the project — see [Installation]({install_guide}) for the full manifest.",
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
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_args(argv)
    tag = args.tag or f"v{args.version}"
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
