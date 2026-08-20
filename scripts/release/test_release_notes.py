#!/usr/bin/env python3
"""Unit tests for release_notes.py, plus guards over this repository's own CHANGELOG.

Run: python3 scripts/release/test_release_notes.py
"""

import json
import re
import unittest
from pathlib import Path

from published_check import RELEASE_DATE
from release_notes import (
    DEFAULT_CHANGELOG,
    DEFAULT_PACKAGE_JSON,
    ReleaseNotesError,
    VERSION_HEADING,
    build_notes,
    extract_version_section,
    read_unity_requirement,
    split_highlights,
    unwrap_soft_breaks,
)

REPO = "s4k10503/velvet"


def bullets(lines):
    """Split a block of CHANGELOG lines into its top-level list items."""
    return [item for item in re.split(r"\n(?=- )", "\n".join(lines)) if item.startswith("- ")]


def normalize(text):
    """Collapse the wrapping a CHANGELOG line carries, so two spellings of one sentence match."""
    return " ".join(text.split())

COMPLETE = """# Changelog

## [2.0.0] - 2026-08-02

### Highlights

- Something that matters.
- Something else.

### Added

- A long-form entry.

## [1.0.0] - 2026-07-05

### Highlights

- The first release.

### Added

- Everything.
"""

TWO_OPEN = """# Changelog

## [Unreleased]

### Added

- Something a minor may ship.

## [Unreleased — breaking]

### Changed

- Something that waits for a major.

## [1.0.0] - 2026-07-05

### Highlights

- The first release.

### Added

- Everything.
"""


def notes_for(changelog, version="2.0.0", **kwargs):
    kwargs.setdefault("previous_compare_tag", "v1.0.0-main")
    return build_notes(changelog, version, REPO, f"v{version}", "6000.3", **kwargs)


class ExtractVersionSection(unittest.TestCase):
    def test_Given_two_versions_When_extracting_the_later_Then_the_earlier_is_excluded(self):
        # Arrange / Act
        section = "\n".join(extract_version_section(COMPLETE, "2.0.0"))

        # Assert
        self.assertNotIn("The first release.", section)

    def test_Given_two_versions_When_extracting_the_earlier_Then_it_runs_to_the_end_of_file(self):
        # Arrange / Act
        section = "\n".join(extract_version_section(COMPLETE, "1.0.0"))

        # Assert
        self.assertIn("Everything.", section)

    # GREEN_ON_BASE(characterization): the base already ends a section at an undated heading.
    def test_Given_two_open_sections_When_extracting_the_first_Then_the_second_is_excluded(self):
        # Arrange / Act
        section = extract_version_section(TWO_OPEN, "Unreleased")

        # Assert
        self.assertEqual(section, ["", "### Added", "", "- Something a minor may ship.", ""])

    def test_Given_an_unlisted_version_When_extracting_Then_it_raises(self):
        # Arrange / Act / Assert
        with self.assertRaises(ReleaseNotesError):
            extract_version_section(COMPLETE, "3.0.0")


class SplitHighlights(unittest.TestCase):
    def test_Given_a_complete_section_When_splitting_Then_highlights_hold_only_their_own_bullets(self):
        # Arrange
        section = extract_version_section(COMPLETE, "2.0.0")

        # Act
        highlights, _ = split_highlights(section, "2.0.0")

        # Assert
        self.assertEqual(highlights, ["- Something that matters.", "- Something else."])

    def test_Given_a_complete_section_When_splitting_Then_the_remainder_keeps_its_own_heading(self):
        # Arrange
        section = extract_version_section(COMPLETE, "2.0.0")

        # Act
        _, remainder = split_highlights(section, "2.0.0")

        # Assert
        self.assertEqual(remainder, ["### Added", "", "- A long-form entry."])

    def test_Given_a_section_with_no_highlights_When_splitting_Then_it_raises(self):
        # Arrange
        section = ["", "### Added", "", "- A long-form entry."]

        # Act / Assert
        with self.assertRaises(ReleaseNotesError):
            split_highlights(section, "2.0.0")

    def test_Given_a_highlights_heading_listing_nothing_When_splitting_Then_it_raises(self):
        # Arrange
        section = ["", "### Highlights", "", "### Added", "", "- A long-form entry."]

        # Act / Assert
        with self.assertRaises(ReleaseNotesError):
            split_highlights(section, "2.0.0")

    def test_Given_a_section_that_is_only_highlights_When_splitting_Then_it_raises(self):
        # Arrange
        section = ["", "### Highlights", "", "- Something that matters."]

        # Act / Assert
        with self.assertRaises(ReleaseNotesError):
            split_highlights(section, "2.0.0")


class UnwrapSoftBreaks(unittest.TestCase):
    def test_Given_a_wrapped_item_When_unwrapping_Then_it_becomes_one_line(self):
        # Arrange / Act
        unwrapped = unwrap_soft_breaks(["- One sentence", "  wrapped at a column."])

        # Assert
        self.assertEqual(unwrapped, ["- One sentence wrapped at a column."])

    def test_Given_a_nested_item_When_unwrapping_Then_it_keeps_its_own_line(self):
        # Arrange / Act
        unwrapped = unwrap_soft_breaks(["- Parent:", "  - Child one", "  - Child two"])

        # Assert
        self.assertEqual(unwrapped, ["- Parent:", "  - Child one", "  - Child two"])

    def test_Given_a_wrapped_nested_item_When_unwrapping_Then_it_joins_onto_the_child(self):
        # Arrange / Act
        unwrapped = unwrap_soft_breaks(["- Parent:", "  - Child", "    wrapped."])

        # Assert
        self.assertEqual(unwrapped, ["- Parent:", "  - Child wrapped."])

    def test_Given_a_blank_line_between_items_When_unwrapping_Then_the_separation_survives(self):
        # Arrange / Act
        unwrapped = unwrap_soft_breaks(["- One", "", "  Not a continuation of anything."])

        # Assert
        self.assertEqual(unwrapped, ["- One", "", "  Not a continuation of anything."])


class BuildNotes(unittest.TestCase):
    def test_Given_a_complete_section_When_building_Then_highlights_lead_the_note(self):
        # Arrange / Act
        notes = notes_for(COMPLETE)

        # Assert
        self.assertTrue(notes.startswith("## Highlights\n\n- Something that matters."))

    def test_Given_a_complete_section_When_building_Then_the_long_form_entries_are_collapsed(self):
        # Arrange / Act
        notes = notes_for(COMPLETE)

        # Assert
        self.assertIn("<summary><b>Full changelog</b></summary>\n\n### Added", notes)

    def test_Given_a_complete_section_When_building_Then_highlights_stay_out_of_the_collapsed_block(self):
        # Arrange / Act
        body = notes_for(COMPLETE).split("<details>", 1)[1]

        # Assert
        self.assertNotIn("Something that matters.", body)

    def test_Given_a_release_tag_When_building_Then_the_install_url_pins_it(self):
        # Arrange / Act
        notes = notes_for(COMPLETE)

        # Assert
        self.assertIn(f'"com.velvet.core": "https://github.com/{REPO}.git#v2.0.0"', notes)

    def test_Given_a_previous_tag_When_building_Then_the_footer_compares_the_two(self):
        # Arrange / Act
        notes = notes_for(COMPLETE, compare_tag="v2.0.0-main")

        # Assert
        self.assertTrue(
            notes.rstrip().endswith(
                f"**Full Changelog**: https://github.com/{REPO}/compare/v1.0.0-main...v2.0.0-main"
            )
        )

    def test_Given_no_previous_tag_When_building_Then_the_footer_links_the_whole_history(self):
        # Arrange / Act
        notes = build_notes(COMPLETE, "1.0.0", REPO, "v1.0.0", "6000.3")

        # Assert
        self.assertTrue(
            notes.rstrip().endswith(f"**Full Changelog**: https://github.com/{REPO}/commits/v1.0.0")
        )

    def test_Given_a_unity_requirement_When_building_Then_the_note_states_it(self):
        # Arrange / Act
        notes = notes_for(COMPLETE)

        # Assert
        self.assertIn("Requires Unity 6000.3 or newer", notes)

    def test_Given_the_install_snippet_When_building_Then_the_peer_dependency_is_named(self):
        # Arrange — installing from the snippet alone does not compile without UniTask, which
        # package.json deliberately does not declare.
        notes = notes_for(COMPLETE)

        # Act / Assert
        self.assertIn(
            f"[UniTask](https://github.com/Cysharp/UniTask) already in the project — see "
            f"[Installation](https://github.com/{REPO}/blob/v2.0.0/README.md#installation)",
            notes,
        )

    def test_Given_a_changelog_relative_link_When_building_Then_it_points_at_the_tag(self):
        # Arrange
        changelog = COMPLETE.replace(
            "- A long-form entry.", "- See [the guide](Documentation~/motion.md)."
        )

        # Act
        notes = notes_for(changelog)

        # Assert
        self.assertIn(
            f"[the guide](https://github.com/{REPO}/blob/v2.0.0/Documentation~/motion.md)", notes
        )

    def test_Given_an_absolute_link_When_building_Then_it_is_left_alone(self):
        # Arrange
        changelog = COMPLETE.replace(
            "- A long-form entry.", "- See [the repo](https://github.com/other/repo)."
        )

        # Act
        notes = notes_for(changelog)

        # Assert
        self.assertIn("[the repo](https://github.com/other/repo)", notes)


class ThisRepositorysChangelog(unittest.TestCase):
    """The guards that make a release fail here rather than publish an empty note."""

    @classmethod
    def setUpClass(cls):
        cls.text = Path(DEFAULT_CHANGELOG).read_text(encoding="utf-8")
        cls.headings = [
            (match.group("version"), line)
            for line in cls.text.splitlines()
            if (match := VERSION_HEADING.match(line))
        ]
        # An in-progress section is not a release and has nothing to summarize yet; requiring
        # Highlights of it would demand a rewritten summary on every merge. Renaming it to a
        # version is what brings it under these guards, on the pull request that renames it. Both
        # open sections are in-progress, so both stand outside them until one is renamed.
        cls.versions = [version for version, _ in cls.headings
                        if version not in ("Unreleased", "Unreleased — breaking")]

    def test_Given_the_shipped_changelog_When_reading_Then_it_lists_versions(self):
        # Arrange / Act / Assert — every case below is vacuous on an empty list.
        self.assertGreater(len(self.versions), 0)

    def test_Given_every_listed_version_When_building_its_note_Then_none_raises(self):
        # Arrange
        unity = read_unity_requirement(DEFAULT_PACKAGE_JSON)

        # Act
        failures = []
        for version in self.versions:
            try:
                build_notes(self.text, version, REPO, f"v{version}", unity)
            except ReleaseNotesError as error:
                failures.append(f"{version}: {error}")

        # Assert
        self.assertEqual(failures, [])

    def test_Given_the_packaged_version_When_looking_it_up_Then_the_changelog_documents_it(self):
        # Arrange
        version = json.loads(Path(DEFAULT_PACKAGE_JSON).read_text(encoding="utf-8"))["version"]

        # Act / Assert
        self.assertIn(version, self.versions)

    def test_Given_every_version_When_comparing_its_two_halves_Then_no_highlight_is_a_copy(self):
        # Arrange — length does not separate the two (the shortest long-form entry here is 61
        # characters), so the guard is verbatim reuse: a highlight that IS its own entry says the
        # same thing twice in one note.
        copied = []
        for version in self.versions:
            highlights, remainder = split_highlights(
                extract_version_section(self.text, version), version
            )
            entries = {normalize(bullet) for bullet in bullets(remainder)}
            for bullet in bullets(highlights):
                if normalize(bullet) in entries:
                    copied.append(f"{version}: {bullet.splitlines()[0][:60]}…")

        # Act / Assert
        self.assertEqual(copied, [])

    def test_Given_the_shipped_changelog_When_reading_Then_the_breaking_section_is_open(self):
        # Arrange — dating this heading makes it a released section of its own; deleting it
        # merges what it holds into the section above, which at a release is the version being
        # closed. Moving entries out changes no heading: the not-empty case below sees that only
        # where every entry goes at once.
        # Act
        open_breaking = [version for version, line in self.headings
                         if version == "Unreleased — breaking" and not RELEASE_DATE.search(line)]

        # Assert
        self.assertEqual(open_breaking, ["Unreleased — breaking"])

    def entries_waiting_for_a_major(self):
        """What the breaking section lists, or nothing where the section itself has gone missing —
        which the case above reports, so reading it here would name one defect twice."""
        if not any(version == "Unreleased — breaking" for version, _ in self.headings):
            return set()
        return {normalize(bullet) for bullet in
                bullets(extract_version_section(self.text, "Unreleased — breaking"))}

    # GREEN_ON_BASE(characterization): the base names nothing as waiting for a major.
    def test_Given_the_breaking_section_When_reading_every_other_section_Then_none_repeats_it(self):
        # Arrange — closing a major moves entries out of this section. One copied rather than moved
        # both ships and goes on waiting, and reads as true in each place; copied into the open
        # minor section instead, it is the same defect one release earlier.
        waiting = self.entries_waiting_for_a_major()
        elsewhere = [version for version, _ in self.headings
                     if version != "Unreleased — breaking"]

        # Act
        copied = [f"{version}: {bullet.splitlines()[0][:60]}…"
                  for version in elsewhere
                  for bullet in bullets(extract_version_section(self.text, version))
                  if normalize(bullet) in waiting]

        # Assert
        self.assertEqual(copied, [])

    def test_Given_the_breaking_section_When_reading_Then_it_is_not_empty(self):
        # Arrange — every entry moved out at once, heading left standing, is how what waits for a
        # major reaches a minor's note with no heading changed. It empties what the case above
        # compares against too, so that one falls silent on the same edit. Draining at a major
        # leaves the standing line CONTRIBUTING.md's release section describes.
        # Act
        waiting = self.entries_waiting_for_a_major()

        # Assert
        self.assertGreater(len(waiting), 0)

    # GREEN_ON_BASE(characterization): the only major bump here already names its breaks.
    def test_Given_a_major_release_When_reading_its_highlights_Then_they_name_what_breaks(self):
        # Arrange — draining the breaking section into the major that ships it is a manual step, and
        # a major whose note names no break is what skipping it looks like from the published side.
        majors = [version for version, previous in zip(self.versions, self.versions[1:])
                  if version.split(".")[0] != previous.split(".")[0]]

        # Act
        silent = [version for version in majors
                  if "**Breaking:**" not in "\n".join(
                      split_highlights(extract_version_section(self.text, version), version)[0])]

        # Assert
        self.assertEqual(silent, [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
